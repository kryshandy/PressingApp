using Pressing.Client;

namespace Pressing.Tests.Unit;

public class FmtTests
{
    private const string Nbsp = " ";

    [Theory]
    [InlineData(0, "0" + Nbsp + "FCFA")]
    [InlineData(500, "500" + Nbsp + "FCFA")]
    [InlineData(12500, "12" + Nbsp + "500" + Nbsp + "FCFA")]
    [InlineData(1234567, "1" + Nbsp + "234" + Nbsp + "567" + Nbsp + "FCFA")]
    public void Money_formate_avec_espaces_insecables(int amount, string expected) =>
        Assert.Equal(expected, Fmt.Money(amount));

    [Fact]
    public void Money_arrondit_sans_decimales()
    {
        Assert.Equal("1" + Nbsp + "235" + Nbsp + "FCFA", Fmt.Money(1234.5m));
        Assert.Equal("1" + Nbsp + "234" + Nbsp + "FCFA", Fmt.Money(1234.4m));
    }

    [Fact]
    public void Money_gere_les_montants_negatifs() =>
        Assert.Equal("-1" + Nbsp + "000" + Nbsp + "FCFA", Fmt.Money(-1000m));

    [Fact]
    public void Date_utilise_le_format_francais() =>
        Assert.Equal("05/03/2026", Fmt.Date(new DateTime(2026, 3, 5)));

    [Fact]
    public void DateTime_inclut_heure_et_minutes() =>
        Assert.Equal("05/03/2026 14:07", Fmt.DateTime(new DateTime(2026, 3, 5, 14, 7, 0)));
}
