# Internat — Lot C (réconciliation, pensionnaires, bascule des lectures et écritures) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Faire de `boarding_enrollments` la source de vérité des affectations d'internat : réconcilier les données héritées, exposer les endpoints pensionnaires (`assign-bed`, `unassign-bed`, liste, fiche, profil), et basculer l'ancien écran `/internat`, le formulaire d'inscription et la fiche élève sur les nouvelles tables **sans changer leurs routes ni leur JSON**.

**Architecture:** Un service `IBoardingAssignmentService` porte la règle d'affectation une seule fois ; les nouvelles commandes et les anciens handlers (`ChangeBoardingAssignment`, `CreateEnrollment`) en sont des clients. Les anciennes routes `/api/v1/internat/*` gardent chemins et formes JSON : leurs handlers deviennent des adaptateurs. Une migration exécute **une seule fois** un script SQL de réconciliation (l'ancien modèle prévaut pour les séjours), puis les colonnes `Enrollment.BoardingStatus/RoomId` ne sont plus lues ni écrites.

**Tech Stack:** .NET 9 (`net9.0`), EF Core + Npgsql, MediatR, FluentValidation, PostgreSQL 16, xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` §2.2 (N3, N5, N7), §4.2, §5.2, §6.2, §10 (lot C). **Empilé sur le lot B** (PR #62, branche `feat/internat-dormitories-cqrs`) : la PR du lot C cible cette branche, puis est retargetée sur `main` après la fusion de B. **B et C partent en production ensemble.**

## Global Constraints

- Contrôleur sans logique métier (AGENTS.md #8) ; `SchoolId` jamais dans un corps, lu via `ITenantProvider` (#10) ; verrou optimiste `xmin` via `SetOriginalConcurrencyToken` (#5) ; soft delete uniquement (#6).
- Un lit n'a **qu'un occupant actif** et une inscription **qu'un séjour actif** : garantis par les index uniques partiels du lot A (`UX_boarding_enrollments_active_bed`, `UX_boarding_enrollments_active_enrollment`). Une course sur le dernier lit remonte en **409 `BED_UNAVAILABLE`**, jamais en 500.
- Un demi-pensionnaire n'a jamais de lit (N7, CHECK en base). Un séjour clos n'a jamais de lit.
- La pension (`FeeCategory.IsBoardingFee`) n'est **jamais retirée** par une fin de séjour ni un transfert (décision #7 de la spec du 18/09, maintenue).
- `MedicalNotes` : lu/écrit seulement par `Directeur` et `Surveillant` (spec §5.2) ; absent (`null`) pour `Secretariat` dans le JSON ; **jamais** interpolé dans un message d'exception (l'audit enregistre `ex.ToString()` en cas d'échec).
- Garde de module `[RequireModule(SchoolModule.Internat)]` sur `BoardingController` et `InternatController` (403 `MODULE_DISABLED`).
- `AuditLoggingBehavior` ne journalise que le nom de la requête (module/action) et, en cas d'échec, `ex.ToString()` : pas le corps. Marquer `IAuditableRequest` les écritures d'affectation, **pas** la commande de profil.
- Prérequis d'exécution : Docker Desktop démarré ; **toujours `dotnet build` avant `dotnet test --no-build`** ; ne jamais lancer les trois projets de tests en parallèle ; une classe de tests fonctionnels partage UN conteneur PostgreSQL (noms uniques, assertions par identifiant).
- Un commit par tâche, en français, terminé par `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Les gros fichiers C# s'écrivent avec l'outil Write (les heredocs bash contenant des apostrophes cassent la commande).

## Décisions de conception du lot

| # | Sujet | Décision |
|---|---|---|
| D1 | Priorité de la réconciliation | **L'ancien modèle prévaut pour les séjours** : avant ce lot, rien n'écrit `boarding_enrollments` en dehors de la reprise, donc toute différence vient d'une écriture legacy plus récente. La **structure** (pavillons, chambres, lits) est **additive seulement** : on crée ce qui manque, on ne modifie ni ne supprime rien de créé par la nouvelle API ni par un Directeur. |
| D2 | Réconciliation = usage unique | Script SQL idempotent (`BoardingReconciliationSql.Script`), exécuté par la migration `ReconcileBoardingWithLegacyModel`, **jamais rejoué après la bascule** : ensuite les colonnes héritées sont périmées et le rejouer fermerait de vrais séjours. Pas de fonction SQL durable (elle serait rejouable par erreur). |
| D3 | Compatibilité `/api/v1/internat/*` | Mêmes chemins, mêmes JSON. `roomId` = `DormitoryRoom.Id` (identique à l'ancien `Room.Id` pour les chambres reprises). Les handlers deviennent des adaptateurs du service. Le jeton `rowVersion` legacy reste **le `xmin` de l'inscription** : l'adaptateur « touche » l'inscription à chaque affectation pour que ce jeton change. Suppression des routes : lot F. |
| D4 | Demi-pensionnaire | Aucune chambre, aucun lit. Une chambre envoyée par l'ancien écran avec ce régime est **ignorée**. Conséquence assumée : il n'occupe plus de place ni n'apparaît dans une chambre du tableau de bord (comptage séparé `demiPensionnaireCount`). |
| D5 | Affectation depuis une chambre (ancien écran) | L'ancien écran envoie une **chambre**, pas un lit : l'adaptateur choisit le **plus petit numéro de lit libre et disponible** de la chambre ; si l'élève est déjà dans un lit de cette chambre, rien ne bouge. Plus de lit libre → 422 « Cette chambre a atteint sa capacité maximale. » (message inchangé). |
| D6 | Genre | Élève `M`/`F` ↔ pavillon `Garcons`/`Filles` ; `Mixte` accepte tout. Refus 422 (champ `BedId`/`RoomId`), y compris par l'ancien écran. Maintenance : 422. **Écart avec la spec §6.2** : pas de codes dédiés `GENDER_MISMATCH`/`BED_IN_MAINTENANCE` (le 422 du projet n'en porte pas) ; messages explicites à la place. |
| D7 | Interne sans lit | `assign-bed` exige un lit pour `Interne` (spec §6.2). Un `Interne` sans lit n'existe que via la reprise/réconciliation (« en attente »). |
| D8 | Colonnes héritées | `Enrollment.BoardingStatus/RoomId` : `[Obsolete]`, plus lues ni écrites. Suppression en base : lot F. |
| D9 | Hors lot | Sorties, retours, pointage, filtre `onLeave` et historique des sorties dans la fiche → lot D. Seule garde posée ici : fin de séjour refusée (409 `LEAVE_IN_PROGRESS`) s'il existe une sortie ouverte. |
| D10 | Période | Pensionnaires listés = ceux de l'**année active** (inscription de l'année active). |

## Prérequis de déploiement (à reporter dans la PR)

1. **Arrêter toutes les anciennes instances avant d'appliquer la migration** (pas de déploiement progressif) : une instance du lot A/B encore active après la réconciliation écrirait les colonnes héritées, que plus personne ne lit.
2. Livrer **B + C dans la même fenêtre** (la tâche Q1 du lot B coupe la création de dortoirs par `/infrastructures`).
3. Après déploiement : vérifier `SELECT count(*)` des séjours actifs = inscriptions pensionnaires de l'année active (requête fournie en tâche 1).

