namespace Pressing.Core.Assistant;

/// <summary>Appel HTTP local avec un seul nouvel essai et un délai maximal explicite.</summary>
public abstract class HttpAssistantClientBase
{
    protected HttpAssistantClientBase(HttpClient httpClient)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    protected HttpClient HttpClient { get; }

    protected async Task<HttpResponseMessage> SendWithOneRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using HttpRequestMessage request = requestFactory();
                HttpResponseMessage response = await HttpClient.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, timeout.Token);

                if ((int)response.StatusCode >= 500 && attempt == 0)
                {
                    response.Dispose();
                    continue;
                }
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 0) continue;
                throw new AssistantEngineUnavailableException(AssistantMessages.EngineTimeout);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 0) continue;
                throw new AssistantEngineUnavailableException(AssistantMessages.EngineNotStarted, ex);
            }
        }

        throw new AssistantEngineUnavailableException("Le moteur IA local a renvoyé une erreur interne.");
    }

    protected static async Task<string> ReadSuccessAsync(HttpResponseMessage response, string context)
    {
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new AssistantEngineUnavailableException(
                $"{context} : le moteur a répondu {(int)response.StatusCode}. {body}");
        }
        return body;
    }
}
