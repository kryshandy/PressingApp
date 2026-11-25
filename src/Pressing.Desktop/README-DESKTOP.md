# Pressing Desktop (Windows Forms, .NET 10)

Application du personnel. API : variable `PRESSING_PUBLIC_API_URL` (défaut `http://localhost:5180`).

## Ajouter un écran
1. Créer `Views/MaVue.cs` : `public sealed class MaVue : BaseView` (constructeur `(IAppHost host)`), `Title`, interface construite dans le constructeur, données dans `LoadAsync()` (appels `Api.…Async`, vérifier `Alive` après chaque `await`).
2. L'enregistrer dans `Services/NavRegistry.cs` : une ligne dans `Items` (clé, titre, icône `Icons.*`, section, `h => new MaVue(h)`, rôles : `Everyone`, `ManagerOrAdmin`, `AdminOnly`). Le menu, le masquage par rôle et la navigation suivent automatiquement. Pour Promotions/Rapports/Personnel/Paramètres, remplacer la fabrique `ComingSoonView` par la vue réelle.
3. Navigation : `Host.Navigate(clé, paramètre)`, `Host.OpenOrder(id)`, `Host.OpenClient(id)`, `Host.OpenView(clé, () => vue)` + `Host.GoBack()`, `Host.Toast(...)`.

## Conventions
- Couleurs/polices/espacements : uniquement `Ui/Theme.cs` (`Theme.S(px)` pour toute taille en pixels : DPI).
- Réseau : toujours `await RunAsync(...)` (curseur d'attente, boutons `FlatButton.Busy`, erreurs `ApiException` en boîte de dialogue). Dialogues : hériter de `DialogForm`, appeler l'API via `UiRun.RunAsync(this, ...)`.
- Composants : `PageHeader`, `TitledCard`, `RoundedPanel`, `StatCard`, `FlatButton`, `InputBox`, `SelectBox` (+ `SetChoices`), `DateBox`, `ToggleBox`, `StepperBox`, `ThemedGrid`/`GridCard` (+ `IPillRow`, `ITintRow`), `PagerBar`, `FormGrid`, `FieldBox`, `InfoList`, `ScrollPanel`, `TwoColumnPanel`, `UiKit.Rows`, `AppDialogs`.
- Auto-test : `Pressing.Desktop.exe --selftest [--screens dossier]` (journal `%TEMP%\pressing-selftest.log`).
