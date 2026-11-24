using Pressing.Core.Security;

namespace Pressing.Tests.Unit;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_puis_Verify_accepte_le_bon_mot_de_passe()
    {
        string hash = PasswordHasher.Hash("Secret@2026!");
        Assert.StartsWith("PBKDF2$", hash);
        Assert.True(PasswordHasher.Verify("Secret@2026!", hash));
    }

    [Fact]
    public void Verify_refuse_un_mauvais_mot_de_passe()
    {
        string hash = PasswordHasher.Hash("Secret@2026!");
        Assert.False(PasswordHasher.Verify("secret@2026!", hash));
        Assert.False(PasswordHasher.Verify("Autre", hash));
    }

    [Fact]
    public void Hash_est_sale_deux_hash_du_meme_mot_de_passe_different()
    {
        string a = PasswordHasher.Hash("Secret@2026!");
        string b = PasswordHasher.Hash("Secret@2026!");
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("Secret@2026!", a));
        Assert.True(PasswordHasher.Verify("Secret@2026!", b));
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("pwd", null)]
    [InlineData("pwd", "")]
    [InlineData("pwd", "pas-un-hash")]
    [InlineData("pwd", "PBKDF2$abc$@@@$@@@")]
    [InlineData("pwd", "MD5$1$AAAA$AAAA")]
    public void Verify_refuse_les_entrees_invalides(string? password, string? hash) =>
        Assert.False(PasswordHasher.Verify(password, hash));

    [Fact]
    public void Hash_refuse_un_mot_de_passe_vide() =>
        Assert.ThrowsAny<ArgumentException>(() => PasswordHasher.Hash(" "));

    [Fact]
    public void GenerateTemporaryPassword_respecte_la_longueur()
    {
        Assert.Equal(12, PasswordHasher.GenerateTemporaryPassword().Length);
        Assert.Equal(20, PasswordHasher.GenerateTemporaryPassword(20).Length);
    }

    [Fact]
    public void Sha256_est_deterministe_en_hexadecimal_minuscule() =>
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            PasswordHasher.Sha256("abc"));
}
