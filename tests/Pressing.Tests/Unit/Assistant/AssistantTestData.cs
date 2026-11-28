using System.Net;
using System.Text;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

internal static class AssistantTestData
{
    public static CatalogHints Catalog() => new(
        new[] { "Chemise", "Costume" },
        new[] { "Lavage + repassage", "Nettoyage à sec" },
        new DateOnly(2026, 11, 27));

    public static string ValidJson(
        string intent = AssistantIntentions.CreateOrder,
        double confidence = 0.92,
        string category = "Chemise",
        string service = "Lavage + repassage",
        string transcript = "Dépôt pour trois chemises.") =>
        $$"""
        {
          "intent": "{{intent}}",
          "client": { "name": null, "phone": null },
          "items": [
            { "category": "{{category}}", "service": "{{service}}", "quantity": 3, "express": false }
          ],
          "ticket": null,
          "amount": 5000,
          "payment_method": null,
          "deposit": 5000,
          "pickup_date": null,
          "item_status": null,
          "confidence": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "transcript": "{{transcript}}"
        }
        """;

    public static IntentExtraction Extraction(string? json = null, CatalogHints? catalog = null)
    {
        json ??= ValidJson();
        var result = IntentValidator.Validate(json, catalog ?? Catalog());
        return new IntentExtraction(json, result.Payload, result.Warnings);
    }

    public static byte[] Wav(double seconds, short amplitude = 800, int sampleRate = 16000, short channels = 1)
    {
        int samples = (int)(seconds * sampleRate * channels);
        int dataLength = samples * 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        for (int i = 0; i < samples; i++)
        {
            writer.Write(i % 2 == 0 ? amplitude : (short)-amplitude);
        }
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] NonPcmWav()
    {
        byte[] bytes = Wav(0.1);
        bytes[20] = 3; // IEEE float au lieu de PCM.
        return bytes;
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public int CallCount { get; private set; }
    public List<string> Bodies { get; } = [];
    public List<Uri> Uris { get; } = [];

    public StubHandler EnqueueJson(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

    public StubHandler Enqueue(HttpResponseMessage response) =>
        Enqueue((_, _) => Task.FromResult(response));

    public StubHandler Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public StubHandler Throw(HttpRequestException exception) =>
        Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));

    public StubHandler Timeout() =>
        Enqueue((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException()));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        Uris.Add(request.RequestUri!);
        if (request.Content is not null)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Aucune réponse HTTP factice configurée.");
        }
        return await _responses.Dequeue()(request, cancellationToken);
    }
}
