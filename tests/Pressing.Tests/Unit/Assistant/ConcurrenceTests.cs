using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

/// <summary>
/// T1.5 : la chaîne reste correcte sous plusieurs appels simultanés. La sérialisation
/// réelle est imposée par les serveurs locaux (-np 1 / -p 1), vérifiée statiquement
/// ci-dessous ; ce test prouve l'absence de contamination côté pipeline C#.
/// </summary>
public class ConcurrenceTests
{
    private const int Calls = 10;

    [Fact]
    public async Task Dix_appels_simultanes_produisent_dix_brouillons_corrects()
    {
        const string transcript = "Dépôt pour trois chemises.";
        var pipeline = new LocalAssistantPipeline(
            new FakeSpeechToText { Default = new SpeechTranscription(transcript, 1.0, 800, false) },
            new FakeIntentExtractor { Default = AssistantTestData.Extraction() },
            new AssistantOptions());
        CatalogHints catalog = AssistantTestData.Catalog();
        MemoryStream[] wavs = Enumerable.Range(0, Calls)
            .Select(_ => new MemoryStream(AssistantTestData.Wav(0.1)))
            .ToArray();

        try
        {
            Task<PipelineResult>[] tasks = wavs
                .Select(wav => pipeline.ProcessAsync(wav, catalog))
                .ToArray();

            PipelineResult[] results = await Task.WhenAll(tasks);

            Assert.Equal(Calls, results.Length);
            Assert.All(results, result =>
            {
                Assert.Equal(PipelineStatus.Ok, result.Status);
                Assert.Equal(transcript, result.Transcript);
                Assert.NotNull(result.Extraction);
                Assert.Equal(AssistantIntentions.CreateOrder, result.Extraction!.Payload.Intent);
                Assert.Equal(3, result.Extraction.Payload.Items[0].Quantity);
            });
        }
        finally
        {
            foreach (MemoryStream wav in wavs)
            {
                wav.Dispose();
            }
        }
    }

    [Fact]
    public void Les_serveurs_locaux_imposent_une_seule_inference_a_la_fois()
    {
        string script = File.ReadAllText(FindRepoFile(Path.Combine(
            "tools", "assistant", "start-ai.ps1")));

        Assert.Contains("'-np', '1'", script);
        Assert.Contains("'-p', '1'", script);
        Assert.Contains("--host', '127.0.0.1'", script);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Fichier du dépôt introuvable : {relativePath}");
    }
}
