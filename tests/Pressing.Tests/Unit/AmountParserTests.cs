using Pressing.Client;

namespace Pressing.Tests.Unit;

/// <summary>Formats de montants saisis au clavier (correctif D04).</summary>
public class AmountParserTests
{
    [Theory]
    [InlineData("1.500,50", 1500.50)]
    [InlineData("1500,50", 1500.50)]
    [InlineData("1 500,50", 1500.50)]
    [InlineData("1\u00A0500,50", 1500.50)]
    [InlineData("1500.50", 1500.50)]
    [InlineData("1500,5", 1500.5)]
    [InlineData("1.500", 1500)]
    [InlineData("12 500 FCFA", 12500)]
    [InlineData("-1500,50", -1500.50)]
    public void TryParseAmount_comprend_les_formats_francais(string input, decimal expected)
    {
        Assert.True(AmountParser.TryParseAmount(input, out decimal value), input);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("FCFA")]
    public void TryParseAmount_refuse_les_saisies_sans_montant(string input) =>
        Assert.False(AmountParser.TryParseAmount(input, out _));
}
