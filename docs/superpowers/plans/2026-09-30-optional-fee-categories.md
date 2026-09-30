# Frais optionnels (uniforme, tenue de sport) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Une catégorie de frais peut être déclarée **optionnelle** (uniforme, tenue de sport, cantine…). À l'inscription et à la réinscription, la famille coche ou décoche ces frais ; seuls les frais cochés entrent dans le dû annuel. Les frais **obligatoires** (inscription, mensualité) ne peuvent jamais être décochés.

**Architecture :** Un drapeau `FeeCategory.IsOptional` (défaut `false` = obligatoire). `CreateEnrollmentCommand` reçoit la liste des catégories optionnelles choisies ; `BuildFeeLinesAsync` (`CreateEnrollmentCommandHandler`) n'inclut une catégorie optionnelle que si elle est dans cette liste. Le filtrage est **serveur** : le client ne fixe jamais un montant (règle #10) et ne peut pas retirer un frais obligatoire — un identifiant obligatoire ou inconnu dans la liste est ignoré ou rejeté en 422, jamais interprété comme un décochage.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql, MediatR + FluentValidation, Alpine.js, xUnit + FluentAssertions (Testcontainers Postgres), `node --test`.

## Global Constraints

- **Le défaut est OBLIGATOIRE.** La liste des catégories est libre (aucun nom codé en dur) : ce défaut est ce qui protège inscription et mensualité. Toute catégorie existante reste due par tous après migration.
- Le drapeau n'agit que sur les inscriptions **futures** : `EnrollmentFeeLine` est un instantané, `TotalDue` n'est jamais recalculé (règle #4).
- `IsOptional` et `IsBoardingFee` sont **exclusifs** : la pension a son propre choix (régime d'hébergement + `IncludeBoardingFee`).
- Le matricule reste généré dans la transaction d'inscription (règle #3) ; le choix des frais se fait dans cette même transaction.
- Migration EF Core **nouvelle** (jamais modifier une migration appliquée) + scripts `docs/migrations/<id>.sql` et `<id>.rollback.sql` idempotents. `dotnet ef migrations add` exige `ConnectionStrings__Migrations` : une valeur factice suffit pour générer.
- Tests **ciblés** (`--filter`) ; la suite complète `dotnet test` est lancée par le propriétaire. Les tests d'intégration exigent Docker (Testcontainers).
- Conventional Commits, un commit par tâche, staging explicite (jamais `git add .`).

## Tâche 1 — Drapeau `IsOptional` (domaine, EF Core, API, écran Frais) — FAIT

- [x] Tests unitaires au rouge : `tests/SamaEcole.UnitTests/Finance/OptionalFeeCategoryTests.cs` (défaut obligatoire, validation création, pension incompatible, validation modification). Rouge de compilation puis rouge de comportement (2 échecs), puis vert.
- [x] `FeeCategory.IsOptional` (défaut `false`) + `FeeCategoryConfiguration` (`HasDefaultValue(false)`).
- [x] Migration `AddFeeCategoryIsOptional` + scripts SQL up/rollback.
- [x] `CreateFeeCategoryCommand.IsOptional` (+ règle : incompatible avec `IsBoardingFee`), `CreateFeeCategoryResult` et `FeeCategoryDto` exposent `IsOptional`.
- [x] `UpdateFeeCategoryCommand` + handler + validator ; `PUT /api/v1/finance/fee-categories/{id}` (politique `CanModifyFees`, audité) ; `openapi.yaml`.
- [x] Écran `/frais` : case « Frais optionnel » à la création d'une catégorie.
- [x] Tests d'intégration `tests/SamaEcole.IntegrationTests/Finance/OptionalFeeCategoryTests.cs` (persistance, bascule dans les deux sens, cloisonnement entre écoles, liste).
- [ ] Reste à la charge d'une tâche ultérieure : bascule d'une catégorie **existante** depuis l'écran (le `PUT` est prêt, l'écran n'expose que la création).

## Tâche 2 — Sélection à l'inscription et à la réinscription — FAIT

- [x] Règle pure `OptionalFeeSelection` (`IsBilled`, `FindInvalid`) — tests unitaires `OptionalFeeSelectionTests` : rouge de compilation, rouge de comportement (6 échecs sur 17), puis vert.
- [x] `CreateEnrollmentCommand.OptionalFeeCategoryIds` (`IReadOnlyList<Guid>?`) : **absent (`null`) = comportement historique** (tous les frais de la classe), `[]` = aucun frais optionnel. Validateur : pas de doublon, pas de `Guid.Empty`.
- [x] `BuildFeeLinesAsync` : une catégorie `IsOptional` n'est facturée que si cochée ; les obligatoires le sont toujours. Un identifiant qui n'est pas un frais optionnel de la classe → 422, et l'inscription entière est annulée (matricule compris). Tests d'intégration `EnrollmentTests` (PostgreSQL) : rouge (5 échecs) puis vert (25/25).
- [x] Formulaire (`Views/Enrollments/Index.cshtml`, `wwwroot/js/enrollments.js`) : une case par frais optionnel dans le récapitulatif, **cochée par défaut**, ligne barrée et sortie du total quand décochée ; les frais obligatoires n'ont pas de case. Un seul composant sert l'inscription et la réinscription. État `form.uncheckedFeeIds` (les exceptions, pas les cochés) : restauré avec le brouillon, remis à zéro quand la classe change. Tests `node --test` `enrollment-optional-fees.test.mjs` : rouge (13 échecs) puis vert.
- [x] `openapi.yaml` et `Volume_4_API_Design.md` : champ `optionalFeeCategoryIds`.
- Le reçu ne change pas de mise en page (règle #12) : il liste les lignes réellement facturées.

## Tâche 3 — Dû annuel et protection des frais obligatoires — FAIT

Constat de l'audit des lecteurs : **aucun code de production à changer.** Tous lisent `Enrollment.TotalDue` ou les `EnrollmentFeeLine` figées à l'inscription ; aucun ne recalcule depuis le barème (`ClassFees`, seulement lu par l'inscription, `ApplyStandardFee`, `UpdateClassFee`, `DeleteClassFee`, `DeleteFeeCategory`). Le filtrage de la Tâche 2 se propage donc tout seul. Cette tâche est un filet de non-régression, pas une correction.

- [x] `tests/SamaEcole.IntegrationTests/Finance/OptionalFeesAnnualDueTests.cs` (PostgreSQL, rôle applicatif) : trois élèves d'une même classe aux factures différentes (Awa tout coché = 178 000, Fatou uniforme seul = 170 000, Modou aucun = 145 000), inscrits par le vrai handler, puis lus par `GetStudentBalance` (dû, reste, échéances), `GetDuesNotice` (sommation), `GetDebtorAgingReport`, `SearchStudentsForCashier` (dû et « à régler maintenant »), `GetFinanceDashboard` et `ApplyFeeInstallmentPlanToClassroom` (échéancier calculé sur le dû propre à chaque élève).
- [x] Protection des obligatoires, de bout en bout : rendre une catégorie obligatoire après coup ne réécrit aucune inscription existante ; une fois obligatoire, elle est facturée à tout nouvel élève même si un formulaire périmé la décoche ; lister la mensualité comme « frais optionnel » est refusé (422), pas ignoré.
- [x] Tests verts dès l'écriture (9/9) puisque le code était déjà correct. Pour prouver qu'ils ne sont pas creux : **test de mutation** sur `OptionalFeeSelection.IsBilled`. « Tout est facturé » → 7 échecs sur 9 ; « un obligatoire peut être retiré » → 11 échecs d'intégration et 2 unitaires. Code restauré ensuite.
- Non traité, hors périmètre : `GenerateDebtorReminderBatches` et `SendDuesReminderSms` lisent aussi `TotalDue − AmountPaid` (même source figée que le rapport des débiteurs, couvert ici) mais exigent des réglages SMS/relance ; l'ajout d'un frais optionnel APRÈS l'inscription passe par une correction Secrétariat/Admin historisée (règle #4).
