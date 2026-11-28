using System.Text;
using Pressing.Core;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Printing;

namespace Pressing.Tests.Unit;

/// <summary>Octets ESC/POS du ticket 80 mm : commandes, encodage CP858, colonnes, QR, coupe et tiroir.</summary>
public class EscPosReceiptTests
{
    private static readonly Encoding Cp858 = CreateCp858();

    private static Encoding CreateCp858()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(858);
    }

    private static ShopInfo Shop() => new(
        "Pressing Éclat", "Pressing Éclat SARL", "12, avenue de la Paix", "Abidjan", "+225 07 00 00 00 00",
        "+225 07 00 00 00 00", "contact@pressing-eclat.local", "Le soin du détail", "FCFA",
        "Merci de votre confiance. Présentez ce ticket pour retirer vos articles.", "Lun–Sam : 7h30 – 19h30");

    private static Order SampleOrder(bool express = false)
    {
        var client = new Pressing.Core.Entities.Client { FirstName = "Aïcha", LastName = "Koné", Phone = "+225 07 11 22 33 44" };
        var chemise = new Category { Id = 1, Name = "Chemise" };
        var robe = new Category { Id = 2, Name = "Robe de soirée en soie brodée à la main (très longue désignation)" };
        var order = new Order
        {
            Id = 42,
            TicketNumber = "PRESS-2026-00042",
            Client = client,
            CreatedAt = new DateTime(2026, 11, 28, 9, 30, 0),
            DueDate = new DateTime(2026, 12, 1),
            IsExpress = express,
            Subtotal = 12500m,
            Total = 12500m,
            PaidAmount = 5000m
        };
        order.Items.Add(new OrderItem { Id = 1, Category = chemise, ServiceType = ServiceType.WashAndIron, Quantity = 3, UnitPrice = 1000m, Status = ItemStatus.Deposited });
        order.Items.Add(new OrderItem { Id = 2, Category = robe, ServiceType = ServiceType.DryCleaning, Quantity = 1, UnitPrice = 9500m, Status = ItemStatus.Deposited });
        order.Payments.Add(new Payment { Amount = 5000m, Type = PaymentType.Payment, Method = PaymentMethod.Cash });
        return order;
    }

    private static int IndexOf(byte[] data, params byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) return i;
        }

        return -1;
    }

    /// <summary>Texte imprimé : décode en CP858 et retire les commandes ESC/GS à paramètres.</summary>
    private static List<string> TextLines(byte[] data)
    {
        var text = new StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (b == 0x1B)
            {
                // ESC @ (0 param) ; ESC t/a/E/d (1) ; ESC p (3)
                i += data[i + 1] switch { (byte)'@' => 1, (byte)'p' => 4, _ => 2 };
                continue;
            }

            if (b == 0x1D)
            {
                byte cmd = data[i + 1];
                if (cmd == (byte)'(')
                {
                    int length = data[i + 3] | (data[i + 4] << 8);
                    i += 4 + length;  // GS ( k pL pH data
                    continue;
                }

                i += cmd == (byte)'V' ? 3 : 2; // GS V m n ; GS ! n
                continue;
            }

            text.Append(b == 0x0A ? '\n' : Cp858.GetString([b])[0]);
        }

        return text.ToString().Split('\n').ToList();
    }

    [Fact]
    public void Ticket_commence_par_init_et_page_de_codes_858_et_finit_par_la_coupe()
    {
        byte[] data = EscPosReceiptBuilder.Build(SampleOrder(), Shop());

        Assert.Equal([0x1B, 0x40, 0x1B, 0x74, 19], data[..5]);               // ESC @ ; ESC t 19 (CP858)
        Assert.Equal([0x1D, 0x56, 66, 3], data[^4..]);                         // GS V 66 3 : coupe partielle
    }

    [Fact]
    public void Page_850_en_repli()
    {
        byte[] data = EscPosReceiptBuilder.Build(SampleOrder(), Shop(), new EscPosOptions { CodePage = 850 });
        Assert.Equal([0x1B, 0x40, 0x1B, 0x74, 2], data[..5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => EscPosReceiptBuilder.CodePageId(1252));
    }

    [Fact]
    public void Accents_encodes_en_cp858_et_non_en_utf8()
    {
        byte[] data = EscPosReceiptBuilder.Build(SampleOrder(), Shop());

        Assert.True(IndexOf(data, Cp858.GetBytes("Pressing Éclat")) >= 0);       // É = 0x90
        Assert.Equal(0x90, Cp858.GetBytes("É")[0]);
        Assert.True(IndexOf(data, 0xC3, 0x89) < 0, "aucun octet UTF-8 de « É »");
        Assert.True(IndexOf(data, Cp858.GetBytes("Aïcha Koné")) >= 0);
    }

    [Fact]
    public void Toutes_les_lignes_de_texte_tiennent_sur_48_colonnes()
    {
        List<string> lines = TextLines(EscPosReceiptBuilder.Build(SampleOrder(express: true), Shop()));

        Assert.All(lines, l => Assert.True(l.Length <= 48, $"ligne trop longue ({l.Length}) : {l}"));
        Assert.Contains(lines, l => l.Length == 48 && l.StartsWith("Sous-total", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("3 x Chemise", StringComparison.Ordinal) && l.EndsWith("3 000 FCFA", StringComparison.Ordinal));
    }

    [Fact]
    public void Totaux_paye_et_reste()
    {
        List<string> lines = TextLines(EscPosReceiptBuilder.Build(SampleOrder(), Shop()));

        Assert.Contains(lines, l => l.StartsWith("TOTAL", StringComparison.Ordinal) && l.EndsWith("12 500 FCFA", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Payé", StringComparison.Ordinal) && l.EndsWith("5 000 FCFA", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("RESTE À PAYER", StringComparison.Ordinal) && l.EndsWith("7 500 FCFA", StringComparison.Ordinal));
    }

    [Fact]
    public void Ticket_en_double_hauteur_gras_et_centre()
    {
        byte[] data = EscPosReceiptBuilder.Build(SampleOrder(), Shop());
        byte[] ticket = Cp858.GetBytes("PRESS-2026-00042");
        int at = IndexOf(data, ticket);

        Assert.True(at > 0);
        // Juste avant le numéro : ESC E 1 (gras) puis GS ! 1 (double hauteur) ; après : GS ! 0.
        Assert.Equal([0x1B, 0x45, 1, 0x1D, 0x21, 0x01], data[(at - 6)..at]);
        Assert.True(IndexOf(data, 0x1B, 0x61, 1) >= 0);                        // ESC a 1 : centré
    }

    [Fact]
    public void Qr_natif_gs_k_avec_le_numero_de_ticket()
    {
        byte[] data = EscPosReceiptBuilder.Build(SampleOrder(), Shop());
        byte[] ticket = Encoding.ASCII.GetBytes("PRESS-2026-00042");

        Assert.True(IndexOf(data, 0x1D, 0x28, 0x6B, 4, 0, 49, 65, 50, 0) >= 0, "modèle 2");
        Assert.True(IndexOf(data, 0x1D, 0x28, 0x6B, 3, 0, 49, 81, 48) >= 0, "impression du QR");
        int length = ticket.Length + 3;
        byte[] store = [0x1D, 0x28, 0x6B, (byte)length, 0, 49, 80, 48, .. ticket];
        Assert.True(IndexOf(data, store) >= 0, "données du QR = numéro de ticket");
    }

    [Fact]
    public void Sans_qr_aucune_commande_gs_k()
    {
        byte[] data = EscPosReceiptBuilder.Build(SampleOrder(), Shop(), new EscPosOptions { PrintQr = false });
        Assert.True(IndexOf(data, 0x1D, 0x28, 0x6B) < 0);
    }

    [Fact]
    public void Tiroir_caisse_seulement_si_demande()
    {
        byte[] pulse = EscPosReceiptBuilder.DrawerPulse();
        Assert.Equal([0x1B, (byte)'p', 0, 25, 250], pulse);

        Assert.True(IndexOf(EscPosReceiptBuilder.Build(SampleOrder(), Shop()), pulse) < 0);
        byte[] withDrawer = EscPosReceiptBuilder.Build(SampleOrder(), Shop(), new EscPosOptions { OpenCashDrawer = true });
        Assert.True(IndexOf(withDrawer, pulse) >= 0);
        Assert.True(IndexOf(withDrawer, pulse) < IndexOf(withDrawer, 0x1D, 0x56, 66), "tiroir avant la coupe");
    }

    [Fact]
    public void Montants_sans_espace_insecable_hors_page_de_codes()
    {
        string money = EscPosReceiptBuilder.Money(1234567m, "FCFA");

        Assert.Equal("1 234 567 FCFA", money);
        Assert.DoesNotContain(' ', money);
        Assert.DoesNotContain(' ', money);
    }

    [Fact]
    public void Caracteres_hors_page_remplaces_par_un_point_d_interrogation()
    {
        var w = new EscPosWriter();
        w.Text("Prix 5 € 中");
        Assert.Contains((byte)'?', w.ToArray());
        Assert.Equal(0xD5, Cp858.GetBytes("€")[0]);   // « € » existe en CP858 (pas en CP850)
    }

    [Theory]
    [InlineData("Sous-total", "3 500 FCFA")]
    [InlineData("Un libellé vraiment beaucoup trop long pour tenir sur une seule ligne d'un ticket", "12 500 FCFA")]
    public void Colonnes_font_toujours_la_largeur_demandee(string left, string right)
    {
        string line = EscPosWriter.FormatColumns(left, right, 48);

        Assert.Equal(48, line.Length);
        Assert.EndsWith(right, line);
    }

    [Fact]
    public void Retour_a_la_ligne_ne_depasse_jamais_la_largeur()
    {
        IReadOnlyList<string> lines = EscPosWriter.WrapText(
            "Merci de votre confiance. Présentez ce ticket pour retirer vos articles. " + new string('x', 70), 48);

        Assert.True(lines.Count >= 3);
        Assert.All(lines, l => Assert.True(l.Length <= 48));
    }
}
