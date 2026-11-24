using Pressing.Core.Security;

namespace Pressing.Tests.Unit;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+225 07 11 22 33 44", "+2250711223344")]
    [InlineData("07-11.22 33 44", "0711223344")]
    [InlineData("  (225) 07 11  ", "2250711")]
    [InlineData("+225+07", "+22507")]
    [InlineData("07+11", "0711")]
    public void Normalize_garde_chiffres_et_plus_initial(string input, string expected) =>
        Assert.Equal(expected, PhoneNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_vide_donne_chaine_vide(string? input) =>
        Assert.Equal(string.Empty, PhoneNormalizer.Normalize(input));

    [Fact]
    public void Normalize_est_idempotent()
    {
        string once = PhoneNormalizer.Normalize("+225 05 22 33 44 55");
        Assert.Equal(once, PhoneNormalizer.Normalize(once));
    }
}