## Carte des fichiers

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Persistence/Migrations/BoardingReconciliationSql.cs` (créer) | Script SQL de réconciliation, `public static`, partagé migration/tests |
| `src/SamaEcole.Persistence/Migrations/*_ReconcileBoardingWithLegacyModel.cs` (créer, migration **vide** côté modèle) | Exécute le script une fois |
| `src/SamaEcole.Application/Boarding/Assignments/IBoardingAssignmentService.cs`, `BoardingAssignmentService.cs`, `BoardingConflicts.cs` (créer) | Règle d'affectation unique |
| `src/SamaEcole.Application/Boarding/Boarders/*` (créer) | `AssignBed`, `EndBoarding`, `ListBoarders`, `GetBoarder`, `UpdateBoarderProfile`, DTO |
| `src/SamaEcole.Web/Controllers/BoardingController.cs` (modifier) | 5 routes pensionnaires |
| `src/SamaEcole.Application/Internat/**`, `Enrollments/Commands/CreateEnrollment/*`, `Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs` (modifier) | Bascule |
| `src/SamaEcole.Domain/Entities/Enrollment.cs`, `Persistence/Configurations/EnrollmentConfiguration.cs`, `Persistence/Errors/UniqueConstraintCatalog.cs` (modifier) | `[Obsolete]`, catalogue |
| `tests/**/Boarding/*`, `tests/**/Internat/*`, `tests/**/Enrollments/*`, `tests/**/Students/*` (créer/modifier) | Tests |
| `docs/Volume_4_API_Design.md`, `docs/Volume_3_DDS.md`, `ACTIVE_CONTEXT.md` (modifier) | Documentation |

---

### Task 1 : Script de réconciliation et migration (tests d'abord)

**Files:**
- Create: `src/SamaEcole.Persistence/Migrations/BoardingReconciliationSql.cs`, `tests/SamaEcole.IntegrationTests/Boarding/BoardingReconciliationTests.cs`
- Generate: `src/SamaEcole.Persistence/Migrations/<ts>_ReconcileBoardingWithLegacyModel.cs` (+ Designer)

**Interfaces — Produces:** `SamaEcole.Persistence.BoardingReconciliationSql.Script` (string : un bloc `DO $reconcile$ ... $reconcile$;`).

- [ ] **Step 1 : écrire les tests (échouent : classe absente).** Même structure que `BoardingBackfillMigrationTests` (`[Trait("Category","MultiTenant")]`, `RlsTestDatabase _db`, `InitializeAsync => _db.InitializeAsync()`), avec `#pragma warning disable CS0618` en tête de fichier (les colonnes héritées deviendront `[Obsolete]` à la tâche 8). Helper :

```csharp
private async Task RunReconciliationAsync()
{
    await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);   // rôle propriétaire : exempté de RLS
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(BoardingReconciliationSql.Script, connection);
    await command.ExecuteNonQueryAsync();
}
```
Jeu de données hérité (école A : année active « 2026-2027 », année passée « 2025-2026 ; école B : un dortoir avec un interne) semé par EF **après** `InitializeAsync` (les tables du lot A existent, vides) ; legacy via `Enrollment.BoardingStatus/RoomId` exactement comme `BoardingBackfillMigrationTests.SeedLegacyAsync` (bâtiments « Pavillon Garçons » avec Dortoir 101 (cap. 2) et Dortoir 102 (cap. 1), « Pavillon Filles » avec Dortoir 201 (cap. 2), « Bloc classes » avec Salle 1 ; élèves g1,g2 `M` Interne ch.101, g3 `M` Interne ch.102, f1 `F` Interne ch.201, f2 `F` Demi ch.201, x1 Interne sans chambre, x2 Externe). Tests :

1. `A_First_Run_Creates_Structure_And_Seats_Every_Boarder` — après `RunReconciliationAsync()` : 2 pavillons pour A (`Garcons`, `Filles`, `Id` = `Building.Id`), 3 chambres (`Id` = `Room.Id`), lits `max(capacité, internes)` par chambre ; stays actifs = {g1,g2,g3,f1,f2,x1} ; g1,g2 sur les lits 1 et 2 de la ch.101 (ordre d'inscription) ; f2 et x1 sans lit ; aucun `BedId` en double ; x2 sans séjour.
2. `Drift_After_The_Backfill_Is_Reconciled_And_The_Legacy_Model_Wins` — après un premier passage, **modifier le legacy en SQL** : g1 passe de la ch.101 à la ch.102 ; f1 devient `Externe` (RoomId nul) ; g3 est `Cancelled` ; f2 passe `Interne` ch.201 ; x1 reçoit `RoomId` = ch.101 ; un nouvel élève n1 `Interne` ch.101 ; une **nouvelle** salle `Dortoir 103` (cap. 2) dans « Pavillon Garçons » avec un nouvel élève n2 `Interne` dedans ; un **nouveau** bâtiment « Pavillon neuf » avec `Dortoir 301` (cap. 1) et n3 `Interne`. Second passage, puis assertions : g1 sur un lit de la ch.102 et libère son lit de la ch.101 ; f1 : séjour **clos** (`IsActive=false`, `EndDate` non nul, `BedId` nul) ; g3 : clos ; f2 : régime `Interne` avec un lit de la ch.201 ; x1 : sur un lit de la ch.101 ; n1 : sur un lit de la ch.101 ; la ch.103 existe (`DormitoryRoom`, 2 lits) et n2 y est assis ; « Pavillon neuf » existe (genre déduit) avec n3 assis ; **aucun** lit partagé ; chaque séjour actif `Interne` assis est dans la chambre légale de son inscription.
3. `Running_It_Twice_Changes_Nothing` — instantané `(Id, EnrollmentId, Regime, BedId, IsActive)` de tous les séjours et nombre de lits avant/après un passage supplémentaire : identiques.
4. `The_Structure_Created_By_The_New_Api_Is_Never_Touched` — un pavillon + chambre + lit créés avec de **nouveaux** `Id` (comme le ferait le lot B), dont un lit tenu par un séjour actif d'une inscription legacy `Interne` sans chambre : après le passage, pavillon/chambre/lit **inchangés** (nom, `IsDeleted`, lit), seul le séjour est réconcilié selon le legacy (ici : sorti du lit de la nouvelle API car le legacy dit « sans chambre » → `BedId` nul).
5. `A_Name_Conflict_Does_Not_Abort_The_Run` — un pavillon créé par la nouvelle API porte déjà le nom d'un bâtiment legacy contenant un `Dortoir` : le passage **ne lève pas** (pas de violation `UX_dormitories_name`), le pavillon legacy n'est pas créé, ses occupants restent « en attente » (`BedId` nul).
6. `A_Dormitory_Deleted_By_A_Director_Is_Not_Resurrected` — un pavillon repris puis supprimé logiquement (`IsDeleted = true`) reste supprimé ; ses occupants restent sans lit.
7. `A_Year_Rollover_Closes_The_Stays_Of_The_Old_Year_And_Opens_The_New_One` — l'année « 2026-2027 » est désactivée, une année « 2027-2028 » activée, et l'élève g1 réinscrit `Interne` ch.101 : séjour de l'ancienne année clos à la fin de cette année (`EndDate` = `max(fin d'année, StartDate)`), nouveau séjour actif pour la nouvelle inscription, assis sur un lit libéré.
8. `Reconciliation_Invariants_Hold` — pour chaque scénario ci-dessus (méthode d'aide `AssertInvariantsAsync`) : (a) autant de séjours actifs que d'inscriptions `BoardingStatus <> 'Externe' AND Status <> 'Cancelled'` de l'année active ; (b) aucun séjour actif pour une inscription `Externe`/annulée/hors année active ; (c) aucun `BedId` partagé entre séjours actifs ; (d) aucun séjour `DemiPensionnaire` avec lit.

- [ ] **Step 2 : lancer — doit échouer** (`BoardingReconciliationSql` absent). Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~BoardingReconciliationTests"` — Expected: erreur de compilation.

- [ ] **Step 3 : écrire le script.** `src/SamaEcole.Persistence/Migrations/BoardingReconciliationSql.cs` (namespace `SamaEcole.Persistence`, classe `public static class BoardingReconciliationSql`, constante `Script`) — **figé après fusion : ne jamais le modifier**, la migration l'exécute telle quelle. Contenu (un seul bloc `DO`, dans cet ordre) :

```sql
DO $reconcile$
DECLARE v_n integer;
BEGIN
    -- R1. Pavillons manquants : un par bâtiment vivant ayant une chambre Dortoir vivante, Id = Building.Id.
    --     Additif seulement (ON CONFLICT DO NOTHING) ; un nom déjà pris par un pavillon vivant n'est PAS écrasé.
    INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
    SELECT b."Id", b."SchoolId", b."Name",
           CASE g.gender WHEN 'M' THEN 'Garcons' WHEN 'F' THEN 'Filles' ELSE 'Mixte' END, NOW(), FALSE
    FROM buildings b
    LEFT JOIN LATERAL (
        SELECT CASE WHEN count(DISTINCT s."Gender") = 1 THEN min(s."Gender") END AS gender
        FROM enrollments e
        JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
        JOIN rooms r ON r."SchoolId" = e."SchoolId" AND r."Id" = e."RoomId"
        JOIN students s ON s."SchoolId" = e."SchoolId" AND s."Id" = e."StudentId"
        WHERE r."BuildingId" = b."Id" AND r."Type" = 'Dortoir' AND NOT r."IsDeleted"
          AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
    ) g ON TRUE
    WHERE NOT b."IsDeleted"
      AND EXISTS (SELECT 1 FROM rooms r WHERE r."SchoolId" = b."SchoolId" AND r."BuildingId" = b."Id"
                  AND r."Type" = 'Dortoir' AND NOT r."IsDeleted")
      AND NOT EXISTS (SELECT 1 FROM dormitories d WHERE d."SchoolId" = b."SchoolId" AND d."Name" = b."Name" AND NOT d."IsDeleted")
    ON CONFLICT ("Id") DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R1 pavillons créés : %', v_n;

    -- R2. Chambres manquantes : Id = Room.Id, uniquement sous un pavillon vivant.
    INSERT INTO dormitory_rooms ("Id","SchoolId","DormitoryId","Name","CreatedAt","IsDeleted")
    SELECT r."Id", r."SchoolId", r."BuildingId", r."Name", NOW(), FALSE
    FROM rooms r
    JOIN dormitories d ON d."SchoolId" = r."SchoolId" AND d."Id" = r."BuildingId" AND NOT d."IsDeleted"
    WHERE r."Type" = 'Dortoir' AND NOT r."IsDeleted"
    ON CONFLICT ("Id") DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R2 chambres créées : %', v_n;

    -- R3a. Lits des chambres reprises qui n'en ont encore aucun : max(capacité, internes de l'année active).
    INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
    SELECT gen_random_uuid(), dr."SchoolId", dr."Id", n::int, 'Available', NOW(), FALSE
    FROM dormitory_rooms dr
    JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id" AND r."Type" = 'Dortoir'
    CROSS JOIN LATERAL generate_series(1, GREATEST(r."Capacity", (
        SELECT count(*) FROM enrollments e
        JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
        WHERE e."SchoolId" = r."SchoolId" AND e."RoomId" = r."Id" AND e."BoardingStatus" = 'Interne'
          AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"))::int) AS n
    WHERE NOT dr."IsDeleted"
      AND NOT EXISTS (SELECT 1 FROM beds b WHERE b."DormitoryRoomId" = dr."Id");
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R3a lits créés : %', v_n;

    -- R3b. Complément : une chambre reprise qui a moins de lits utilisables que d'internes legacy reçoit des lits
    --      supplémentaires numérotés APRÈS le plus grand numéro existant (lits supprimés compris).
    INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
    SELECT gen_random_uuid(), dr."SchoolId", dr."Id", m.max_no + g::int, 'Available', NOW(), FALSE
    FROM dormitory_rooms dr
    JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id" AND r."Type" = 'Dortoir'
    CROSS JOIN LATERAL (
        SELECT COALESCE(max(b."BedNumber"), 0) AS max_no,
               count(*) FILTER (WHERE NOT b."IsDeleted" AND b."Status" = 'Available') AS usable
        FROM beds b WHERE b."DormitoryRoomId" = dr."Id") m
    CROSS JOIN LATERAL (
        SELECT count(*) AS need FROM enrollments e
        JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
        WHERE e."SchoolId" = r."SchoolId" AND e."RoomId" = r."Id" AND e."BoardingStatus" = 'Interne'
          AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted") o
    CROSS JOIN LATERAL generate_series(1, GREATEST(o.need - m.usable, 0)::int) AS g
    WHERE NOT dr."IsDeleted";
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R3b lits ajoutés : %', v_n;

    -- S1. Clore les séjours actifs que le legacy ne justifie plus : inscription Externe, annulée, supprimée ou
    --     hors année active. EndDate = fin de l'année si elle est inactive, sinon aujourd'hui ; jamais avant StartDate.
    UPDATE boarding_enrollments be
    SET "IsActive" = FALSE, "BedId" = NULL, "UpdatedAt" = NOW(),
        "EndDate" = GREATEST(be."StartDate", COALESCE(
            (SELECT y."EndDate" FROM enrollments e JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId"
             WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId" AND NOT y."IsActive"), CURRENT_DATE))
    WHERE be."IsActive" AND NOT be."IsDeleted"
      AND NOT EXISTS (
          SELECT 1 FROM enrollments e
          JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
          WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
            AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted");
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S1 séjours clos : %', v_n;

    -- S2. Aligner le régime ; un demi-pensionnaire n'a jamais de lit (CHECK).
    UPDATE boarding_enrollments be
    SET "Regime" = e."BoardingStatus", "UpdatedAt" = NOW(),
        "BedId" = CASE WHEN e."BoardingStatus" = 'DemiPensionnaire' THEN NULL ELSE be."BedId" END
    FROM enrollments e
    WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
      AND be."IsActive" AND NOT be."IsDeleted" AND be."Regime" <> e."BoardingStatus";
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S2 régimes alignés : %', v_n;

    -- S3. Libérer d'abord les lits des internes dont la chambre legacy a changé (ou a disparu) : l'index unique
    --     du lit est immédiat, on ne peut pas permuter dans une seule instruction.
    UPDATE boarding_enrollments be
    SET "BedId" = NULL, "UpdatedAt" = NOW()
    FROM enrollments e
    WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
      AND be."IsActive" AND NOT be."IsDeleted" AND be."Regime" = 'Interne' AND be."BedId" IS NOT NULL
      AND (e."RoomId" IS NULL
           OR NOT EXISTS (SELECT 1 FROM beds b WHERE b."Id" = be."BedId" AND b."DormitoryRoomId" = e."RoomId"));
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S3 lits libérés : %', v_n;

    -- S4. Séjours manquants : inscription legacy pensionnaire de l'année active sans séjour actif.
    INSERT INTO boarding_enrollments
        ("Id","SchoolId","StudentId","EnrollmentId","Regime","BedId","StartDate","EndDate","IsActive","AllowedExitPersons","CreatedAt","IsDeleted")
    SELECT gen_random_uuid(), e."SchoolId", e."StudentId", e."Id", e."BoardingStatus", NULL,
           (e."EnrolledAt" AT TIME ZONE 'UTC')::date, NULL, TRUE, '[]'::jsonb, NOW(), FALSE
    FROM enrollments e
    JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
    WHERE e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
      AND NOT EXISTS (SELECT 1 FROM boarding_enrollments be
                      WHERE be."SchoolId" = e."SchoolId" AND be."EnrollmentId" = e."Id" AND be."IsActive" AND NOT be."IsDeleted");
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S4 séjours créés : %', v_n;

    -- S5. Asseoir les internes sans lit dans leur chambre legacy : lits libres et disponibles par numéro croissant,
    --     internes par ordre d'inscription. Ceux qui n'ont pas de place restent « en attente » (BedId nul).
    WITH need AS (
        SELECT be."Id" AS stay_id, e."RoomId" AS room_id,
               row_number() OVER (PARTITION BY e."RoomId" ORDER BY e."EnrolledAt", be."Id") AS rn
        FROM boarding_enrollments be
        JOIN enrollments e ON e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
        WHERE be."IsActive" AND NOT be."IsDeleted" AND be."Regime" = 'Interne' AND be."BedId" IS NULL AND e."RoomId" IS NOT NULL
          AND EXISTS (SELECT 1 FROM dormitory_rooms dr WHERE dr."Id" = e."RoomId" AND NOT dr."IsDeleted")
    ), free AS (
        SELECT b."Id" AS bed_id, b."DormitoryRoomId" AS room_id,
               row_number() OVER (PARTITION BY b."DormitoryRoomId" ORDER BY b."BedNumber") AS rn
        FROM beds b
        WHERE NOT b."IsDeleted" AND b."Status" = 'Available'
          AND NOT EXISTS (SELECT 1 FROM boarding_enrollments x WHERE x."BedId" = b."Id" AND x."IsActive" AND NOT x."IsDeleted")
    )
    UPDATE boarding_enrollments be
    SET "BedId" = f.bed_id, "UpdatedAt" = NOW()
    FROM need n JOIN free f ON f.room_id = n.room_id AND f.rn = n.rn
    WHERE be."Id" = n.stay_id;
    GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S5 internes assis : %', v_n;
END
$reconcile$;
```

- [ ] **Step 4 : générer la migration (vide côté modèle) et y brancher le script.** `$env:ConnectionStrings__Migrations="Host=localhost;Port=1;Database=scaffold_only;Username=x;Password=x"` puis `dotnet ef migrations add ReconcileBoardingWithLegacyModel -p src/SamaEcole.Persistence -s src/SamaEcole.Web`. Remplacer le corps :

```csharp
/// <summary>
/// Réconcilie UNE FOIS les séjours Internat avec l'ancien modèle (Enrollment.BoardingStatus/RoomId) : la reprise du lot A
/// était un instantané, et l'ancien écran a continué d'écrire depuis. L'ancien modèle prévaut pour les séjours ; la
/// structure est additive. NE JAMAIS rejouer après la bascule (spec lot C, D2) : <c>Down()</c> est volontairement vide.
/// </summary>
public partial class ReconcileBoardingWithLegacyModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(BoardingReconciliationSql.Script);

    // Données seulement : rien à défaire. Les tables restent celles du lot A.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
```
Vérifier que le `Designer`/snapshot ne contient **aucun** changement de schéma (`git diff` du snapshot vide).

- [ ] **Step 5 : lancer — doit passer.** `dotnet build SamaEcole.sln` puis `dotnet test tests/SamaEcole.IntegrationTests --no-build --filter "FullyQualifiedName~BoardingReconciliationTests|FullyQualifiedName~BoardingBackfillMigrationTests"` — Expected: PASS (la reprise du lot A reste verte : la migration de réconciliation s'exécute en fin de chaîne sur une base vide).
- [ ] **Step 6 : requête de contrôle post-déploiement** (à coller dans la PR) :
```sql
SELECT (SELECT count(*) FROM boarding_enrollments be JOIN enrollments e ON e."Id" = be."EnrollmentId"
        JOIN school_years y ON y."Id" = e."SchoolYearId" AND y."IsActive" WHERE be."IsActive" AND NOT be."IsDeleted") AS stays,
       (SELECT count(*) FROM enrollments e JOIN school_years y ON y."Id" = e."SchoolYearId" AND y."IsActive"
        WHERE e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted") AS legacy;
```
- [ ] **Step 7 : commit** `feat(internat): réconciliation à usage unique des séjours avec l'ancien modèle`.

---

### Task 2 : Service d'affectation (tests d'abord)

**Files:**
- Create: `src/SamaEcole.Application/Boarding/Assignments/{IBoardingAssignmentService,BoardingAssignmentService,BoardingConflicts}.cs`, `tests/SamaEcole.IntegrationTests/Boarding/BoardingAssignmentServiceTests.cs`
- Modify: `src/SamaEcole.Application/DependencyInjection.cs` (enregistrement scoped), `src/SamaEcole.Persistence/Errors/UniqueConstraintCatalog.cs`

**Interfaces — Produces :**
```csharp
public interface IBoardingAssignmentService
{
    /// Crée ou transfère le séjour ACTIF de l'inscription. N'appelle PAS SaveChanges.
    Task<BoardingEnrollment> AssignAsync(Enrollment enrollment, BoardingRegime regime, Guid? bedId, Guid? roomId,
        uint? stayRowVersion, bool requireStayRowVersion, CancellationToken cancellationToken);

    /// Clôt le séjour actif (EndDate = aujourd'hui, BedId = null). N'appelle PAS SaveChanges. Null si aucun séjour actif.
    Task<BoardingEnrollment?> EndAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// Lignes de pension manquantes de l'inscription (jamais de doublon, jamais de retrait). N'appelle PAS SaveChanges.
    Task AddMissingBoardingFeeAsync(Enrollment enrollment, CancellationToken cancellationToken);
}
public static class BoardingConflicts
{
    /// SaveChanges qui traduit la violation de UX_boarding_enrollments_active_bed en 409 BED_UNAVAILABLE.
    public static Task SaveAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken);
}
```

- [ ] **Step 1 : tests (échouent).** `BoardingAssignmentServiceTests` — chaque test construit `new BoardingAssignmentService(ctx, TimeProvider.System)` sur `_db.NewAppContext(EcoleA)` ; jeu de données : école A, année active, deux pavillons (`Garcons` « P-G » avec chambre 101 de 2 lits, `Filles` « P-F » avec chambre 201 de 2 lits dont le lit 2 en `Maintenance`), élèves `M` (m1, m2, m3) et `F` (f1), une inscription active chacun. Cas :
  1. `Interne_With_A_Bed_Creates_An_Active_Stay_Starting_Today`
  2. `Interne_With_A_Room_Takes_The_Lowest_Free_Bed` (m1 → lit 1 ; m2 → lit 2 ; m3 → 422 « Cette chambre a atteint sa capacité maximale. »)
  3. `Re_Assigning_To_The_Same_Room_Keeps_The_Current_Bed`
  4. `Half_Board_Never_Gets_A_Bed_And_Ignores_A_Room` (`bedId` fourni avec `DemiPensionnaire` → 422 ; `roomId` fourni → ignoré)
  5. `Interne_Without_Bed_Nor_Room_Is_Refused` (422 champ `BedId`)
  6. `A_Bed_Held_By_Another_Boarder_Is_A_409_BED_UNAVAILABLE` (`BusinessRuleException.Code == "BED_UNAVAILABLE"`)
  7. `A_Bed_In_Maintenance_Is_Refused_With_422`
  8. `A_Boy_Cannot_Take_A_Bed_In_A_Girls_Dormitory` (422 champ `BedId`) et `A_Mixte_Dormitory_Accepts_Anyone`
  9. `Transfer_Updates_The_Same_Stay_And_Frees_The_Old_Bed` (même `Id` de séjour, ancien lit réutilisable ensuite)
  10. `Transfer_Requires_The_Stay_RowVersion_Unless_Told_Otherwise` (`requireStayRowVersion = true` et jeton nul → 422 ; jeton périmé → `ConcurrencyConflictException`)
  11. `Ending_Closes_The_Stay_And_Keeps_The_Boarding_Fee_Line` (ligne de pension déjà posée persiste ; `EndDate` = aujourd'hui ; `BedId` nul) et `Ending_Is_Refused_While_A_Leave_Is_Open` (`BusinessRuleException` code `LEAVE_IN_PROGRESS`)
  12. `The_Boarding_Fee_Is_Added_Once` (deux appels → une seule `EnrollmentFeeLine` de pension, `TotalDue` incrémenté une fois) — réutiliser le semis de `ChangeBoardingAssignmentTests.Assigning_A_Room_Adds_The_Pension_Line_Once` (catégorie `IsBoardingFee`, `ClassFee`).
  13. `Two_Concurrent_Assignments_On_The_Last_Bed_One_Wins_The_Other_Gets_BED_UNAVAILABLE` — deux contextes, deux inscriptions, même lit, `AssignAsync` des deux **avant** les deux `BoardingConflicts.SaveAsync` : le premier réussit, le second lève `BusinessRuleException` `BED_UNAVAILABLE`, un seul séjour actif sur le lit.

- [ ] **Step 2 : implémentation.** `BoardingConflicts` :
```csharp
public static class BoardingConflicts
{
    public const string BedUnavailable = "BED_UNAVAILABLE";

    public static async Task SaveAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex) when (ex.TechnicalDetail.Contains("UX_boarding_enrollments_active_bed", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("Ce lit vient d'être attribué à un autre pensionnaire.", BedUnavailable);
        }
    }
}
```
`UniqueConstraintCatalog` (`Persistence/Errors`) : ajouter, dans `ByConstraintFragment`, les entrées `UX_boarding_enrollments_active_enrollment` (« Cet élève est déjà hébergé à l'internat pour cette année. »), `UX_dormitories_name` (« Un pavillon porte déjà ce nom. »), `UX_dormitory_rooms_name` (« Une chambre porte déjà ce nom dans ce pavillon. »), `UX_beds_number` (« Ce numéro de lit est déjà utilisé dans cette chambre. ») — vérifier d'abord leur absence et le test unitaire du catalogue s'il existe.

`BoardingAssignmentService` (primary constructor `(IApplicationDbContext dbContext, TimeProvider timeProvider)`) ; `AssignAsync` suit **dans cet ordre** :
```csharp
if (!Enum.IsDefined(regime)) throw Invalid("Regime", "Le régime d'hébergement indiqué n'est pas valide.");

var stay = await dbContext.BoardingEnrollments
    .FirstOrDefaultAsync(b => b.EnrollmentId == enrollment.Id && b.IsActive, cancellationToken);

Bed? bed = null;
if (regime == BoardingRegime.Interne)
{
    if (bedId is { } explicitBed)
    {
        bed = await dbContext.Beds.FirstOrDefaultAsync(b => b.Id == explicitBed, cancellationToken)
            ?? throw Invalid("BedId", "Le lit indiqué n'existe pas dans votre établissement.");
    }
    else if (roomId is { } room)
    {
        bed = await FindFreeBedAsync(room, stay, cancellationToken);   // 422 « capacité maximale » s'il n'y en a pas
    }
    else
    {
        throw Invalid("BedId", "Un lit est requis pour un régime Interne.");
    }

    if (bed.Status == BedStatus.Maintenance) throw Invalid("BedId", "Ce lit est en maintenance.");

    await EnsureGenderAsync(enrollment.StudentId, bed, cancellationToken);

    if (await dbContext.BoardingEnrollments.AnyAsync(
            b => b.BedId == bed.Id && b.IsActive && (stay == null || b.Id != stay.Id), cancellationToken))
    {
        throw new BusinessRuleException("Ce lit est déjà occupé.", BoardingConflicts.BedUnavailable);
    }
}
else if (bedId is not null)
{
    throw Invalid("BedId", "Un demi-pensionnaire n'occupe pas de lit.");
}
// DemiPensionnaire : une chambre éventuelle (ancien écran) est ignorée (D4).

if (stay is null)
{
    stay = new BoardingEnrollment
    {
        SchoolId = enrollment.SchoolId, StudentId = enrollment.StudentId, EnrollmentId = enrollment.Id,
        Regime = regime, BedId = bed?.Id, StartDate = Today(), IsActive = true
    };
    dbContext.BoardingEnrollments.Add(stay);
}
else
{
    if (requireStayRowVersion && stayRowVersion is null) throw Invalid("RowVersion", "Le jeton de version du séjour est requis pour un transfert.");
    if (stayRowVersion is { } token) dbContext.SetOriginalConcurrencyToken(stay, token);
    stay.Regime = regime;
    stay.BedId = bed?.Id;
}
return stay;
```
`FindFreeBedAsync(roomId, stay, ct)` : charge la chambre (`DormitoryRooms`, vivante, sinon 422 « La chambre indiquée n'existe pas dans votre établissement. » sur `RoomId`) ; si `stay?.BedId` désigne un lit **de cette chambre**, le retourne (rien ne bouge) ; sinon le lit `Available` vivant de plus petit `BedNumber` non tenu par un séjour actif ; aucun → `Invalid("RoomId", "Cette chambre a atteint sa capacité maximale.")`. `EnsureGenderAsync` : charge `Student.Gender` et le `Dormitory.Gender` du lit (via chambre) ; `Mixte` accepte ; `M` ↔ `Garcons`, `F` ↔ `Filles` ; sinon `Invalid("BedId", "Ce pavillon accueille uniquement des garçons." | "... des filles.")`. `Invalid(field, message)` = `new ValidationException([new ValidationFailure(field, message)])` (alias `ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException` si `FluentValidation` est importé). `Today()` = `DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)` (Dakar = UTC+0, sans changement d'heure).

`EndAsync` : charge le séjour actif de l'inscription (null → retourne null) ; `if (await dbContext.BoardingLeaves.AnyAsync(l => l.BoardingEnrollmentId == stay.Id && l.ActualReturnDate == null, ct)) throw new BusinessRuleException("Enregistrez d'abord le retour de la sortie en cours.", "LEAVE_IN_PROGRESS");` ; `stay.IsActive = false; stay.EndDate = Max(Today(), stay.StartDate); stay.BedId = null;`.

`AddMissingBoardingFeeAsync` : **déplacer** ici la logique de `ChangeBoardingAssignmentCommandHandler` (lignes de pension manquantes via `BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync`, mois de scolarité résolus depuis `SchoolSettings` avec repli sur `SchoolSettingsDefaults`), en ajoutant les lignes et en incrémentant `enrollment.TotalDue` ; sans `SaveChanges`.

Enregistrement : `services.AddScoped<IBoardingAssignmentService, BoardingAssignmentService>();` à côté des autres services scoped de `DependencyInjection.cs` (lire le fichier pour le style exact).

- [ ] **Step 3 :** build + `dotnet test ... --filter "FullyQualifiedName~BoardingAssignmentServiceTests"` — Expected: PASS.
- [ ] **Step 4 : commit** `feat(internat): service d'affectation des pensionnaires (lit, genre, maintenance, pension)`.

---

### Task 3 : Commandes `AssignBed` et `EndBoarding`

**Files:** Create `src/SamaEcole.Application/Boarding/Boarders/{BoarderDtos,BoarderReader}.cs`, `AssignBed/AssignBedCommand.cs`, `EndBoarding/EndBoardingCommand.cs` ; Test `tests/SamaEcole.UnitTests/Boarding/BoarderCommandValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/BoarderCommandsTests.cs`.

**Interfaces — Produces :**
```csharp
public record BoarderListItemDto(
    Guid Id, Guid StudentId, Guid EnrollmentId, string StudentName, string Matricule, string ClassroomName,
    BoardingRegime Regime, bool IsActive, Guid? BedId, int? BedNumber, Guid? RoomId, string? RoomName,
    Guid? DormitoryId, string? DormitoryName, DateOnly StartDate, DateOnly? EndDate, uint RowVersion);
public record AssignBedCommand(Guid EnrollmentId, BoardingRegime Regime, Guid? BedId, bool IncludeBoardingFee, uint? RowVersion)
    : IRequest<BoarderListItemDto>, IAuditableRequest;
public record EndBoardingCommand(Guid BoarderId, uint RowVersion) : IRequest<Unit>, IAuditableRequest;
```

- [ ] **Step 1 : tests unitaires (échouent)** : `AssignBedCommandValidator` — `EnrollmentId` non vide ; `Regime` défini ; `Interne` ⇒ `BedId` requis ; `DemiPensionnaire` ⇒ `BedId` interdit. `EndBoardingCommandValidator` — `BoarderId` non vide.
- [ ] **Step 2 : tests d'intégration (échouent)** : création d'un séjour (régime/lit/`StartDate` aujourd'hui) ; transfert (même `Id`, lit libéré) ; transfert sans jeton → 422, jeton périmé → `ConcurrencyConflictException` ; inscription d'une **autre école**, d'une **année non active** ou **annulée** → `KeyNotFoundException`/422 (reprendre les gardes de `ChangeBoardingAssignmentCommandHandler` : année active obligatoire, inscription de l'année active, sinon 404) ; `IncludeBoardingFee` ajoute la pension une fois et `TotalDue` ; `EndBoarding` : séjour clos, lit libre, ligne de pension conservée, jeton périmé → conflit, sortie ouverte → `LEAVE_IN_PROGRESS`, séjour d'une autre école → `KeyNotFoundException`.
- [ ] **Step 3 : implémentation.** `AssignBedCommandHandler(IApplicationDbContext, IBoardingAssignmentService)` : année active (`ValidationException` « Aucune année scolaire active… » comme l'ancien handler), inscription `Id == request.EnrollmentId && SchoolYearId == activeYear.Id && Status != Cancelled` (sinon `KeyNotFoundException($"Inscription {id} introuvable.")`), puis `service.AssignAsync(enrollment, request.Regime, request.BedId, null, request.RowVersion, requireStayRowVersion: true, ct)` — **mais** `requireStayRowVersion` ne vaut vrai que s'il existe déjà un séjour actif : le service lève sinon seulement quand un séjour existe (c'est le comportement du code ci-dessus) ; si `IncludeBoardingFee` → `service.AddMissingBoardingFeeAsync(enrollment, ct)` ; `BoardingConflicts.SaveAsync(dbContext, ct)` ; retour `BoarderReader.GetItemAsync(dbContext, stay.Id, ct)`.
  `EndBoardingCommandHandler` : charge le séjour par `Id` (404), `SetOriginalConcurrencyToken(stay, request.RowVersion)`, `service.EndAsync(stay.EnrollmentId, ct)`, `SaveChangesAsync`, `Unit.Value`. (Le séjour est déjà suivi : `EndAsync` le retrouve par l'inscription ; le jeton posé avant s'applique.)
  `BoarderReader.GetItemAsync(db, id, ct)` : projette `BoarderListItemDto` pour un séjour (jointures `Students`, `Enrollments`→`Classrooms`, `Beds` ⟕, `DormitoryRooms` ⟕, `Dormitories` ⟕ ; `RowVersion = EF.Property<uint>(be, "xmin")`), `KeyNotFoundException` si absent. Utiliser des sous-requêtes `FirstOrDefault` plutôt que des `LEFT JOIN` GroupJoin si la traduction EF pose problème ; la suite de tests arbitre.
- [ ] **Step 4 :** build + tests — Expected: PASS.
- [ ] **Step 5 : commit** `feat(internat): commandes d'affectation de lit et de fin de séjour`.

---

### Task 4 : Requêtes pensionnaires, fiche et profil (masquage de `MedicalNotes`)

**Files:** Create `Boarders/ListBoarders/ListBoardersQuery.cs`, `Boarders/GetBoarder/GetBoarderQuery.cs`, `Boarders/UpdateBoarderProfile/UpdateBoarderProfileCommand.cs` ; Test `tests/SamaEcole.UnitTests/Boarding/BoarderProfileValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/BoarderQueriesTests.cs`.

**Interfaces — Produces :**
```csharp
public record PaginatedBoarders(IReadOnlyList<BoarderListItemDto> Items, int TotalCount, int Page, int PageSize);
public record ListBoardersQuery : IRequest<PaginatedBoarders>
{ public Guid? DormitoryId {get;init;} public Guid? RoomId {get;init;} public BoardingRegime? Regime {get;init;}
  public string Status {get;init;} = "active";   // active | ended | awaitingBed
  public string? Search {get;init;} public int Page {get;init;} = 1; public int PageSize {get;init;} = 20; }
public record AllowedExitPersonDto(string Name, string Relationship, string Phone);
public record BoarderDetailDto(BoarderListItemDto Boarder, string? MedicalNotes, string? EmergencyContactName,
    string? EmergencyContactPhone, IReadOnlyList<AllowedExitPersonDto> AllowedExitPersons);
public record GetBoarderQuery(Guid Id) : IRequest<BoarderDetailDto>;
public record UpdateBoarderProfileCommand(Guid Id, string? MedicalNotes, string? EmergencyContactName,
    string? EmergencyContactPhone, IReadOnlyList<AllowedExitPersonDto> AllowedExitPersons, uint RowVersion) : IRequest<BoarderDetailDto>;
```

- [ ] **Step 1 : tests unitaires (échouent)** : profil — `MedicalNotes` ≤ 2000 sans HTML ; contact ≤ 150 ; téléphone `MustBeValidSenegalPhone` ≤ 30 ; `AllowedExitPersons` ≤ 10, chacune avec `Name` requis ≤ 150, `Relationship` ≤ 50, `Phone` valide.
- [ ] **Step 2 : tests d'intégration (échouent)** : liste — filtres `dormitoryId`, `roomId`, `regime`, `status` (`active` par défaut = séjours actifs de l'année active ; `ended` = clos de l'année active ; `awaitingBed` = `Interne` actif sans lit), `search` (nom ou matricule, insensible à la casse, ≥ 2 caractères sinon ignoré), tri par nom, pagination (`pageSize` plafonné à 100, `page` ≥ 1), **aucun séjour d'une autre école** ; fiche — `KeyNotFoundException` pour une autre école ; **masquage** : un appelant `Directeur` ou `Surveillant` (`TestCurrentUser(id, Role.Surveillant)`) reçoit `MedicalNotes`, `Secretariat` reçoit `null` ; profil — enregistre contact/personnes habilitées/notes pour `Directeur` et `Surveillant`, `Secretariat` avec `MedicalNotes` non nul → `ForbiddenException` **sans** que la valeur apparaisse dans le message, `Secretariat` avec `MedicalNotes` nul → **fiche médicale existante conservée** et autres champs mis à jour, jeton périmé → conflit.
- [ ] **Step 3 : implémentation.** `ListBoardersQueryHandler` : année active (aucune → page vide) ; base = séjours dont l'inscription est de l'année active ; prédicats selon `Status` ; `Search` appliqué sur `Student.FullName`/`Matricule` en `ToLower().Contains(...)` (comme `SearchBoardableStudents`) ; `TotalCount` avant `Skip/Take` ; projection par `BoarderReader` (un helper `Project(IQueryable<BoardingEnrollment>)` partagé avec `GetItemAsync`). `GetBoarderQueryHandler(IApplicationDbContext, ICurrentUserService)` : `var canReadMedical = currentUser.Role is Role.Directeur or Role.Surveillant;` → `MedicalNotes = canReadMedical ? stay.MedicalNotes : null`. `UpdateBoarderProfileCommandHandler` : si `currentUser.Role is Role.Secretariat && request.MedicalNotes is not null` → `throw new ForbiddenException("Seuls le Directeur et le Surveillant peuvent renseigner la fiche médicale.")` (aucune interpolation de la valeur) ; `SetOriginalConcurrencyToken` ; pour `Secretariat` la fiche médicale n'est **pas** touchée, pour les deux autres rôles `MedicalNotes` est remplacée (nulle ou vide = effacée) ; `AllowedExitPersons` remplacée (liste de `AllowedExitPerson`, textes `Trim()`).
- [ ] **Step 4 :** build + tests — Expected: PASS.
- [ ] **Step 5 : commit** `feat(internat): liste et fiche des pensionnaires, profil et masquage de la fiche médicale`.

---

### Task 5 : Routes pensionnaires dans `BoardingController`

**Files:** Modify `src/SamaEcole.Web/Controllers/BoardingController.cs` ; Test `tests/SamaEcole.FunctionalTests/Boarding/BoardersEndpointsTests.cs`.

- [ ] **Step 1 : actions** (avec `[ProducesResponseType]` 200/204, 403, 404, 409, 422, comme les actions existantes). Rôles : lecture `ReadRoles` ; `assign-bed`, `unassign-bed` et `profile` : `ReadRoles` (Directeur, Secretariat, Surveillant — spec §5.1) ; la règle `MedicalNotes` est dans le handler.
```csharp
public record AssignBedRequest(Guid EnrollmentId, BoardingRegime Regime, Guid? BedId, bool IncludeBoardingFee, uint? RowVersion);
public record UpdateBoarderProfileRequest(string? MedicalNotes, string? EmergencyContactName, string? EmergencyContactPhone,
    List<AllowedExitPersonDto>? AllowedExitPersons, uint RowVersion);

[HttpPost("assign-bed")] [Authorize(Roles = ReadRoles)]
public async Task<IActionResult> AssignBed([FromBody] AssignBedRequest r, CancellationToken ct)
    => Ok(await mediator.Send(new AssignBedCommand(r.EnrollmentId, r.Regime, r.BedId, r.IncludeBoardingFee, r.RowVersion), ct));

[HttpDelete("unassign-bed/{boarderId:guid}")] [Authorize(Roles = ReadRoles)]
public async Task<IActionResult> UnassignBed(Guid boarderId, [FromQuery] uint rowVersion, CancellationToken ct)
{ await mediator.Send(new EndBoardingCommand(boarderId, rowVersion), ct); return NoContent(); }

[HttpGet("boarders")] [Authorize(Roles = ReadRoles)]
public async Task<IActionResult> ListBoarders([FromQuery] Guid? dormitoryId, [FromQuery] Guid? roomId,
    [FromQuery] BoardingRegime? regime, [FromQuery] string? status, [FromQuery] string? search,
    [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    => Ok(await mediator.Send(new ListBoardersQuery
    { DormitoryId = dormitoryId, RoomId = roomId, Regime = regime, Status = status ?? "active", Search = search, Page = page, PageSize = pageSize }, ct));

[HttpGet("boarders/{id:guid}")] [Authorize(Roles = ReadRoles)]
public async Task<IActionResult> GetBoarder(Guid id, CancellationToken ct) => Ok(await mediator.Send(new GetBoarderQuery(id), ct));

[HttpPut("boarders/{id:guid}/profile")] [Authorize(Roles = ReadRoles)]
public async Task<IActionResult> UpdateBoarderProfile(Guid id, [FromBody] UpdateBoarderProfileRequest r, CancellationToken ct)
    => Ok(await mediator.Send(new UpdateBoarderProfileCommand(id, r.MedicalNotes, r.EmergencyContactName,
        r.EmergencyContactPhone, r.AllowedExitPersons ?? [], r.RowVersion), ct));
```
Une valeur d'enum hors domaine (`regime=99`) doit être refusée en 422 (validateur `IsInEnum`), jamais persistée.

- [ ] **Step 2 : tests fonctionnels (HTTP → PostgreSQL), même squelette que `BoardingDormitoriesEndpointsTests`** (compte Surveillant créé par `SeedAsOwnerAsync`, `EnableInternatAsync`, noms uniques, `Tokens`, `ApiError`) ; un élève et une inscription actifs semés par `SeedAsOwnerAsync` (année active, classe, élève, inscription). Tests : module désactivé → 403 `MODULE_DISABLED` sur `GET /boarders` ; parcours Directeur : créer pavillon + chambre (lits), `assign-bed` → 200, `GET /boarders` le liste, `GET /boarders/{id}`, `PUT .../profile`, transfert (jeton), `DELETE unassign-bed` → 204 puis `status=ended` ; jeton périmé sur transfert → 409 ; lit occupé → 409 `BED_UNAVAILABLE` ; lit en maintenance → 422 ; genre incompatible → 422 ; `regime=99` → 422 ; **masquage** : `Secretariat` lit la fiche → `medicalNotes` nul, `Surveillant` → valeur présente (poser la valeur via `Directeur`) ; `Secretariat` envoie `medicalNotes` → 403 ; `Enseignant` et `Finance` → 403 sur chaque route (`[Theory]`) ; `pageSize=1000` → 100 maximum.
- [ ] **Step 3 :** `dotnet build` puis `dotnet test tests/SamaEcole.FunctionalTests --no-build --filter "FullyQualifiedName~Boarding"` — Expected: PASS.
- [ ] **Step 4 : commit** `feat(internat): endpoints pensionnaires (affectation, fin de séjour, liste, fiche, profil)`.

---

### Task 6 : Bascule des lectures (tableau de bord, recherche, fiche élève)

**Files:** Modify `Internat/Queries/GetInternatDashboard/GetInternatDashboardQuery.cs`, `Internat/Queries/SearchBoardableStudents/SearchBoardableStudentsQuery.cs`, `Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs` ; Test (réécrire) `tests/SamaEcole.IntegrationTests/Internat/GetInternatDashboardQueryTests.cs`, `SearchBoardableStudentsQueryTests.cs`, `tests/SamaEcole.IntegrationTests/Students/GetStudentDetailQueryTests.cs`.

**Contrat de compatibilité (JSON inchangé, `internat.js`/`enrollments.js`/`students.js` ne sont pas modifiés) :**
- `GET /internat/dashboard` : `rooms[]` = **`DormitoryRoom`** vivantes (`roomId` = `DormitoryRoom.Id`, `roomName`, `buildingName` = **nom du pavillon**), `capacity` = lits **hors maintenance**, `occupantsCount` = séjours actifs `Interne` assis dans la chambre de l'année active, `occupants[]` (`boardingStatus` = « Interne »). KPI : `totalCapacity` = somme des capacités ; `totalOccupied` = internes assis ; `interneCount` = séjours `Interne` actifs (assis ou en attente) ; `demiPensionnaireCount` = séjours `DemiPensionnaire` actifs ; `fullRoomsCount` / `roomsWithFreeSpaceCount` inchangés. Aucune année active → chambres vides sans exception.
- `GET /internat/students/search` : même forme ; `boardingStatus` = régime du séjour actif (« Externe » sinon) ; `currentRoomId/Name` = chambre du lit ; `rowVersion` = **`xmin` de l'inscription** (D3).
- Fiche élève : `identity.boardingStatus` / `roomName` (inscription de l'année active) et `academicHistory[].boardingStatus` / `roomLabel` viennent du séjour ; `roomLabel` = « Chambre — Pavillon » (même gabarit que l'ancien « Salle — Bâtiment ») ; régime d'une année passée = celui du **dernier séjour** (par `StartDate` décroissant) de cette inscription, « Externe » s'il n'y en a pas ; chambre supprimée → « Chambre supprimée ».

- [ ] **Step 1 : réécrire les tests existants sur le nouveau modèle (ils échouent).** Les semis cessent de poser `Enrollment.RoomId/BoardingStatus` et de créer des `Room` de type `Dortoir` : ils créent `Dormitory`/`DormitoryRoom`/`Bed` et un `BoardingEnrollment` actif (comme `BoardingQueriesTests`). Conserver **chaque assertion métier** : `Reports_Occupancy_Per_Room_And_Global_Kpis` (occupation, KPIs, chambres pleines/libres) avec en plus un demi-pensionnaire (compté dans `demiPensionnaireCount`, absent des chambres) et un interne en attente (compté dans `interneCount`, absent des chambres) ; `Returns_Empty_Rooms_Without_Throwing_When_No_Active_School_Year` ; `Finds_By_Partial_Name_And_Reports_Current_Boarding_State` ; `Finds_By_Matricule` ; `Handle_Populates_BoardingStatus_And_RoomLabel_For_An_Interne_Student` et les cas « Externe par défaut » de `GetStudentDetailQueryTests`. Ajouter : un lit en maintenance ne compte pas dans `capacity` ; une chambre supprimée n'apparaît pas.
- [ ] **Step 2 : lancer — doivent échouer** (les handlers lisent encore l'ancien modèle).
- [ ] **Step 3 : réécrire `GetInternatDashboardQueryHandler`** (types de réponse **inchangés**, y compris les noms `DormitoryRoomDto`/`BoardingOccupantDto` de l'espace `SamaEcole.Application.Internat.Queries.GetInternatDashboard`, qui diffèrent volontairement de `SamaEcole.Application.Boarding.DormitoryRoomDto`) :
```csharp
var activeYearId = await dbContext.SchoolYears.AsNoTracking().Where(y => y.IsActive)
    .Select(y => (Guid?)y.Id).FirstOrDefaultAsync(cancellationToken);

var rooms = await (from r in dbContext.DormitoryRooms.AsNoTracking()
                   join d in dbContext.Dormitories.AsNoTracking() on r.DormitoryId equals d.Id
                   select new { r.Id, r.Name, DormitoryName = d.Name }).ToListAsync(cancellationToken);

var capacities = (await dbContext.Beds.AsNoTracking()
        .Where(b => b.Status != BedStatus.Maintenance)
        .GroupBy(b => b.DormitoryRoomId).Select(g => new { RoomId = g.Key, Count = g.Count() })
        .ToListAsync(cancellationToken)).ToDictionary(x => x.RoomId, x => x.Count);

var stays = activeYearId is null ? [] : await (
    from be in dbContext.BoardingEnrollments.AsNoTracking()
    where be.IsActive
    join e in dbContext.Enrollments.AsNoTracking() on be.EnrollmentId equals e.Id
    where e.SchoolYearId == activeYearId && e.Status != EnrollmentStatus.Cancelled
    join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
    join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
    select new
    {
        RoomId = dbContext.Beds.Where(b => b.Id == be.BedId).Select(b => (Guid?)b.DormitoryRoomId).FirstOrDefault(),
        StudentId = s.Id, EnrollmentId = e.Id, s.FullName, ClassroomName = c.Name, s.GuardianPhone, be.Regime
    }).ToListAsync(cancellationToken);
```
puis assembler : `occupantsByRoom` = séjours `Interne` avec `RoomId` non nul ; chambres triées par `BuildingName` puis `RoomName` ; KPI comme ci-dessus.

- [ ] **Step 4 : réécrire `SearchBoardableStudentsQueryHandler`** : même requête de base (inscriptions de l'année active non annulées, filtre `ToLower().Contains`, `Take(20)`), en remplaçant `e.BoardingStatus`/`e.RoomId` par
```csharp
Regime = dbContext.BoardingEnrollments.Where(b => b.EnrollmentId == e.Id && b.IsActive)
    .Select(b => (BoardingRegime?)b.Regime).FirstOrDefault(),
RoomId = (from be in dbContext.BoardingEnrollments
          where be.EnrollmentId == e.Id && be.IsActive
          join bed in dbContext.Beds on be.BedId equals bed.Id
          select (Guid?)bed.DormitoryRoomId).FirstOrDefault(),
```
et les noms de chambre depuis `DormitoryRooms` ; `BoardingStatus` = `Regime?.ToString() ?? "Externe"`.

- [ ] **Step 5 : `GetStudentDetailQueryHandler`** : remplacer la requête `boarding` (identité) par
```csharp
.Select(e => new
{
    Regime = dbContext.BoardingEnrollments.Where(b => b.EnrollmentId == e.Id && b.IsActive)
        .Select(b => (BoardingRegime?)b.Regime).FirstOrDefault(),
    RoomName = (from be in dbContext.BoardingEnrollments
                where be.EnrollmentId == e.Id && be.IsActive && be.BedId != null
                join bed in dbContext.Beds.IgnoreQueryFilters() on be.BedId equals bed.Id
                join r in dbContext.DormitoryRooms.IgnoreQueryFilters() on bed.DormitoryRoomId equals r.Id
                select r.IsDeleted ? "Chambre supprimée" : r.Name).FirstOrDefault()
})
```
(`IgnoreQueryFilters` lève aussi le filtre tenant : la RLS reste la barrière, comme ailleurs dans ce fichier pour `ClassroomName` — ajouter la condition `bed.SchoolId == e.SchoolId` pour réimposer le tenant), `identity.BoardingStatus = (boarding?.Regime?.ToString() ?? "Externe")`, `RoomName` seulement si `Regime == Interne`. Pour l'historique, remplacer `e.BoardingStatus`/`RoomLabel` par le **dernier séjour** de l'inscription :
```csharp
Stay = dbContext.BoardingEnrollments.IgnoreQueryFilters()
    .Where(b => b.SchoolId == e.SchoolId && b.EnrollmentId == e.Id && !b.IsDeleted)
    .OrderByDescending(b => b.StartDate).ThenByDescending(b => b.CreatedAt)
    .Select(b => new { b.Regime, b.BedId }).FirstOrDefault(),
```
puis `BoardingStatus = e.Stay?.Regime.ToString() ?? "Externe"` et `RoomLabel` = `Chambre — Pavillon` calculé par une requête séparée groupée sur les `BedId` collectés (pas de N+1) ; `RoomLabel` nul si le séjour est clos (aucun lit).

- [ ] **Step 6 :** `dotnet build` puis `dotnet test` des trois classes réécrites + `dotnet test tests/SamaEcole.FunctionalTests --no-build --filter "FullyQualifiedName~Internat|FullyQualifiedName~Students"` — Expected: PASS.
- [ ] **Step 7 : commit** `feat(internat): le tableau de bord, la recherche et la fiche élève lisent les séjours`.

---

### Task 7 : Bascule des écritures (`ChangeBoardingAssignment`, `CreateEnrollment`)

**Files:** Modify `Internat/Commands/ChangeBoardingAssignment/ChangeBoardingAssignmentCommand.cs`, `Enrollments/Commands/CreateEnrollment/{CreateEnrollmentCommandHandler,CreateEnrollmentCommandValidator}.cs` ; Test (réécrire) `tests/SamaEcole.IntegrationTests/Internat/ChangeBoardingAssignmentTests.cs`, `tests/SamaEcole.IntegrationTests/Enrollments/EnrollmentBoardingTests.cs`, `tests/SamaEcole.UnitTests/Enrollments/CreateEnrollmentCommandValidatorTests.cs`.

- [ ] **Step 1 : réécrire les tests (échouent).** `ChangeBoardingAssignmentTests` : mêmes 7 scénarios sur le nouveau modèle — `Assigning_A_Room_Adds_The_Pension_Line_Once`, `Releasing_A_Student_Keeps_The_Pension_Line_Due`, `Room_At_Capacity_Is_Rejected_With_422` (message inchangé), `Stale_RowVersion_Is_Rejected_With_Concurrency_Conflict` (jeton = `xmin` de l'**inscription**), `Reassigning_The_Same_Student_To_The_Same_Full_Room_Is_Not_Rejected`, `Externe_With_A_RoomId_Is_Rejected_With_422`, `NonExterne_Without_A_RoomId_Is_Rejected_With_422` **devenu** `Interne_Without_A_RoomId_Is_Rejected_With_422` + `DemiPensionnaire_Without_A_RoomId_Is_Accepted_And_Takes_No_Bed` ; ajouter : l'affectation **change le `xmin` de l'inscription** (jeton renvoyé ≠ jeton fourni) ; un transfert de chambre libère l'ancien lit ; un demi-pensionnaire avec une chambre n'occupe aucun lit ; `Externe` clôt le séjour et conserve la pension ; refus de genre ; une chambre créée par la **nouvelle API** (nouvel `Id`) est affectable par l'ancien contrat. `EnrollmentBoardingTests` : `Interne_With_IncludeBoardingFee_Adds_The_Pension_Line`, `..._Without_...`, `Externe_Student_Never_Sees_The_Pension_Line_Even_If_Requested`, `Room_At_Capacity_Is_Rejected_With_422`, `Boarding_Status_Rejected_With_422_When_Internat_Module_Disabled` + **le séjour est créé dans la même transaction** (inscription refusée → aucun séjour ; séjour créé → `StartDate` du jour, lit tenu) et `Enrollment.BoardingStatus/RoomId` **restent à leur défaut**. Validateur unitaire : `Interne` sans `RoomId` invalide ; `DemiPensionnaire` sans `RoomId` **valide** ; `Externe` avec `RoomId` invalide (inchangé).
- [ ] **Step 2 : lancer — doivent échouer.**
- [ ] **Step 3 : `ChangeBoardingAssignmentCommandHandler` devient un adaptateur** (constructeur `(IApplicationDbContext, IBoardingAssignmentService)` ; **signature de commande et `EnrollmentBoardingDto` inchangées**) :
```csharp
if (!Enum.IsDefined(request.BoardingStatus)) throw Validation("BoardingStatus", "Le régime d'hébergement indiqué n'est pas valide.");
var activeYear = ...;                                   // inchangé : 422 « Aucune année scolaire active… »
var enrollment = ...;                                   // inchangé : année active, sinon KeyNotFoundException
if (request.BoardingStatus == BoardingStatus.Externe && request.RoomId is not null)
    throw Validation("RoomId", "Un élève Externe ne peut pas être affecté à une chambre.");
if (request.BoardingStatus == BoardingStatus.Interne && request.RoomId is null)
    throw Validation("RoomId", "Une chambre est requise pour un régime Interne.");

dbContext.SetOriginalConcurrencyToken(enrollment, request.RowVersion);   // jeton legacy = xmin de l'inscription (D3)
enrollment.UpdatedAt = timeProvider.GetUtcNow();                          // « touche » l'inscription : son xmin change à chaque affectation

BoardingEnrollment? stay;
if (request.BoardingStatus == BoardingStatus.Externe)
    stay = await service.EndAsync(enrollment.Id, cancellationToken);
else
    stay = await service.AssignAsync(enrollment, ToRegime(request.BoardingStatus), null, request.RoomId, null, false, cancellationToken);

if (request.IncludeBoardingFee && request.BoardingStatus != BoardingStatus.Externe)
    await service.AddMissingBoardingFeeAsync(enrollment, cancellationToken);

await BoardingConflicts.SaveAsync(dbContext, cancellationToken);
// jeton xmin réel post-écriture (inchangé), puis :
var roomId = stay?.BedId is { } bedId
    ? await dbContext.Beds.AsNoTracking().Where(b => b.Id == bedId).Select(b => (Guid?)b.DormitoryRoomId).FirstOrDefaultAsync(ct) : null;
return new EnrollmentBoardingDto(enrollment.Id, status, roomId, enrollment.TotalDue, rowVersion);
```
`status` = `BoardingStatus.Externe` si séjour clos/absent, sinon le régime du séjour (`Interne`/`DemiPensionnaire`). Injecter `TimeProvider`. La conversion `ToRegime` : `Interne→Interne`, `DemiPensionnaire→DemiPensionnaire`.

- [ ] **Step 4 : `CreateEnrollmentCommandHandler`** : injecter `IBoardingAssignmentService` ; **supprimer** le bloc de capacité (`dbContext.Rooms … occupied >= room.Capacity`, ~lignes 120-137) et `BoardingStatus = request.BoardingStatus, RoomId = request.RoomId` de l'initialiseur de `Enrollment` ; après `dbContext.Enrollments.Add(enrollment)` :
```csharp
if (request.BoardingStatus != BoardingStatus.Externe)
{
    await boardingAssignment.AssignAsync(
        enrollment, request.BoardingStatus == BoardingStatus.Interne ? BoardingRegime.Interne : BoardingRegime.DemiPensionnaire,
        bedId: null, roomId: request.RoomId, stayRowVersion: null, requireStayRowVersion: false, ct);
}
```
et remplacer `await dbContext.SaveChangesAsync(ct);` de cette transaction par `await BoardingConflicts.SaveAsync(dbContext, ct);`. Les lignes de pension de l'inscription restent composées par le code existant (`BoardingFeeLineBuilder`). Garde de module (422 si Internat désactivé) **inchangée**. Validateur : `RoomId` requis seulement si `BoardingStatus == Interne` ; interdit si `Externe` ; libre si `DemiPensionnaire`.

- [ ] **Step 5 :** build + les trois classes + `dotnet test tests/SamaEcole.IntegrationTests --no-build --filter "FullyQualifiedName~Enrollment"` (aucune régression d'inscription) — Expected: PASS.
- [ ] **Step 6 : commit** `feat(internat): l'affectation et l'inscription écrivent les séjours (anciennes routes conservées)`.

---

### Task 8 : Colonnes héritées dépréciées

**Files:** Modify `src/SamaEcole.Domain/Entities/Enrollment.cs`, `src/SamaEcole.Persistence/Configurations/EnrollmentConfiguration.cs`, tests qui sèment encore l'ancien modèle (`BoardingBackfillMigrationTests`, `BoardingReconciliationTests`, `BoardingPurgeTests` si concerné).

- [ ] **Step 1 :** `[Obsolete("Remplacé par BoardingEnrollment (lot C). Plus lu ni écrit ; suppression en base au lot F.")]` sur `Enrollment.BoardingStatus` et `Enrollment.RoomId`. `#pragma warning disable CS0618` / `restore` autour de leur mapping dans `EnrollmentConfiguration` et en tête des fichiers de tests qui sèment l'ancien modèle (commentaire : « ancien modèle volontairement utilisé : reprise/réconciliation »).
- [ ] **Step 2 : garde anti-régression.** Test unitaire `LegacyBoardingColumnsGuardTests` : parcourt les sources de `SamaEcole.Application` et `SamaEcole.Web` (chemin résolu depuis `AppContext.BaseDirectory` jusqu'à la racine du dépôt) et échoue si un fichier **hors** `Boarding/`, `Internat/…/ChangeBoardingAssignmentCommand.cs` (DTO de compat), `CreateEnrollmentCommand*.cs` et `GetStudentDetailQuery.cs` référence `e.BoardingStatus`, `.RoomId` d'une inscription ou `Enrollment.BoardingStatus`. Si la lecture des sources est jugée trop fragile, remplacer par un test de réflexion vérifiant que les deux propriétés portent `ObsoleteAttribute`.
- [ ] **Step 3 :** `dotnet build SamaEcole.sln` — Expected : **0 avertissement** (toute utilisation restante des colonnes héritées apparaît en CS0618 et doit être soit migrée, soit couverte par un `pragma` justifié).
- [ ] **Step 4 : commit** `refactor(internat): déprécie les colonnes héritées Enrollment.BoardingStatus/RoomId`.

---

### Task 9 : Documentation, vérification globale et PR

**Files:** Modify `docs/Volume_4_API_Design.md` (§31 + §28), `docs/Volume_3_DDS.md`, `ACTIVE_CONTEXT.md`.

- [ ] **Step 1 : Volume 4.** §31 : retirer « Pas encore livré : affectation… » ; ajouter les 5 routes pensionnaires (verbe, route, rôles, corps, codes 200/204/403/404/409/422 dont `BED_UNAVAILABLE`, `LEAVE_IN_PROGRESS`), le masquage de `medicalNotes` par rôle, la règle « demi-pensionnaire sans lit », le contrôle de genre et la maintenance (422 sans code dédié). Section « Compatibilité » : les routes `/api/v1/internat/{dashboard,students/search,assignments/{enrollmentId}}` gardent leurs formes JSON, `roomId` = `DormitoryRoom.Id`, jeton `rowVersion` = `xmin` de l'inscription, **suppression au lot F**. Ajouter `BED_UNAVAILABLE` et `LEAVE_IN_PROGRESS` au catalogue d'erreurs (§0.4).
- [ ] **Step 2 : DDS.** Noter que `boarding_enrollments` est désormais la source de vérité et que `enrollments.BoardingStatus/RoomId` sont obsolètes (plus lus ni écrits).
- [ ] **Step 3 : ACTIVE_CONTEXT.** Entrée « Lot C livré : réconciliation (usage unique), pensionnaires, bascule » avec : les prérequis de déploiement (arrêt des anciennes instances, B + C ensemble), la règle « la réconciliation ne se rejoue jamais », la requête de contrôle, et « lot D : sorties et pointage ».
- [ ] **Step 4 : vérification globale, SÉQUENTIELLE** : `dotnet build SamaEcole.sln` (0 avertissement) ; `ef migrations has-pending-model-changes` (aucun changement) ; `dotnet test tests/SamaEcole.UnitTests --no-build` ; `dotnet test tests/SamaEcole.IntegrationTests --no-build` ; `dotnet test tests/SamaEcole.FunctionalTests --no-build` ; `cd src/SamaEcole.Web && node --test tests/js/*.test.mjs`. Un échec « Exception while writing to stream » se relance seul avant de conclure.
- [ ] **Step 5 : commit** `docs(internat): API pensionnaires, compatibilité et état du lot C`, `git push -u origin feat/internat-boarders-switch`, puis PR **dont la base est `feat/internat-dormitories-cqrs`** (diff = lot C seul), gabarit `.github/PULL_REQUEST_TEMPLATE.md`, avec en tête les trois prérequis de déploiement, la requête de contrôle et le rappel « à retargeter sur `main` après la fusion du lot B ». Le jeton GitHub ne peut pas passer une PR en brouillon : le demander à l'équipe.

---

## Self-review

- **Couverture de la spec §4.2 / §6.2 / §10 lot C :** `IBoardingAssignmentService` unique → tâche 2 ; `assign-bed`, `unassign-bed`, `boarders` (liste filtrée/paginée), `boarders/{id}`, complément `PUT profile` → tâches 3-5 ; masquage de `MedicalNotes` (§5.2) → tâche 4 ; bascule de `GetInternatDashboard`, `SearchBoardableStudents`, `CreateEnrollment`, badge fiche élève → tâches 6-7 ; réconciliation (ajout du plan : la reprise du lot A est un instantané) → tâche 1 ; `[Obsolete]` des colonnes héritées → tâche 8. **Hors lot :** sorties/pointage/PDF (D, E), suppression des colonnes et des routes legacy (F), IHM (G).
- **Écarts assumés avec la spec :** codes `GENDER_MISMATCH`/`BED_IN_MAINTENANCE` remplacés par des 422 avec message (D6) ; filtre `onLeave` reporté au lot D (D9) ; demi-pensionnaire n'apparaît plus dans une chambre du tableau de bord (D4).
- **Points à trancher à l'exécution (signalés dans le texte, l'arbitre est la suite de tests) :** traduction EF des `LEFT JOIN` du `BoarderReader` et de la liste d'historique de `GetStudentDetail` (repli : sous-requêtes `FirstOrDefault`, puis requêtes séparées assemblées en mémoire) ; style exact de l'enregistrement dans `DependencyInjection.cs` ; entrées déjà présentes dans `UniqueConstraintCatalog`.
- **Placeholders :** aucun ; les tests des tâches 3 à 7 sont énumérés avec leurs assertions, le code de production des parties délicates (script SQL, service, adaptateur, requêtes réécrites) est écrit.
- **Cohérence des noms :** `AssignAsync(Enrollment, BoardingRegime, Guid?, Guid?, uint?, bool, CancellationToken)`, `EndAsync(Guid, CancellationToken)`, `AddMissingBoardingFeeAsync(Enrollment, CancellationToken)`, `BoardingConflicts.SaveAsync`, `BoarderListItemDto`, `BoarderReader.GetItemAsync` identiques dans toutes les tâches.
