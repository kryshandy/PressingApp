using System.Net;
using System.Text.RegularExpressions;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

public sealed class Contact503Tests
{
    [Fact]
    public async Task Contact_WhenApiReturns503_PreservesFormValues()
    {
        using var factory = new WebFactory();
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/contact");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var html = await getResponse.Content.ReadAsStringAsync();

        var tokenMatch = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"");

        Assert.True(tokenMatch.Success, "Le jeton antifalsification est absent.");

        const string name = "Test Contact";
        const string email = "test.contact@example.com";
        const string phone = "690123456";
        const string message = "Ceci est un message de test pour vérifier la conservation du formulaire.";

        var form = new Dictionary<string, string>
        {
            ["Name"] = name,
            ["Email"] = email,
            ["Phone"] = phone,
            ["Message"] = message,
            ["Website"] = "",
            ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value
        };

        var postResponse = await client.PostAsync(
            "/contact",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var result = await postResponse.Content.ReadAsStringAsync();
        var decodedResult = System.Net.WebUtility.HtmlDecode(result);
        Assert.Contains(name, decodedResult);
        Assert.Contains(email, decodedResult);
        Assert.Contains(phone, decodedResult);
        Assert.Contains(message, decodedResult);
        Assert.Contains("Votre message est conservé dans le formulaire", decodedResult);
    }
}
