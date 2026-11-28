using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Assistant;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>
/// Routes /api/assistant/* avec des moteurs factices (aucun moteur IA réel) : droits, taille des fichiers,
/// scénario nominal, indisponibilité, audit et limitation de débit.
/// </summary>
[Collection("Api")]
public class AssistantApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AssistantApiTests(ApiFactory factory) => _factory = factory;

    // ---------- Outils ----------

    private WebApplicationFactory<Program> Host(
        StubSpeechToText? stt = null, StubIntentExtractor? llm = null, AssistantOptions? options = null) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISpeechToText>();
            services.RemoveAll<IIntentExtractor>();
            if (stt is not null) services.AddSingleton<ISpeechToText>(stt);
            if (llm is not null) services.AddSingleton<IIntentExtractor>(llm);
            if (options is not null) services.AddSingleton(options);
        }));

    private static async Task<PressingApiClient> CashierAsync(WebApplicationFactory<Program> host)
    {
        var api = new PressingApiClient(host.CreateClient());
        await api.LoginStaffAsync("caisse", ApiFactory.CashierPassword);
        return api;
    }

    private static byte[] Wav(int length = 64)
    {
        byte[] bytes = new byte[length];
        "RIFF"u8.CopyTo(bytes);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static async Task<ApiException> FailureAsync(Func<Task> action) =>
        await Assert.ThrowsAsync<ApiException>(action);

    // ---------- Droits ----------

    [Fact]
    public async Task Un_client_du_portail_recoit_403_sur_les_trois_routes()
    {
        using var host = Host(new StubSpeechToText(), new StubIntentExtractor());
        var portal = new PressingApiClient(host.CreateClient());
        await portal.LoginClientAsync("+225 07 11 22 33 44", ApiFactory.ClientPassword);

        Assert.True((await FailureAsync(() => portal.GetAssistantStatusAsync())).IsForbidden);
        Assert.True((await FailureAsync(() => portal.TranscribeAssistantAsync(Wav()))).IsForbidden);
        Assert.True((await FailureAsync(() => portal.InterpretAssistantAsync("dépôt"))).IsForbidden);
    }

    [Fact]
    public async Task Sans_jeton_les_trois_routes_repondent_401()
    {
        using var host = Host(new StubSpeechToText(), new StubIntentExtractor());
        var anonymous = new PressingApiClient(host.CreateClient());

        Assert.True((await FailureAsync(() => anonymous.GetAssistantStatusAsync())).IsUnauthorized);
        Assert.True((await FailureAsync(() => anonymous.TranscribeAssistantAsync(Wav()))).IsUnauthorized);
        Assert.True((await FailureAsync(() => anonymous.InterpretAssistantAsync("dépôt"))).IsUnauthorized);
    }

    // ---------- État ----------

    [Fact]
    public async Task Status_indique_que_les_moteurs_sont_prets()
    {
        using var host = Host(new StubSpeechToText(), new StubIntentExtractor());
        var api = await CashierAsync(host);

        AssistantStatusDto status = (await api.GetAssistantStatusAsync())!;

        Assert.True(status.Enabled);
        Assert.True(status.SpeechToTextReady);
        Assert.True(status.LanguageModelReady);
        Assert.Equal(0.6, status.ConfidenceThreshold);
        Assert.Null(status.Message);
    }

    [Fact]
    public async Task Status_explique_pourquoi_l_assistant_est_inutilisable()
    {
        using (var stopped = Host(new StubSpeechToText { Ready = false }, new StubIntentExtractor()))
        {
            AssistantStatusDto status = (await (await CashierAsync(stopped)).GetAssistantStatusAsync())!;
            Assert.False(status.SpeechToTextReady);
            Assert.Contains("n'est pas démarré", status.Message);
        }

        using (var disabled = Host(new StubSpeechToText(), new StubIntentExtractor(), new AssistantOptions { Enabled = false }))
        {
            AssistantStatusDto status = (await (await CashierAsync(disabled)).GetAssistantStatusAsync())!;
            Assert.False(status.Enabled);
            Assert.False(status.SpeechToTextReady);
            Assert.Contains("désactivé", status.Message);
        }

        using (var none = Host())
        {
            AssistantStatusDto status = (await (await CashierAsync(none)).GetAssistantStatusAsync())!;
            Assert.Contains("n'est pas installé", status.Message);
        }
    }

    // ---------- Transcription ----------

    [Fact]
    public async Task Transcription_nominale_renvoie_le_texte_reconnu()
    {
        var stt = new StubSpeechToText { Text = "  déposer trois chemises  " };
        using var host = Host(stt, new StubIntentExtractor());

        AssistantTranscriptionDto result = (await (await CashierAsync(host)).TranscribeAssistantAsync(Wav()))!;

        Assert.Equal("déposer trois chemises", result.Text);
        Assert.True(result.DurationMs >= 0);
        Assert.Equal(1, stt.Calls);
    }

    [Fact]
    public async Task Fichier_de_plus_de_2_Mo_est_refuse_avec_400_et_2_Mo_pile_est_accepte()
    {
        var stt = new StubSpeechToText();
        using var host = Host(stt, new StubIntentExtractor());
        var api = await CashierAsync(host);

        ApiException tooBig = await FailureAsync(() => api.TranscribeAssistantAsync(Wav(AssistantContract.MaxAudioBytes + 1)));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(0, stt.Calls);

        AssistantTranscriptionDto ok = (await api.TranscribeAssistantAsync(Wav(AssistantContract.MaxAudioBytes)))!;
        Assert.False(string.IsNullOrEmpty(ok.Text));
        Assert.Equal(1, stt.Calls);
    }

    [Fact]
    public async Task Fichier_vide_ou_qui_n_est_pas_un_wav_est_refuse_avec_400()
    {
        var stt = new StubSpeechToText();
        using var host = Host(stt, new StubIntentExtractor());
        var api = await CashierAsync(host);

        ApiException empty = await FailureAsync(() => api.TranscribeAssistantAsync([]));
        ApiException notWav = await FailureAsync(() => api.TranscribeAssistantAsync("ceci n'est pas un enregistrement"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notWav.StatusCode);
        Assert.Equal(0, stt.Calls);
    }

    [Fact]
    public async Task Moteur_de_reconnaissance_arrete_donne_503_avec_un_message_clair()
    {
        using var host = Host(new StubSpeechToText { Stopped = true }, new StubIntentExtractor());

        ApiException error = await FailureAsync(async () => await (await CashierAsync(host)).TranscribeAssistantAsync(Wav()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Contains("L'assistant vocal n'est pas démarré", error.Message);
    }

    // ---------- Interprétation ----------

    [Fact]
    public async Task Interpretation_nominale_renvoie_un_brouillon_chiffre_avec_les_prix_du_catalogue()
    {
        var llm = new StubIntentExtractor();
        using var host = Host(new StubSpeechToText(), llm);

        OrderDraftDto draft = (await (await CashierAsync(host)).InterpretAssistantAsync("  deux chemises lavage repassage  "))!;

        Assert.Equal(AssistantTarget.NewOrder, draft.Target);
        Assert.Equal("deux chemises lavage repassage", draft.Transcript);
        Assert.Equal("Chemise", draft.Items.Single().CategoryName);
        Assert.Equal(1000m, draft.Items.Single().UnitPrice);
        Assert.Equal(2000m, draft.Quote!.Total);
        Assert.True(draft.Client!.IsNew);
        Assert.Contains(draft.Warnings, w => w.Contains("Client inconnu"));

        // Le catalogue est fourni au moteur, pas appris par lui.
        Assert.Equal("deux chemises lavage repassage", llm.LastText);
        Assert.Contains("Chemise", llm.LastHints!.Categories);
        Assert.Contains("Lavage + repassage", llm.LastHints.Services);
    }

    [Fact]
    public async Task Interpretation_sous_le_seuil_de_confiance_donne_je_n_ai_pas_compris()
    {
        var llm = new StubIntentExtractor { Intent = new AssistantIntentDto(Intent: AssistantIntents.CreateOrder, Confidence: 0.3) };
        using var host = Host(new StubSpeechToText(), llm);

        OrderDraftDto draft = (await (await CashierAsync(host)).InterpretAssistantAsync("euh"))!;

        Assert.Equal(AssistantTarget.NotUnderstood, draft.Target);
        Assert.Empty(draft.Items);
    }

    [Fact]
    public async Task Texte_vide_ou_trop_long_est_refuse_avec_400()
    {
        var llm = new StubIntentExtractor();
        using var host = Host(new StubSpeechToText(), llm);
        var api = await CashierAsync(host);

        ApiException empty = await FailureAsync(() => api.InterpretAssistantAsync("   "));
        ApiException tooLong = await FailureAsync(() => api.InterpretAssistantAsync(new string('a', AssistantContract.MaxTextLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Null(llm.LastText);
    }

    [Fact]
    public async Task Assistant_desactive_ou_modele_arrete_donnent_503()
    {
        using (var disabled = Host(new StubSpeechToText(), new StubIntentExtractor(), new AssistantOptions { Enabled = false }))
        {
            var api = await CashierAsync(disabled);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await FailureAsync(() => api.InterpretAssistantAsync("dépôt"))).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await FailureAsync(() => api.TranscribeAssistantAsync(Wav()))).StatusCode);
        }

        using (var stopped = Host(new StubSpeechToText(), new StubIntentExtractor { Stopped = true }))
        {
            ApiException error = await FailureAsync(async () => await (await CashierAsync(stopped)).InterpretAssistantAsync("dépôt"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
            Assert.Contains("n'est pas démarré", error.Message);
        }
    }

    [Fact]
    public async Task Sans_moteurs_installes_les_routes_repondent_503()
    {
        using var host = Host();
        var api = await CashierAsync(host);

        ApiException error = await FailureAsync(() => api.InterpretAssistantAsync("dépôt"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Contains("n'est pas installé", error.Message);
    }

    // ---------- Audit, lecture seule, limitation de débit ----------

    [Fact]
    public async Task Les_appels_sont_audites_sans_jamais_creer_de_commande()
    {
        using var host = Host(new StubSpeechToText(), new StubIntentExtractor());
        var api = await CashierAsync(host);

        int ordersBefore, auditBefore;
        using (IServiceScope scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PressingDbContext>();
            ordersBefore = await db.Orders.CountAsync();
            auditBefore = await db.AuditLogs.CountAsync(a => a.Action == "assistant");
        }

        await api.TranscribeAssistantAsync(Wav());
        await api.InterpretAssistantAsync("deux chemises lavage repassage pour Mme Zorro");

        using (IServiceScope scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PressingDbContext>();
            Assert.Equal(ordersBefore, await db.Orders.CountAsync());
            Assert.Equal(auditBefore + 2, await db.AuditLogs.CountAsync(a => a.Action == "assistant"));
            Assert.Contains(
                await db.AuditLogs.Where(a => a.Action == "assistant").Select(a => a.Details).ToListAsync(),
                d => d != null && d.Contains("Transcription"));
        }
    }

    [Fact]
    public async Task Au_dela_de_30_requetes_par_minute_l_API_repond_429()
    {
        using var host = Host(new StubSpeechToText(), new StubIntentExtractor());
        var api = await CashierAsync(host);

        for (int i = 0; i < 30; i++) await api.GetAssistantStatusAsync();
        ApiException limited = await FailureAsync(() => api.GetAssistantStatusAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
