# Consignes de relecture — Pressing Éclat

Tu relis les pull requests d'une équipe de 5 étudiants sur une application de pressing en .NET 10 :
`Pressing.Core` (métier, EF Core SQLite), `Pressing.Api` (minimal API, JWT), `Pressing.Client`,
`Pressing.Desktop` (WinForms, thème sombre), `Pressing.Web` (MVC), `tests/Pressing.Tests` (xUnit).
**Rédige tes commentaires en français.** Sois précis : fichier, ligne, scénario concret qui échoue, correction proposée.
N'approuve une PR que si **aucun** point « bloquant » ci-dessous n'est présent.

## Bloquant (demander des modifications)

1. **Encodage** : tout texte contenant « Ã », « Â » ou « â€ » (accents ré-encodés) est un défaut, même en commentaire.
2. **Base de données** : aucun `ALTER TABLE`, `CREATE TABLE` ni `ExecuteSqlRaw` modifiant le schéma dans
   `DatabaseInitializer`. Toute modification d'entité doit venir avec une migration dans
   `src/Pressing.Core/Data/Migrations` (fichier de migration + `PressingDbContextModelSnapshot.cs` cohérents).
   Une colonne non nulle doit avoir une vraie valeur par défaut (énumération stockée en texte : son nom, jamais `""`).
   Une migration ne supprime jamais de données.
3. **Tests** : toute règle métier, route ou service nouveau a des tests (cas nominal, cas refusé, cas limite).
   Une PR qui ajoute de la logique sans test est bloquée.
4. **Sécurité** : chaque route a la bonne politique (`StaffOnly`, `ManagerOrAdmin`, `AdminOnly`, `ClientOnly`) ou est
   volontairement anonyme et limitée en débit ; un client ne voit jamais les données d'un autre ; aucune donnée
   sensible dans les journaux ; formulaires web avec anti-falsification ; pas de redirection ouverte ; aucun secret,
   fichier `.env`, `.db`, `jwt.key` ou modèle `.gguf` commité.
5. **Architecture** : le Desktop et le Web ne lisent jamais la base (tout passe par l'API) ; la logique métier est dans
   `Pressing.Core`, pas dans les endpoints ni les formulaires. L'assistant vocal (`Pressing.Core/Assistant`) est en
   **lecture seule** : il ne crée ni commande, ni paiement, ni retrait, et n'appelle jamais `SaveChanges`.
6. **Contrat** : une route ajoutée ou modifiée doit figurer dans `docs/API.md` et, si elle est utilisée par le
   Desktop ou le site, dans `PressingApiClient` ; une variable de configuration nouvelle doit figurer dans `.env.example`.
7. **Async** : jamais `.Result`, `.Wait()` ni `async void` (hors gestionnaires d'événements WinForms).

## À signaler (non bloquant)

- Textes visibles par l'utilisateur : en français, avec accents, messages d'erreur compréhensibles.
- Montants en `decimal`, affichés via `Fmt.Money` ; dates via `Fmt.Date` / `Fmt.DateTime`.
- `Program.cs` : une seule ligne par fonctionnalité (méthode d'extension `AddXxx()` / `MapXxxEndpoints()`).
- Routes d'un lot dans leur propre fichier `XxxEndpoints.cs` plutôt que dans un fichier existant.
- Desktop : réutiliser les contrôles de `Ui/` (thème sombre), pas de contrôle Windows gris non stylé.
- PR de plus de 400 lignes (hors fichiers générés) : suggérer de la découper.
- Message de commit / titre de PR au format « Zone : description » en français (`Core : …`, `Desktop : …`).

## Contexte utile

- Règles complètes : `CONTRIBUTING.md` ; contrat de l'API : `docs/API.md`.
- Les rappels (WhatsApp / e-mail) n'ont lieu qu'avec un consentement daté (`ConsentGivenAt`) et jamais après une
  révocation (`ConsentRevokedAt`).
- Les jetons JWT portent `token_type` (`staff`, `client`, `unsubscribe`) ; `TokenRevalidation` refuse tout autre type
  pour l'authentification.
