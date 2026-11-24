namespace Pressing.Desktop.Ui;

/// <summary>Fabriques de panneaux réutilisables.</summary>
public static class UiFactory
{
    /// <summary>Carte arrondie avec titre, grille et libellé d'état vide.</summary>
    public static RoundedPanel Section(string titre, out DataGridView grid, out Label vide)
    {
        RoundedPanel carte = new() { Padding = new Padding(12), Dock = DockStyle.Fill };
        Label titreLabel = new()
        {
            Text = titre,
            Dock = DockStyle.Top,
            Height = 30,
            Font = Theme.F(11f, FontStyle.Bold),
            ForeColor = Theme.Navy,
            BackColor = Color.Transparent
        };
        Panel hote = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        grid = new DataGridView { Dock = DockStyle.Fill };
        Theme.StyleGrid(grid);
        vide = Theme.EmptyLabel();
        hote.Controls.Add(grid);
        hote.Controls.Add(vide);
        carte.Controls.Add(hote);
        carte.Controls.Add(titreLabel);
        return carte;
    }

    /// <summary>Carte d'information avec paires libellé / valeur.</summary>
    public static RoundedPanel InfoCard(string titre, params (string Libelle, string Valeur)[] champs)
    {
        RoundedPanel carte = new() { Padding = new Padding(14, 10, 14, 10), Dock = DockStyle.Fill };
        Label titreLabel = new()
        {
            Text = titre,
            Dock = DockStyle.Top,
            Height = 26,
            Font = Theme.F(11f, FontStyle.Bold),
            ForeColor = Theme.Navy,
            BackColor = Color.Transparent
        };

        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = Math.Max(1, champs.Length),
            RowCount = 2
        };
        for (int i = 0; i < table.ColumnCount; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / table.ColumnCount));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        for (int i = 0; i < champs.Length; i++)
        {
            Label libelle = new()
            {
                Text = champs[i].Libelle.ToUpperInvariant(),
                Font = Theme.F(8f, FontStyle.Bold),
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                AutoSize = false
            };
            Label valeur = new()
            {
                Text = champs[i].Valeur,
                Font = Theme.F(11f, FontStyle.Bold),
                ForeColor = Theme.Navy,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                AutoSize = false
            };
            table.Controls.Add(libelle, i, 0);
            table.Controls.Add(valeur, i, 1);
        }

        carte.Controls.Add(table);
        carte.Controls.Add(titreLabel);
        return carte;
    }

    /// <summary>Barre horizontale de boutons alignés à droite.</summary>
    public static FlowLayoutPanel Barre(params Control[] boutons)
    {
        FlowLayoutPanel barre = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0)
        };
        foreach (Control bouton in boutons)
        {
            bouton.Margin = new Padding(6, 0, 0, 0);
            barre.Controls.Add(bouton);
        }
        return barre;
    }
}
