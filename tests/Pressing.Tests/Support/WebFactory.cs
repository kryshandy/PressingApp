extern alias WebApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pressing.Client;

namespace Pressing.Tests.Support;

public sealed class WebFactory : WebApplicationFactory<WebApp::Pressing.Web.WebEntryPoint>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<PressingApiClient>();

            services.AddScoped(_ =>
            {
                var handler = new ServiceUnavailableHandler();
                var http = new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://fake-api/")
                };
                return new PressingApiClient(http);
            });
        });
    }

    private sealed class ServiceUnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    """{"message":"Service temporairement indisponible"}""")
            });
        }
    }
}
