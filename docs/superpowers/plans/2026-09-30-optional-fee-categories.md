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

## Tâche 2 — Sélection à l'inscription et à la réinscription

- [ ] Tests d'abord (rouge) — `CreateEnrollmentCommandValidatorTests` : liste `OptionalFeeCategoryIds` sans doublon ; tests d'intégration `EnrollmentTests` : une catégorie optionnelle décochée ne crée aucune `EnrollmentFeeLine` et n'entre pas dans `TotalDue`.
- [ ] `CreateEnrollmentCommand.OptionalFeeCategoryIds` (`IReadOnlyList<Guid>?`). **Absent (`null`) = comportement historique** : tous les frais optionnels de la classe inclus, pour ne pas changer la facture d'un ancien client. Liste fournie = uniquement ceux-là.
- [ ] `BuildFeeLinesAsync` : les catégories `IsOptional` ne sont incluses que si choisies ; les obligatoires le sont toujours, quoi que contienne la liste.
- [ ] Un identifiant qui n'est pas une catégorie optionnelle **de cette classe** → 422 (jamais ignoré en silence).
- [ ] Écran d'inscription et de réinscription (`Views/Enrollments/Index.cshtml`, `wwwroot/js/enrollments.js`) : une case par frais optionnel de la classe, **cochée par défaut**, avec le montant ; le total affiché se recalcule ; les frais obligatoires sont listés sans case.
- [ ] Le reçu (règle #12) ne change pas de mise en page : il liste les lignes réellement facturées.

## Tâche 3 — Dû annuel et protection des frais obligatoires

- [ ] Tests (rouge) : le solde, la relance des débiteurs, l'avis de dû et l'échéancier ne comptent que les lignes d'`EnrollmentFeeLine` existantes (donc pas les frais décochés) ; un frais obligatoire ne peut pas être retiré par une requête forgée.
- [ ] Vérifier chaque lecteur de `EnrollmentFeeLine` / `TotalDue` (`GetStudentBalance`, `GetDuesNotice`, `GetDebtorAgingReport`, `InstallmentScheduleCalculator`) : aucun ne doit recalculer le dû depuis le barème de la classe.
- [ ] Ajout d'un frais optionnel après l'inscription (oubli à l'inscription) : hors périmètre de ce plan — passe par une correction Secrétariat/Admin historisée (règle #4).
