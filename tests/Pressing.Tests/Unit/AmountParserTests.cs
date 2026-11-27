using Pressing.Client;

namespace Pressing.Tests.Unit;

/// <summary>Formats de montants saisis au clavier (correctif D04, durcissement T4.0).</summary>
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
    [InlineData("1,500", 1500)]
    [InlineData("0.250", 0.25)]
    [InlineData("0,250", 0.25)]
    [InlineData("1.500.000", 1500000)]
    [InlineData("1 500 000", 1500000)]
    [InlineData("12500 F CFA", 12500)]
    [InlineData("5000f", 5000)]
    [InlineData("-1,500", -1500)]
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
    [InlineData("15O0")]
    [InlineData("12 abc 500")]
    [InlineData("12-500")]
    [InlineData("1,50,50")]
    [InlineData("1.2.3")]
    public void TryParseAmount_refuse_les_saisies_sans_montant_ou_ambigues(string input) =>
        Assert.False(AmountParser.TryParseAmount(input, out _));
}