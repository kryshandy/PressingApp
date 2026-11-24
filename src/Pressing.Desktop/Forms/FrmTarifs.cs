using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Forms;

/// <summary>Grille des tarifs éditable et ajout de catégories.</summary>
public sealed class FrmTarifs : PressingForm
{
    private DataGridView _grid = new();
    private Label _vide = Theme.EmptyLabel();
    private readonly HashSet<int> _modifies = new();
    private bool _chargement;

    private readonly TextBox _nom = Theme.Texte();
    private readonly TextBox _prix = Theme.Texte("0");
    private readonly TextBox _supplement = Theme.Texte("500");
    private readonly TextBox _delaiNormal = Theme.Texte("3");
    private readonly TextBox _delaiExpress = Theme.Texte("1");

    public FrmTarifs()
    {
        Text = "Tarifs et catégories";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1050, 740);
        MinimumSize = new Size(800, 560);
        BackColor = Theme.Light;
        ConstruireInterface();
        Shown += async (_, _) => await ChargerAsync();
    }

    private void ConstruireInterface()
    {
        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        RoundedPanel section = UiFactory.Section("Tarifs par catégorie — modifiez une cellule puis enregistrez", out _grid, out _vide);
        Grids.Tarifs.Configure(_grid, editable: true);
        _grid.CellValueChanged += (_, e) =>
        {
            if (_chargement || e.RowIndex < 0) return;
            TarifDto? tarif = Grids.Tarifs.Selection(_grid);
            if (tarif != null) _modifies.Add(tarif.CategorieId);
        };
        racine.Controls.Add(section, 0, 0);
        racine.Controls.Add(ConstruireAjout(), 0, 1);
        racine.Controls.Add(ConstruireBoutons(), 0, 2);
        Controls.Add(racine);
    }

    private RoundedPanel ConstruireAjout()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "AJOUTER UNE CATÉGORIE", Dock = DockStyle.Top, Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Color.Transparent
        };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Padding = new Padding(0, 4, 0, 0)
        };
        for (int i = 0; i < 5; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

        _nom.PlaceholderText = "Nom de la catégorie";
        _prix.PlaceholderText = "Prix";
        _supplement.PlaceholderText = "Supplément";
        _delaiNormal.PlaceholderText = "Délai normal";
        _delaiExpress.PlaceholderText = "Délai express";

        table.Controls.Add(Theme.Champ("Catégorie", _nom), 0, 0);
        table.Controls.Add(Theme.Champ("Prix (F)", _prix), 1, 0);
        table.Controls.Add(Theme.Champ("Suppl. express (F)", _supplement), 2, 0);
        table.Controls.Add(Theme.Champ("Délai normal (jours)", _delaiNormal), 3, 0);
        table.Controls.Add(Theme.Champ("Délai express (jours)", _delaiExpress), 4, 0);

        FlatButton ajouter = new() { Text = "＋  Ajouter la catégorie", Width = 220, Dock = DockStyle.Left, Margin = new Padding(0, 0, 8, 0) };
        ajouter.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        ajouter.Click += async (_, _) => await AjouterAsync();
        Label note = Theme.Label(
            "La nouvelle catégorie apparaîtra immédiatement dans le catalogue et dans le formulaire de dépôt.",
            Theme.F(8.5f), Theme.Muted, DockStyle.Fill);
        table.Controls.Add(ajouter, 0, 1);
        table.SetColumnSpan(ajouter, 2);
        table.Controls.Add(note, 2, 1);
        table.SetColumnSpan(note, 3);

        panneau.Controls.Add(table);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private Panel ConstruireBoutons()
    {
        Panel barre = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        FlatButton fermer = new() { Text = "Fermer", Width = 110, Dock = DockStyle.Right };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        FlatButton actualiser = new() { Text = "Actualiser", Width = 120, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        actualiser.SetSecondary();
        actualiser.Click += async (_, _) => await ChargerAsync();
        FlatButton enregistrer = new() { Text = "Enregistrer les modifications", Width = 250, Dock = DockStyle.Left, Height = 42 };
        enregistrer.Click += async (_, _) => await EnregistrerAsync();
        barre.Controls.Add(fermer);
        barre.Controls.Add(actualiser);
        barre.Controls.Add(enregistrer);
        return barre;
    }

    private async Task ChargerAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            List<TarifDto> tarifs = await AppState.Api.GetTarifsAsync();
            _chargement = true;
            Grids.Tarifs.Remplir(_grid, tarifs);
            _modifies.Clear();
            _vide.Visible = _grid.Rows.Count == 0;
            _vide.BringToFront();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement des tarifs");
        }
        finally
        {
            _chargement = false;
            Cursor = Cursors.Default;
        }
    }

    private async Task EnregistrerAsync()
    {
        if (_modifies.Count == 0)
        {
            Theme.Info("Aucune modification à enregistrer.", "Tarifs");
            return;
        }

        try
        {
            _grid.EndEdit();
            Cursor = Cursors.WaitCursor;
            int count = 0;
            foreach (DataGridViewRow ligne in _grid.Rows)
            {
                if (ligne.IsNewRow) continue;
                int categorieId = Convert.ToInt32(ligne.Cells["CategorieId"].Value ?? 0);
                if (!_modifies.Contains(categorieId)) continue;
                if (!Theme.TryDecimal(ligne.Cells["Prix"].Value?.ToString(), out decimal prix) ||
                    !Theme.TryDecimal(ligne.Cells["SupplementExpress"].Value?.ToString(), out decimal supplement) ||
                    !int.TryParse(ligne.Cells["DelaiNormalJours"].Value?.ToString(), out int normal) ||
                    !int.TryParse(ligne.Cells["DelaiExpressJours"].Value?.ToString(), out int express))
                {
                    Theme.Info("Vérifiez les valeurs numériques de la ligne « " +
                               ligne.Cells["CategorieNom"].Value + " ».", "Tarifs invalides");
                    return;
                }

                await AppState.Api.UpdateTarifAsync(categorieId, new TarifUpdateDto
                {
                    Prix = prix,
                    SupplementExpress = supplement,
                    DelaiNormalJours = normal,
                    DelaiExpressJours = express
                });
                count++;
            }
            await ChargerAsync();
            Theme.Info(count + " tarif(s) mis à jour.", "Tarifs enregistrés");
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'enregistrement des tarifs");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task AjouterAsync()
    {
        if (string.IsNullOrWhiteSpace(_nom.Text))
        {
            Theme.Info("Le nom de la catégorie est obligatoire.", "Catégorie");
            return;
        }
        if (!Theme.TryDecimal(_prix.Text, out decimal prix) ||
            !Theme.TryDecimal(_supplement.Text, out decimal supplement) ||
            !int.TryParse(_delaiNormal.Text, out int normal) ||
            !int.TryParse(_delaiExpress.Text, out int express))
        {
            Theme.Info("Vérifiez les valeurs numériques saisies.", "Catégorie");
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            TarifDto cree = await AppState.Api.AddCategorieAsync(new CategorieCreateDto
            {
                Nom = _nom.Text.Trim(),
                Prix = prix,
                SupplementExpress = supplement,
                DelaiNormalJours = normal,
                DelaiExpressJours = express
            });
            await ChargerAsync();
            _nom.Clear();
            Theme.Info("Catégorie « " + cree.CategorieNom + " » ajoutée au catalogue.", "Catégorie créée");
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'ajout de la catégorie");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
