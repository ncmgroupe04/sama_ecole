# Module Tenues — Plan d'implémentation

> Spécification : `docs/superpowers/specs/2026-10-04-uniforms-module-design.md` (arbitrages validés le 04/10/2026).
> Démarche TDD à chaque phase : test au rouge (compilation puis comportement), implémentation minimale, vert, commit.

**Goal :** suivre, par élève et par année, la tenue due, sa taille, sa facturation (lecture seule de Finance) et sa remise (via `StockLedger` de l'Inventaire).

**Architecture :** trois tables (`uniform_items`, `uniform_variants`, `student_uniforms`) au-dessus d'`inventory_items` et d'`enrollment_fee_lines`. Aucune écriture dans Finance, aucun second stock, `CreateEnrollment` inchangé.

## Contraintes globales

- Branche `feature/uniforms-module` (worktree `sama_ecole-uniforms`), issue de `origin/main`. Conventional Commits, un commit par tâche, staging explicite (jamais `git add .`).
- Migration EF Core **nouvelle** + scripts `docs/migrations/<id>.sql` et `<id>.rollback.sql` idempotents. `dotnet ef migrations add` exige `ConnectionStrings__Migrations` (valeur factice acceptée).
- Tests ciblés par `--filter` ; les tests d'intégration exigent Docker (Testcontainers). La suite complète est lancée par le propriétaire. Un serveur de dev actif verrouille les DLL Debug : utiliser `-c Release`.
- Règles AGENTS.md : #2 (filtre EF **et** RLS), #4 (Finance en lecture seule), #5 (`xmin`), #6 (jamais de `DELETE`), #7 (CQRS), #8 (aucune logique dans contrôleur ni entité).

## Phase 0 — Spécification et plan — FAIT

- [x] Spécification validée, plan.

## Phase 1 — Domaine, EF Core, migration, RLS — FAIT

- [x] Tests unitaires : `UniformBillingCheck`, `UniformDeliveryPolicy` (règles pures, `SamaEcole.Application.Uniforms`) — rouge de compilation, puis vert (39 tests).
- [x] Enums (`UniformKind`, `UniformGender`, `StudentUniformStatus`, `UniformBillingStatus`), entités `UniformItem`, `UniformVariant`, `StudentUniform`, configurations EF, `DbSet` dans `ApplicationDbContext` et `IApplicationDbContext`, messages de doublon dans `UniqueConstraintCatalog`.
- [x] Migration `AddUniformsModule` (tables, index partiels, CHECK, FK composites tenant-safe, RLS, GRANT sans DELETE) + `AddUniformsToPurges` (`reset_school_data`, `delete_school_year`) + scripts `docs/migrations/` up/rollback.
- [x] Tests d'intégration `UniformsPersistenceTests` (PostgreSQL, rôle applicatif) : RLS qui filtre, WITH CHECK, jamais de DELETE, FK composites entre écoles, CHECK, uniques partiels, purges d'année et de remise à neuf ; `RlsCoverageTests` vert (31/31).
- **Défaut trouvé par les tests et corrigé :** `HasDefaultValue(1)` sur `Quantity` et `QuantityIncluded` faisait remplacer silencieusement un `0` par `1` à l'insertion (EF omet la valeur égale au défaut CLR). Défauts retirés côté base ; les initialiseurs C# suffisent.

## Phase 2 — Catalogue (CQRS) — FAIT

- [x] Commandes : créer/modifier/archiver un modèle (`xmin` → 409, renommage propagé aux lots) ; créer/modifier/archiver une taille (crée le lot dans « Tenues » de l'école, dans la même transaction : un doublon ne laisse aucun lot orphelin). Garde `UNIFORM_LOT_IN_USE` sur `DeleteInventoryItem`.
- [x] Requête `GetUniformItems` (tailles triées, stock vivant du lot). Validateurs FluentValidation. 17 tests d'intégration (PostgreSQL) + tests unitaires des règles pures.

## Phase 3 — Dotations (concurrence `xmin`) — FAIT

- [x] Commandes : `CreateStudentUniforms` (tout ou rien, élève et année copiés de l'inscription), `UpdateStudentUniform` et `CancelStudentUniform` sous `xmin` (409). Requête `GetStudentUniforms` : badge de solde (information, jamais un blocage), facturation attendue lue dans les lignes de frais, propositions (payées, adaptées à l'élève, non encore demandées).
- [x] 16 tests d'intégration : doublon actif refusé avec rollback de tout le lot, taille d'un autre modèle, hors tenant (404/422), inscription annulée, conflit `xmin` sur modification et annulation, Finance jamais écrite (TotalDue, lignes de frais, paiements inchangés).
## Phase 4 — Remise et retour (partie critique)

- [ ] `DeliverStudentUniformCommand` / `ReturnStudentUniformCommand` via `StockLedger`, audités.
- [ ] Tests : concurrence sur la dernière unité (un 409), stock insuffisant (422), remise non facturée sans motif refusée, mouvement inverse au retour, journal append-only.

## Phase 5 — Lectures et PDF

- [ ] `GetPendingUniforms`, `GetUniformNeeds` (besoin × taille contre stock). Documents QuestPDF : fiche de remise (A5), feuille de tailles par classe.

## Phase 6 — API et interface

- [ ] `UniformsController` (`/api/v1/uniforms`), rôles du §5 de la spécification, `openapi.yaml`.
- [ ] Écran `/tenues` (Razor + Tailwind + Alpine, `wwwroot/js/uniforms.js`), onglet « Tenues » de la fiche élève, navigation. Tests `node --test` sur la logique pure.

## Phase 7 — Isolation et documentation

- [ ] `UniformsIsolationTests` (lecture et écriture croisées, références entre écoles). Volume 3 (tables), Volume 4 (routes), Volume 7 (rôles), `ACTIVE_CONTEXT`.

