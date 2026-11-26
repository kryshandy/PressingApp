using Pressing.Core.Security;

namespace Pressing.Tests.Unit;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+225 07 11 22 33 44", "0711223344")]
    [InlineData("07-11.22 33 44", "0711223344")]
    [InlineData("  (225) 07 11  ", "2250711")]
    [InlineData("+225+07", "22507")]
    [InlineData("07+11", "0711")]
    public void Normalize_garde_les_chiffres_et_retire_l_indicatif_ivoirien(string input, string expected) =>
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

    [Theory]
    [InlineData("+225 05 55 66 77 88")]
    [InlineData("00225 0555667788")]
    [InlineData("2250555667788")]
    [InlineData("05 55 66 77 88")]
    public void Normalize_les_quatre_formats_donnent_le_meme_numero_canonique(string input) =>
        Assert.Equal("0555667788", PhoneNormalizer.Normalize(input));

    [Fact]
    public void Normalize_ne_touche_pas_aux_numeros_etrangers_ou_partiels()
    {
        Assert.Equal("33612345678", PhoneNormalizer.Normalize("+33 6 12 34 56 78"));
        Assert.Equal("225071", PhoneNormalizer.Normalize("225 07 1"));
    }
}
