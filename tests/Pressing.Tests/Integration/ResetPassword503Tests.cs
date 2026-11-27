using System.Net;
using System.Text.RegularExpressions;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

public sealed class ResetPassword503Tests
{
    [Fact]
    public async Task Reset_WhenApiReturns503_PreservesTokenInRedirect()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        const string token = "test-token-503-abc";

        var getResponse = await client.GetAsync(
            "/compte/reinitialiser?token=" + Uri.EscapeDataString(token));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var html = await getResponse.Content.ReadAsStringAsync();

        var tokenMatch = Regex.Match(
            html,
            "name=\"Token\"[^>]*value=\"([^\"]*)\"");

        var antiforgeryMatch = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"");

        Assert.True(tokenMatch.Success, "Le champ Token est absent du formulaire.");
        Assert.True(antiforgeryMatch.Success, "Le jeton antifalsification est absent.");

        var form = new Dictionary<string, string>
        {
            ["Token"] = tokenMatch.Groups[1].Value,
            ["NewPassword"] = "Nouveau@123",
            ["ConfirmPassword"] = "Nouveau@123",
            ["__RequestVerificationToken"] = antiforgeryMatch.Groups[1].Value
        };

        var postResponse = await client.PostAsync(
            "/compte/reinitialiser",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Found, postResponse.StatusCode);
        Assert.NotNull(postResponse.Headers.Location);

        var redirectedUrl = Uri.UnescapeDataString(
            postResponse.Headers.Location!.OriginalString);

        Assert.Contains("token=" + token, redirectedUrl);
    }
}
