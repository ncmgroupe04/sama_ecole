# SAMA ECOLE

# VOLUME 3 — Database Design Specification (DDS)

**Version :** 2.0
**Statut :** Document de référence pour le développement — remplace la version 1.0
**Changements de cette version :**
- Un seul moteur cible : **PostgreSQL**. Suppression de toute compatibilité SQLite/MySQL et des types associés.
- Ajout du **Chapitre 2 — Stratégie Multi-Tenant**, absent de la version 1.0 malgré la présence de tables SaaS (gap corrigé ici).
- Suppression des tables `SyncQueue` / `SyncHistory` (plus nécessaires : il n'y a plus de synchronisation offline, voir Volume 0 §0.8).

---

## Table des matières

1. Présentation et conventions
2. Stratégie multi-tenant (isolation des données)
3. Structure commune des tables
4. Catalogue des tables par domaine
5. Spécification détaillée des tables clés
6. Dictionnaire des énumérations
7. Cardinalités et relations
8. Stratégie de migration

---

## 1. Présentation et conventions

### 1.1 Objectif

Ce document décrit de façon exhaustive la structure physique de la base de données PostgreSQL de Sama Ecole : tables, colonnes, types, clés, index, contraintes, relations. Il est directement exploitable pour générer les migrations Entity Framework Core.

### 1.2 Moteur unique

**PostgreSQL**, toutes versions confondues (développement, staging, production). Il n'existe plus de notion de « compatibilité multi-moteur » : le modèle utilise sans réserve les fonctionnalités avancées de PostgreSQL (types `UUID` natifs, `JSONB`, `Row-Level Security`, index partiels, contraintes d'exclusion) sans devoir rester compatible avec SQLite ou MySQL.

### 1.3 Standards appliqués à toutes les tables métier

- Clé primaire **UUID** (type natif PostgreSQL `uuid`, génération recommandée côté application avec UUIDv7 pour la triabilité chronologique).
- **Soft delete** (`IsDeleted`, `DeletedAt`, `DeletedBy`) — aucune suppression physique de donnée métier.
- **Audit** (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`).
- **Concurrence optimiste** (`RowVersion` / `xmin` PostgreSQL, ou colonne `xmin` système exploitée directement par EF Core).
- Toutes les dates techniques sont stockées en **UTC** (`timestamptz`) ; le format d'affichage local est une préférence d'établissement gérée uniquement côté présentation (Volume 1 §12.1).

### 1.4 Conventions de nommage

| Élément | Convention | Exemple |
|---|---|---|
| Table | Pluriel, PascalCase | `Students`, `Payments` |
| Colonne | PascalCase | `StudentId`, `CreatedAt` |
| Clé primaire | `Id` (UUID) | — |
| Clé étrangère | `{Table au singulier}Id` | `SchoolId`, `TeacherId` |
| Index | `IX_{Table}_{Colonne}` | `IX_Students_SchoolId` |
| Contrainte | `PK_`, `FK_`, `UQ_`, `CK_` | `FK_Enrollments_Students` |

---

## 2. Stratégie multi-tenant (isolation des données)

> Ce chapitre corrige une lacune de la documentation précédente : la présence d'une table `SchoolTenants` ne suffisait pas à garantir une isolation réelle. Voici la stratégie complète et définitive.

### 2.1 Choix retenu : base unique, schéma partagé, isolation par ligne

Toutes les écoles partagent la même base de données et les mêmes tables. Chaque ligne de donnée métier porte une colonne `SchoolId` (UUID, non nul, indexée) qui la rattache à un établissement. C'est le modèle le plus économique à opérer et le plus simple à faire évoluer pour plusieurs centaines, voire milliers, d'écoles (Volume 0 §0.8).

### 2.2 Double barrière d'isolation

L'isolation ne repose **jamais sur un seul mécanisme**. Deux barrières indépendantes sont actives simultanément :

**Barrière 1 — Row-Level Security PostgreSQL (barrière de base de données, non contournable par un bug applicatif)**

```sql
-- Activation sur chaque table métier
ALTER TABLE "Students" ENABLE ROW LEVEL SECURITY;

-- Politique : une session ne voit que les lignes de son établissement
CREATE POLICY tenant_isolation ON "Students"
    USING ("SchoolId" = current_setting('app.current_school_id')::uuid);
```

À chaque requête, le backend positionne `app.current_school_id` via `SET LOCAL` au début de la transaction, à partir de l'identité de l'utilisateur authentifié. Un rôle applicatif dédié (`sama_ecole_app`) n'a **aucun privilège `BYPASSRLS`**.

**Barrière 2 — Global Query Filter Entity Framework Core (barrière applicative, ergonomique pour le développeur)**

```csharp
modelBuilder.Entity<Student>()
    .HasQueryFilter(s => s.SchoolId == _tenantContext.CurrentSchoolId);
```

Cette barrière évite les oublis de filtre dans le code métier au quotidien, mais **ce n'est pas elle qui garantit la sécurité** : c'est la RLS PostgreSQL qui joue ce rôle en dernier ressort, y compris en cas d'oubli, de bug, ou de requête SQL brute.

### 2.3 Entités hors périmètre `SchoolId`

Les entités suivantes sont globales à la plateforme et gérées par le Super Admin, hors RLS par établissement : `Schools`, `Subscriptions`, `SubscriptionPlans`, `SubscriptionPayments`, `SchoolRegistrationRequests`, `PlatformAdmins`, `PlatformAuditLogs`.

### 2.4 Test obligatoire

Le Volume 8 (Test Strategy) impose un test d'intégration systématique : *« un utilisateur de l'école A ne peut, par aucune requête, lire ou modifier une ligne appartenant à l'école B »* — exécuté à chaque pipeline CI/CD, pas seulement en recette manuelle.

### 2.5 Cache applicatif (Redis) — une troisième surface, hors RLS

Redis est prévu dès la V1 (Volume_6_Dev_Guide.md, « Cache ») mais n'est pas encore consommé en code à ce jour. Les deux barrières de §2.2 (RLS PostgreSQL, Global Query Filter EF Core) protègent **exclusivement PostgreSQL** : un cache applicatif est une troisième surface entièrement hors de leur portée. Une clé de cache qui n'embarque pas le `SchoolId` (ex. une clé littérale `"grading-scale"`, la même pour toutes les écoles) ferait fuiter la donnée d'une école vers toutes les autres dès la première écriture concurrente — silencieusement, sans qu'aucune requête SQL ne soit en cause.

**Règle non négociable dès qu'un cache Redis (ou tout autre cache applicatif partagé) est introduit** : toute clé est composée via `ITenantCacheKeyFactory.BuildKey` (`src/SamaEcole.Application/Common/Interfaces/ITenantCacheKeyFactory.cs`, implémenté par `TenantCacheKeyFactory` dans `SamaEcole.Infrastructure/Multitenancy`), jamais par interpolation de chaîne manuelle. La factory préfixe systématiquement par le `SchoolId` du tenant courant (`ITenantProvider.CurrentSchoolId`, dérivé du JWT — jamais un paramètre modifiable par le client, règle #10) et refuse de construire une clé si aucun tenant n'est résolu (fail closed). Les rares données réellement globales à la plateforme (ex. barème de tarification des abonnements) doivent le documenter explicitement en commentaire à l'endroit de l'appel — l'absence de `SchoolId` doit toujours être un choix visible, jamais un oubli.

---

## 3. Structure commune des tables métier

| Colonne | Type PostgreSQL | Obligatoire |
|---|---|---|
| `Id` | `uuid` | Oui |
| `SchoolId` | `uuid` (FK `Schools.Id`) | Oui (sauf entités globales, §2.3) |
| `CreatedAt` | `timestamptz` | Oui |
| `CreatedBy` | `uuid` (FK `Users.Id`) | Oui |
| `UpdatedAt` | `timestamptz` | Non |
| `UpdatedBy` | `uuid` | Non |
| `DeletedAt` | `timestamptz` | Non |
| `DeletedBy` | `uuid` | Non |
| `IsDeleted` | `boolean` (défaut `false`) | Oui |

---

## 4. Catalogue des tables par domaine

### 4.1 Domaine Plateforme (hors `SchoolId` — Super Admin)

`Schools`, `SubscriptionPlans`, `Subscriptions`, `SubscriptionPayments`, `SchoolRegistrationRequests`, `PlatformAdmins`, `PlatformAuditLogs`, `ActivationKeys`.

### 4.2 Domaine Administration & Sécurité

`Users`, `Roles`, `Permissions`, `RolePermissions`, `UserSessions`, `UserStatusHistory` (historique des suspensions/blocages, Volume 1 §1.1).

### 4.3 Domaine Paramétrage

`SchoolSettings`, `GradingSettings`, `ReportSettings`, `EnrollmentSettings`, `FeeSettings`.

`school_settings` porte, entre autres, le découpage de l'année en périodes d'évaluation (Évolution N°2) :
`EvaluationPeriodType` (`varchar(20)`, `Trimester` par défaut / `Semester` / `Custom`) et `CustomPeriodCount`
(`integer`, 3 par défaut, 2 à 6 utilisé seulement pour `Custom`). Les périodes elles-mêmes restent des lignes
de `terms` (`Label`, `Order`) : ces deux colonnes ne pilotent que leur GÉNÉRATION à la création d'une année.

### 4.4 Domaine Pédagogique

`SchoolYears`, `Terms`, `Levels`, `ClassRooms`, `Subjects`, `Teachers`, `TeacherAssignments`, `Students`, `Guardians`, `StudentGuardians`, `Enrollments`, `EnrollmentDocuments`, `WaitingListEntries`, `StudentTransfers`, `Attendances`, `Grades`, `GradeDetails`, `ReportCards`, `ReportCardDetails`, `ScheduleSlots` (créneaux d'emploi du temps, Volume 1 §21), `TeacherAttendances` (pointage des enseignants, Volume 1 §21.3).

### 4.5 Domaine Finance

`FeeCategories`, `SchoolFees`, `FeeChangeHistory`, `Payments`, `PaymentDetails`, `Receipts`, `ExpenseCategories`, `Expenses`, `ExpenseAttachments`, `FinancialReports`, `EmployeeContracts`, `EmployeeContractHistories`, `FichePaies`, `TeacherHourRecords` (contrats, paie et vacations, Volume 1 §14 — *ajout au catalogue, gap corrigé ici : ces tables existent en base depuis les migrations `AddEmployeeContractLifecycle`/`AddPayrollAndTax`/`AddDocumentsModuleEntities` mais n'y figuraient pas*).

> **Casse réelle en base, ici corrigée en documentation seulement.** Les tables listées ci-dessus en §4.4/§4.5 respectent la convention PascalCase de §1.4 à l'exception de `schedule_slots`, `employee_contracts`, `employee_contract_histories` et `fiche_paies`, créées en snake_case (mêmes migrations que ci-dessus, plus `AddScheduleAndDisbursements`) — `TeacherAttendances` et `TeacherHourRecords`, elles, sont bien en PascalCase. Ce mélange est un fait acquis du schéma : renommer une table déjà appliquée en production est interdit (AGENTS.md, « Ne jamais faire »). Le nom réel en base fait foi pour toute migration ou requête SQL ; ne pas supposer la casse à partir de §1.4 seul.

`employee_contracts` porte, depuis la migration `AddEmployeeContractPayoutMethod` (ticket JGK-K02), deux colonnes supplémentaires : `PayoutMethod` (`varchar(20)`, CHECK IN `Cash`/`BankTransfer`/`Wave`/`OrangeMoney`, NOT NULL, DEFAULT `Cash`) et `PayoutAccountReference` (`varchar(50)`, NULL — RIB/IBAN ou numéro mobile money, texte libre). Distinct de la colonne `PaymentMethod` du domaine Finance élèves (§5.5) : deux domaines qui ne partagent jamais une énumération, même si Wave/Orange Money s'y retrouvent conceptuellement des deux côtés.

### 4.6 Domaine Inventaire

`inventory_categories`, `inventory_items`, `stock_movements`, `item_assignments` (spécification détaillée en §5.9).

### 4.7 Domaine Infrastructure applicative

`AuditLogs`, `Notifications`, `NotificationRecipients`, `BackupHistory` (traçabilité des sauvegardes automatiques côté infrastructure, Volume 9), `ApplicationLogs`.

### 4.8 Domaine Examens officiels

`exam_sessions`, `exam_dossiers`, `exam_results` (spécification détaillée en §5.10).

> Les tables `SyncQueue` et `SyncHistory` de la version 1.0 sont **supprimées** : elles n'ont plus de raison d'être puisqu'il n'existe qu'une seule source de vérité, le serveur (Volume 0 §0.8).

---

## 5. Spécification détaillée des tables clés

### 5.1 `Schools`

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `Name` | `varchar(200)` | NOT NULL |
| `Code` | `varchar(20)` | UNIQUE, NOT NULL |
| `SchoolType` | `varchar(10)` | CHECK IN (`MAT`,`PRI`,`COL`,`LYC`,`MIX`) |
| `Phone`, `Email`, `Address`, `City`, `Country` | `varchar` | — |
| `LogoUrl`, `StampImageUrl`, `DirectorSignatureUrl` | `varchar(500)` | Chemin vers stockage objet (Volume 9) |
| `Status` | `varchar(20)` | CHECK IN (`ACTIVE`,`SUSPENDED`,`RESTRICTED`) — voir Volume 1 §11.3 |
| `CreatedAt` | `timestamptz` | NOT NULL |

### 5.2 `Users`

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK `Schools.Id`, NULL uniquement pour un Super Admin |
| `Email` | `citext` | UNIQUE **global, insensible à la casse** — un email n'identifie qu'un seul compte vivant sur toute la plateforme. `citext` (et non `varchar`) : sur un btree `varchar`, `Directeur@x` et `directeur@x` étaient deux comptes acceptés. Toute écriture est en outre normalisée en minuscules (`EmailNormalizer`). Une même personne qui gère plusieurs écoles se les fait **rattacher** à son compte (`user_schools`), elle n'en recrée pas un second. |
| `PasswordHash` | `varchar(500)` | NOT NULL (géré par ASP.NET Core Identity) |
| `FullName`, `Phone` | `varchar` | — |
| `RoleId` | `uuid` | FK `Roles.Id` |
| `Status` | `varchar(20)` | CHECK IN (`ACTIVE`,`SUSPENDED`,`BLOCKED`) — Volume 1 §1.1 |
| Index | `IX_Users_SchoolId`, `IX_Users_Email` (`UNIQUE ... WHERE "IsDeleted" = false` — un compte soft-deleted ne réserve plus l'adresse) | |

### 5.3 `Students`

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `RegistrationNumber` | `varchar(30)` | UNIQUE **par établissement** (`UQ_Students_SchoolId_RegistrationNumber`) — généré selon Volume 1 §2.1 |
| `FirstName`, `LastName` | `varchar(100)` | NOT NULL |
| `BirthDate` | `date` | NOT NULL |
| `Gender` | `varchar(10)` | CHECK IN (`M`,`F`) |
| `PhotoUrl` | `varchar(500)` | — |
| `Status` | `varchar(20)` | CHECK IN (`ACTIVE`,`TRANSFERRED`,`GRADUATED`,`DROPPED`,`ARCHIVED`) |
| `CurrentClassRoomId` | `uuid` | FK `ClassRooms.Id`, nullable |

### 5.4 `Enrollments`

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `StudentId` | `uuid` | FK `Students.Id`, NOT NULL |
| `SchoolYearId` | `uuid` | FK `SchoolYears.Id`, NOT NULL |
| `ClassRoomId` | `uuid` | FK `ClassRooms.Id`, NOT NULL |
| `Status` | `varchar(20)` | CHECK IN (`PENDING`,`VALIDATED`,`CANCELLED`,`RENEWED`) |
| `DocumentStatus` | `varchar(20)` | CHECK IN (`COMPLETE`,`INCOMPLETE`,`PENDING`) |
| `TotalDue` | `numeric(12,2)` | NOT NULL — transmis automatiquement à Finance (Volume 1 §7.2) |
| `EnrolledAt` | `timestamptz` | NOT NULL |
| Contrainte | `UQ_Enrollments_StudentId_SchoolYearId` — un élève n'a qu'une inscription active par année scolaire | |

### 5.5 `Payments`

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `EnrollmentId` | `uuid` | FK `Enrollments.Id`, NOT NULL |
| `Amount` | `numeric(12,2)` | NOT NULL, CHECK (`Amount` > 0) |
| `PaymentMethod` | `varchar(20)` | CHECK IN (`CASH`,`CHEQUE`,`TRANSFER`,`MOBILE_MONEY`) |
| `Status` | `varchar(20)` | CHECK IN (`PAID`,`PARTIAL`,`CANCELLED`) |
| `ReceivedBy` | `uuid` | FK `Users.Id` — utilisateur du module Finance |
| `PaidAt` | `timestamptz` | NOT NULL |

### 5.6 `Subscriptions` (hors `SchoolId` en tant que filtre RLS — table plateforme, mais rattachée à une école)

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK `Schools.Id`, NOT NULL, UNIQUE (un seul abonnement actif par école) |
| `PlanId` | `uuid` | FK `SubscriptionPlans.Id` |
| `StartDate` | `date` | NULL tant que `Status = AWAITING_PAYMENT` |
| `EndDate` | `date` | NULL tant que `Status = AWAITING_PAYMENT` ; recalculée à chaque paiement confirmé |
| `Status` | `varchar(20)` | CHECK IN (`AWAITING_PAYMENT`,`ACTIVE`,`EXPIRING`,`RESTRICTED`,`SUSPENDED`) |

### 5.7 `SchoolRegistrationRequests` (table plateforme, précède la création de `Schools`)

Voir Volume 1 §11.5. Aucune ligne de cette table ne devient jamais une ligne de `Schools` par simple mise à jour de statut : l'approbation **crée** une nouvelle ligne `Schools` (+ `Users` + `Subscriptions`) dans une transaction dédiée, la demande reste un historique immuable de la candidature.

Deux demandes **en attente** peuvent partager le même `DirectorEmail` (« une école peut retenter »). En revanche, la soumission est refusée si l'e-mail identifie **déjà un compte** `Users` (unicité globale, §5.2) : ce responsable se fait **rattacher** l'école à son compte, il n'en ouvre pas un second. L'approbation revérifie cette unicité (pré-contrôle hors puis sous transaction) et l'e-mail écrit dans `Users.Email` est normalisé (`EmailNormalizer`).

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `TrackingReference` | `varchar(20)` | UNIQUE, index — communiquée au demandeur pour suivre sa demande sans authentification |
| `DirectorFullName` | `varchar(200)` | NOT NULL |
| `DirectorEmail` | `varchar(200)` | NOT NULL — stocké normalisé (minuscules, sans espaces de bord) ; pas d'unicité entre demandes |
| `DirectorPhone` | `varchar(30)` | NOT NULL |
| `DirectorPasswordHash` | `varchar(500)` | NOT NULL — haché dès la soumission, jamais stocké en clair, jamais journalisé |
| `SchoolName` | `varchar(200)` | NOT NULL |
| `SchoolAddress` | `varchar(300)` | NULL |
| `City`, `Region` | `varchar(100)` | NULL |
| `EstimatedStudentCount` | `int` | NULL |
| `RequestedPlanId` | `uuid` | FK `SubscriptionPlans.Id` |
| `Status` | `varchar(20)` | CHECK IN (`PENDING`,`APPROVED`,`REJECTED`) |
| `RejectionReason` | `text` | NULL, obligatoire applicativement si `Status = REJECTED` |
| `ReviewedBy` | `uuid` | FK `Users.Id` (Super Admin), NULL tant que `Status = PENDING` |
| `ReviewedAt` | `timestamptz` | NULL tant que `Status = PENDING` |
| `CreatedSchoolId` | `uuid` | FK `Schools.Id`, NULL, renseigné uniquement après approbation effective |

### 5.8 `SubscriptionPayments` (table plateforme)

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SubscriptionId` | `uuid` | FK `Subscriptions.Id`, NOT NULL |
| `SchoolId` | `uuid` | FK `Schools.Id`, NOT NULL (dénormalisé pour le reporting, jamais utilisé comme filtre RLS ici) |
| `Amount` | `numeric(12,2)` | NOT NULL, CHECK (`Amount` > 0) |
| `Currency` | `varchar(3)` | DEFAULT `XOF` |
| `Method` | `varchar(20)` | CHECK IN (`MOBILE_MONEY`,`BANK_TRANSFER`,`CARD`) |
| `Provider` | `varchar(50)` | ex. `PAYDUNYA`, `CINETPAY` |
| `ProviderTransactionRef` | `varchar(100)` | UNIQUE — référence renvoyée par l'agrégateur, sert à la déduplication du webhook |
| `Status` | `varchar(20)` | CHECK IN (`INITIATED`,`CONFIRMED`,`FAILED`) |
| `WebhookPayloadRaw` | `jsonb` | NULL — copie brute du webhook reçu, pour audit/réconciliation |
| `InitiatedAt` | `timestamptz` | NOT NULL |
| `ConfirmedAt` | `timestamptz` | NULL |

**Règle non négociable** : `Status` ne passe à `CONFIRMED` que via le traitement d'un webhook dont la signature a été vérifiée (Volume 7 §Paiements). Aucune route ne permet à un client de positionner ce statut directement.

### 5.9 Domaine Inventaire — `inventory_categories`, `inventory_items`, `stock_movements`, `item_assignments`

Quatre tables tenant, toutes protégées par la double barrière §2.2 (Global Query Filter EF Core **et** policy RLS posée par la migration `AddInventoryModule`).

**`inventory_categories`** — famille de biens, nomenclature libre propre à chaque école.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK `schools.Id`, NOT NULL, ON DELETE RESTRICT |
| `Name` | `varchar(100)` | NOT NULL, UNIQUE (`SchoolId`, `Name`, `IsDeleted`) |
| `Description` | `varchar(300)` | NULL |
| `xmin` | `xid` | Verrou optimiste (§3) |

**`inventory_items`** — un **lot** de biens identiques, pas une unité physique. Le suivi à l'unité se fait avec un lot de quantité 1.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `Name` | `varchar(150)` | NOT NULL |
| `Code` | `varchar(50)` | NULL. Index unique **partiel** `WHERE "Code" IS NOT NULL AND NOT "IsDeleted"` — saisie libre, souvent le numéro d'immatriculation posé par la mairie/l'État |
| `CategoryId` | `uuid` | FK `inventory_categories.Id`, RESTRICT |
| `QuantityTotal` | `integer` | NOT NULL |
| `QuantityAvailable` | `integer` | NOT NULL — **compteur dérivé**, voir la règle ci-dessous |
| `Condition` | `varchar(20)` | CHECK IN (`Neuf`,`Bon`,`AReparer`,`HorsService`), DEFAULT `Bon` |
| `RoomId` | `uuid` | NULL, FK `rooms.Id`, RESTRICT — réutilise le module Infrastructures |
| `LocationLabel` | `varchar(100)` | NULL — local qui n'est pas une salle (« Réserve A ») |
| `UnitPrice` | `numeric(12,2)` | NULL — **indicatif**, aucune portée comptable (règle #4) |
| `IsConsumable` | `boolean` | NOT NULL DEFAULT `false` — un consommable ne se prête jamais |
| `Notes` | `varchar(500)` | NULL |
| `xmin` | `xid` | Verrou optimiste |

`CHECK CK_inventory_items_quantities` : `QuantityTotal >= 0 AND QuantityAvailable >= 0 AND QuantityAvailable <= QuantityTotal`.

**`stock_movements`** — journal **append-only** du stock, au même régime que `FeeChangeHistory` : la migration n'accorde que `SELECT, INSERT` au rôle `sama_ecole_app`. Pas de `xmin` — une ligne jamais modifiée n'a rien à verrouiller.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `ItemId` | `uuid` | FK `inventory_items.Id`, RESTRICT |
| `Type` | `varchar(20)` | CHECK IN (`Entree`,`Sortie`,`AjustementPositif`,`AjustementNegatif`,`Attribution`,`Restitution`,`MiseAuRebut`,`PerteSurPret`) |
| `Quantity` | `integer` | CHECK (`Quantity` > 0) — le sens vient du `Type`, jamais du signe |
| `MovementDate` | `date` | NOT NULL |
| `Reason` | `varchar(200)` | NOT NULL — c'est le motif qui rend le journal opposable lors d'un contrôle |
| `CounterpartyLabel` | `varchar(150)` | NULL — fournisseur en entrée, destinataire en sortie |
| `AssignmentId` | `uuid` | NULL, **sans FK** — lien informatif vers la décharge, indexé ; une FK imposerait un ordre d'insertion strict dans la transaction qui crée les deux |
| `QuantityTotalAfter` | `integer` | NOT NULL — instantané |
| `QuantityAvailableAfter` | `integer` | NOT NULL — instantané |

**`item_assignments`** — fiche de prêt/attribution, support de la décharge signée.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `ItemId` | `uuid` | FK `inventory_items.Id`, RESTRICT |
| `Quantity` | `integer` | CHECK (`Quantity` > 0) |
| `BeneficiaryType` | `varchar(20)` | CHECK IN (`Eleve`,`Enseignant`,`Personnel`) |
| `StudentId` / `TeacherId` / `UserId` | `uuid` | NULL, FK réelles vers `students`/`teachers`/`users`, RESTRICT |
| `BeneficiaryLabel` | `varchar(150)` | NOT NULL — nom **figé** à l'affectation |
| `AssignedOn` | `date` | NOT NULL |
| `DueOn`, `ReturnedOn` | `date` | NULL |
| `ReturnedQuantity` | `integer` | NULL — cumul des restitutions successives |
| `ReturnCondition` | `varchar(20)` | NULL, même domaine que `Condition` |
| `Status` | `varchar(25)` | CHECK IN (`EnCours`,`Restitue`,`PartiellementRestitue`,`Perdu`), DEFAULT `EnCours` |
| `Notes` | `varchar(500)` | NULL |
| `xmin` | `xid` | Verrou optimiste |

`CHECK CK_item_assignments_beneficiary` : exactement **une** des trois clés de bénéficiaire est renseignée, et elle correspond au `BeneficiaryType` déclaré. Trois FK nullables plutôt qu'un identifiant polymorphe générique : l'intégrité référentielle reste réelle et la RLS couvre le bénéficiaire comme le bien.

**Règles non négociables du domaine :**

1. **`QuantityAvailable` n'est jamais écrit par un endpoint.** Il ne varie que dans la transaction d'un `stock_movements`, sur l'entité chargée sous verrou `xmin` — même traitement qu'`Enrollments.AmountPaid` face à la caisse. Le point de passage unique côté code est `StockLedger` (`SamaEcole.Application/Inventory/Common`).
2. **Le journal ne se rature pas.** Une erreur se corrige par un mouvement inverse. Le `GRANT` restreint est le mécanisme réel ; la discipline de code n'en est que le reflet.
3. **`Condition` est l'état dominant d'un LOT**, pas une répartition. Le détail par état s'obtient en scindant en deux lots et en transférant par un ajustement.

---

### 5.10 Domaine Examens officiels — `exam_sessions`, `exam_dossiers`, `exam_results`

Trois tables tenant, snake_case (convention des modules les plus récents, §4.5), protégées par la double barrière §2.2. CFEE, BFEM et BAC vivent dans le même modèle : seul `ExamType` distingue une campagne sans série (CFEE) d'une campagne à séries/options (BFEM, BAC).

**`exam_sessions`** — une campagne d'examen de l'école pour une année scolaire donnée.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK `schools.Id`, NOT NULL |
| `SchoolYearId` | `uuid` | FK `school_years.Id`, NOT NULL, RESTRICT |
| `ExamType` | `varchar(10)` | CHECK IN (`CFEE`,`BFEM`,`BAC`) |
| `Series` | `varchar(20)` | NULL — série/option (`S1`,`S2`,`L`,`G`...) ; NULL pour `CFEE`, qui n'a pas de série |
| `CenterName` | `varchar(150)` | NULL — centre par défaut de la session, surchageable par dossier |
| `Status` | `varchar(25)` | CHECK IN (`EnPreparation`,`InscriptionsOuvertes`,`Transmis`,`Clos`), DEFAULT `EnPreparation` |
| `xmin` | `xid` | Verrou optimiste (§3) |

`CREATE UNIQUE INDEX UX_exam_sessions_school_year_type_series ON exam_sessions ("SchoolId", "SchoolYearId", "ExamType", COALESCE("Series", ''))` — un `COALESCE` est nécessaire car `NULL` n'est jamais égal à `NULL` dans une contrainte `UNIQUE` standard ; sans lui, rien n'empêcherait deux sessions `CFEE` de la même année scolaire (`Series` NULL dans les deux cas).

**`exam_dossiers`** — le dossier d'un candidat pour une session. Un élève peut avoir plusieurs dossiers au fil des années (redoublement) ou des sessions, jamais deux pour la même session.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `ExamSessionId` | `uuid` | FK `exam_sessions.Id`, RESTRICT |
| `StudentId` | `uuid` | FK `students.Id`, RESTRICT |
| `ClassroomId` | `uuid` | FK `classrooms.Id`, RESTRICT — classe au moment de l'ouverture du dossier, **figée** (règle ci-dessous) |
| `CandidateNumber` | `varchar(20)` | NULL — numéro de table, généré uniquement à l'attribution (règle ci-dessous) |
| `ExamCenterName` | `varchar(150)` | NULL — hérite de `exam_sessions.CenterName` si non renseigné |
| `BirthCertificateNumber` | `varchar(50)` | NULL — numéro d'enregistrement de l'extrait de naissance |
| `BirthCertificatePresent` | `boolean` | NOT NULL DEFAULT `false` |
| `CivilStatusConforming` | `boolean` | NULL — `NULL` = non encore contrôlé, distinct de `false` |
| `CivilStatusNotes` | `varchar(500)` | NULL — détail d'un écart déclaré (ex. orthographe du nom différente sur l'extrait) |
| `Status` | `varchar(20)` | CHECK IN (`Incomplet`,`Complet`,`Transmis`,`Valide`), DEFAULT `Incomplet` |
| `TransmittedOn` | `date` | NULL |
| `xmin` | `xid` | Verrou optimiste |

`UQ_exam_dossiers_session_student` : `UNIQUE ("SchoolId", "ExamSessionId", "StudentId")`. `UX_exam_dossiers_session_candidate_number` : index unique **partiel** `("SchoolId", "ExamSessionId", "CandidateNumber") WHERE "CandidateNumber" IS NOT NULL`.

**`exam_results`** — résultat et mention à la délibération, support des statistiques de taux de réussite.

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK, NOT NULL |
| `ExamDossierId` | `uuid` | FK `exam_dossiers.Id`, RESTRICT, UNIQUE — au plus un résultat par dossier |
| `IsAdmitted` | `boolean` | NOT NULL |
| `Mention` | `varchar(20)` | NULL, CHECK IN (`Passable`,`AssezBien`,`Bien`,`TresBien`) — NULL si non admis ou `ExamType = CFEE` (le CFEE n'attribue pas de mention) |
| `AverageScore` | `numeric(5,2)` | NULL — moyenne transmise par l'IEF/IA, quand communiquée |
| `DeliberatedOn` | `date` | NOT NULL |
| `xmin` | `xid` | Verrou optimiste |

**Règles non négociables du domaine :**

1. **`CandidateNumber` n'est écrit que dans la transaction de l'endpoint d'attribution** (`POST /exams/dossiers/{id}/assign-center`), jamais à la création du dossier — même contrat que le matricule élève/enseignant (AGENTS.md règle #3).
2. **Un dossier `Incomplet` ne peut jamais passer `Transmis`**, ni figurer dans un export ministériel ou une impression par lot de fiches de candidature. C'est l'audit automatique (Volume 4 §22) qui fait foi, pas une case cochée manuellement.
3. **`ClassroomId` est figé à l'ouverture du dossier.** Un transfert de classe de l'élève en cours d'année ne réécrit jamais un dossier déjà `Transmis` ou `Valide` — l'historique d'examen doit rester celui qui a réellement été transmis à l'IEF/IA.

---

## 6. Dictionnaire des énumérations

| Énumération | Valeurs |
|---|---|
| `SchoolType` | `MAT`, `PRI`, `COL`, `LYC`, `MIX` |
| `UserStatus` | `ACTIVE`, `SUSPENDED`, `BLOCKED` |
| `UserRole` | `SUPER_ADMIN`, `DIRECTOR`, `SECRETARY`, `FINANCE`, `TEACHER` |
| `StudentStatus` | `ACTIVE`, `TRANSFERRED`, `GRADUATED`, `DROPPED`, `ARCHIVED` |
| `EnrollmentStatus` | `PENDING`, `VALIDATED`, `CANCELLED`, `RENEWED` |
| `DocumentStatus` | `COMPLETE`, `INCOMPLETE`, `PENDING` |
| `AcademicPeriodType` | `TRIMESTER`, `SEMESTER`, `ANNUAL` |
| `PaymentType` | `REGISTRATION`, `RE_ENROLLMENT`, `TUITION`, `EXAM`, `OTHER` |
| `PaymentMethod` | `CASH`, `CHEQUE`, `TRANSFER`, `MOBILE_MONEY` |
| `PaymentStatus` | `PAID`, `PARTIAL`, `UNPAID`, `CANCELLED` |
| `SchoolStatus` | `ACTIVE`, `SUSPENDED`, `RESTRICTED` |
| `SubscriptionStatus` | `AWAITING_PAYMENT`, `ACTIVE`, `EXPIRING`, `RESTRICTED`, `SUSPENDED` |
| `RegistrationRequestStatus` | `PENDING`, `APPROVED`, `REJECTED` |
| `SubscriptionPaymentMethod` | `MOBILE_MONEY`, `BANK_TRANSFER`, `CARD` |
| `SubscriptionPaymentStatus` | `INITIATED`, `CONFIRMED`, `FAILED` |
| `NotificationType` | `INFO`, `WARNING`, `ERROR` |

Ces valeurs sont partagées entre la base de données (contraintes `CHECK` ou type `enum` PostgreSQL), le domaine C# (`enum` typé) et le contrat API (Volume 4) — aucune chaîne de caractère magique ne doit être dupliquée entre ces trois couches.

---

## 7. Cardinalités et relations (extrait)

- `Schools (1) — (N) Users`
- `Schools (1) — (N) Students`
- `Students (1) — (N) Enrollments`, `Enrollments (1) — (N) Payments`
- `ClassRooms (1) — (N) Students` (classe courante), `ClassRooms (1) — (N) Enrollments` (historique)
- `Enrollments (1) — (1) TotalDue` répliqué en lecture seule côté Finance (aucune table Finance ne redéfinit ce montant, Volume 1 §7.2)
- `Teachers (N) — (N) ClassRooms` via `TeacherAssignments`
- `Subscriptions (1) — (N) SubscriptionPayments`
- `SchoolRegistrationRequests (1) — (0..1) Schools` (via `CreatedSchoolId`, uniquement après approbation)
- `inventory_categories (1) — (N) inventory_items`, `inventory_items (1) — (N) stock_movements`
- `inventory_items (1) — (N) item_assignments`, chaque fiche pointant vers **un seul** bénéficiaire : `Students`, `Teachers` **ou** `Users` (contrainte `CHECK`, §5.9)
- `Rooms (1) — (N) inventory_items` (emplacement d'entreposage, facultatif — un lot peut n'avoir qu'un libellé libre)
- `Teachers (1) — (N) EmployeeContracts` (historique de contrats, jamais supprimé — §4.5), `EmployeeContracts (1) — (N) FichePaies`, `EmployeeContracts (1) — (N) TeacherHourRecords`
- `Teachers (1) — (N) ScheduleSlots`, `Teachers (1) — (N) TeacherAttendances`
- `exam_sessions (1) — (N) exam_dossiers`, `exam_dossiers (1) — (0..1) exam_results` (§5.10)
- `Students (1) — (N) exam_dossiers` (un élève accumule un dossier par session au fil des années)

Un diagramme entité-association complet (ERD) doit être maintenu à jour dans le dépôt de code (ex. via `dbdiagram.io` ou export EF Core), et non uniquement dans ce document texte.

---

## 8. Stratégie de migration

Toutes les évolutions de schéma passent exclusivement par les **migrations Entity Framework Core** (`dotnet ef migrations add`). Aucune modification manuelle de schéma en production. Chaque migration est :

- testée en environnement de staging avant application en production ;
- réversible (migration `Down()` systématiquement écrite et testée) ;
- accompagnée d'une sauvegarde automatique déclenchée avant application (Volume 9, Chapitre « Sauvegardes »).

### 5.11 Domaine Intégration étatique — `student_mutation_certificates` + colonnes réglementaires

Spécification fonctionnelle : Volume 1 §23. Une seule table nouvelle ; l'essentiel du module tient en **colonnes ajoutées** à des tables existantes.

#### Colonnes ajoutées

**`students`**

| Colonne | Type | Contraintes |
|---|---|---|
| `IenNumber` | `varchar(24)` | NULL — Identifiant National de l'Élève |
| `IsIenProvisional` | `boolean` | NOT NULL, DEFAULT `false` |

`CREATE UNIQUE INDEX UX_students_school_ien ON students ("SchoolId", "IenNumber") WHERE "IenNumber" IS NOT NULL`

> **Pourquoi un index PARTIEL, et pourquoi borné à l'école.** Partiel : l'immense majorité des élèves n'a pas encore d'IEN, et un index unique ordinaire sur une colonne massivement nulle est à la fois inutile et coûteux. Borné à l'école, alors qu'un IEN est *national* : nous ne pouvons pas vérifier l'unicité nationale sans le SIMEN, et un index global échouerait légitimement le jour où deux écoles de la plateforme scolariseraient successivement le même élève — ce qui est le comportement **normal** d'un identifiant qui suit l'élève.
>
> `varchar(24)` alors que notre format provisoire en fait 15 : le format national réel n'est pas connu, la marge évite une migration de colonne le jour où un IEN officiel plus long arrivera.

**`teachers`** — pour le rapport STATEDUC (§23.3)

| Colonne | Type | Contraintes |
|---|---|---|
| `Gender` | `varchar(1)` | NULL — « M »/« F » |
| `AcademicQualification` | `varchar(20)` | NOT NULL, DEFAULT `NonRenseigne` |
| `ProfessionalQualification` | `varchar(20)` | NOT NULL, DEFAULT `NonRenseigne` |
| `CivilServiceStatus` | `varchar(20)` | NOT NULL, DEFAULT `NonRenseigne` |
| `CivilServiceMatricule` | `varchar(30)` | NULL — personnels de l'État uniquement |
| `FirstAppointmentDate` | `date` | NULL — ancienneté dans le métier, pas dans l'établissement |

> `Gender` est **nullable** ici alors qu'il est obligatoire sur `students` : des milliers de fiches enseignant existent déjà sans ce champ, et le rendre obligatoire empêcherait de les rouvrir pour les modifier. Les défauts sont **tous** `NonRenseigne` et jamais `Aucun` : défausser les fiches existantes en « sans diplôme » ferait apparaître, dès le premier rapport, un établissement à 0 % d'enseignants qualifiés.

**`schools`**

| Colonne | Type | Contraintes |
|---|---|---|
| `NationalSchoolCode` | `varchar(30)` | NULL — code SIMEN |
| `MinistryAuthorizationNumber` | `varchar(80)` | NULL — arrêté d'ouverture |
| `SchoolDistrictCode` | `varchar(30)` | NULL — circonscription (carte scolaire) |
| `GpsLatitude` | `numeric(9,6)` | NULL |
| `GpsLongitude` | `numeric(9,6)` | NULL |

`CREATE UNIQUE INDEX UX_schools_national_code ON schools ("NationalSchoolCode") WHERE "NationalSchoolCode" IS NOT NULL` — unique sur **toute la plateforme** (à la différence de l'IEN) : deux écoles ne peuvent pas déclarer le même code au ministère.

> Six décimales ≈ 11 cm au sol. **Deux colonnes numériques, pas une chaîne** « lat,lon » : une coordonnée en texte ne peut être ni validée à la saisie, ni bornée, ni utilisée dans une requête géographique. La chaîne d'affichage `GpsCoordinates` est **calculée en mémoire et non mappée** (`builder.Ignore`) — la stocker permettrait qu'elle contredise un jour le couple qui fait foi.

**`exam_dossiers`**

| Colonne | Type | Contraintes |
|---|---|---|
| `ExamCenterCode` | `varchar(30)` | NULL — code officiel du centre, distinct du nom |
| `TableNumber` | `varchar(20)` | NULL — place physique en salle |
| `CivilRegistryDocumentStatus` | `varchar(20)` | NOT NULL, DEFAULT `NonFourni` |

> `CivilRegistryDocumentStatus` **complète** `BirthCertificatePresent` et `CivilStatusConforming` sans les remplacer : les deux anciens champs restent écrits par les Handlers existants et lus par `GetExamDossierAuditQuery`. Les réécrire aurait cassé l'audit de dossier. Ce que le couple booléen ne savait pas exprimer — et qui est le cas le plus fréquent au Sénégal — est l'état `EnRegularisation`.

#### `student_mutation_certificates`

Table **tenant**, snake_case, protégée par la double barrière §2.2 (Global Query Filter EF Core + policy RLS posée par la migration `AddStateIntegrationModule`).

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `SchoolId` | `uuid` | FK `schools.Id`, NOT NULL, RESTRICT |
| `StudentId` | `uuid` | FK `students.Id`, RESTRICT |
| `SchoolYearId` | `uuid` | FK `school_years.Id`, RESTRICT |
| `CertificateNumber` | `varchar(30)` | NOT NULL — `MUT-2026-0007` |
| `VerificationCode` | `varchar(32)` | NOT NULL — 32 hex d'aléa cryptographique |
| `DestinationSchoolName` | `varchar(150)` | NULL — texte libre (école hors plateforme) |
| `DestinationCity` | `varchar(100)` | NULL |
| `Reason` | `varchar(30)` | NOT NULL, DEFAULT `Autre` |
| `ReasonDetails` | `varchar(300)` | NULL — obligatoire côté validateur si `Reason = Autre` |
| `ClassroomNameSnapshot` | `varchar(100)` | NOT NULL — classe quittée, **figée** |
| `IssuedOn` | `date` | NOT NULL |
| `WasFinanciallyClear` | `boolean` | NOT NULL — instantané, jamais recalculé |
| `RevokedAt` | `timestamptz` | NULL |
| `RevocationReason` | `varchar(300)` | NULL |
| `xmin` | `xid` | Verrou optimiste (§3) |

**Index :**

- `UX_student_mutation_certificates_school_number` sur `("SchoolId", "CertificateNumber")` — unique **par école**, comme le matricule et le numéro de reçu : deux établissements peuvent légitimement émettre chacun leur `MUT-2026-0001`.
- `UX_student_mutation_certificates_verification` sur `("VerificationCode")` — unique **globalement et sans `SchoolId`**.

> **Pourquoi le code de vérification n'est pas borné au tenant.** Le point de vérification publique reçoit un code nu, sans jeton et sans école : c'est un tiers extérieur à la plateforme qui scanne le QR. Un index borné à l'école serait inutilisable par ce chemin, et deux écoles pourraient tirer le même code.
>
> **Pourquoi le code est de l'aléa et non l'`Id`.** Exposer un identifiant séquentiel ou dérivé du numéro de certificat rendrait la table énumérable : un tiers sonderait le point de vérification jusqu'à découvrir quels élèves ont quitté l'établissement. Le point de vérification répond « valide / révoqué / inconnu », **jamais** par les données de l'élève.

**Règles de la table :**

- **Append-only de fait.** Un certificat délivré n'est jamais modifié ; une erreur se corrige par révocation (`RevokedAt`) puis nouvelle délivrance. Réécrire une pièce déjà remise produirait deux documents contradictoires portant le même numéro, dont la version papier ferait foi contre l'école.
- `CertificateNumber` est généré **dans la transaction** de délivrance (règle #3), par le compteur `matricule_sequences` (`Kind = MutationCertificate`) — même garantie d'unicité et d'absence de trou que les matricules.
- `RESTRICT` sur les trois FK : un certificat survit à l'archivage de l'élève, puisqu'il a été remis à un tiers et doit rester vérifiable.

#### Migration

`AddStateIntegrationModule` doit, en plus des colonnes et de la table :

1. ajouter `student_mutation_certificates` à `TenantTables` et poser sa **policy RLS** — sans quoi la table n'est protégée que par le filtre EF Core (règle #2, `RlsCoverageTests` échoue) ;
2. accorder `SELECT, INSERT, UPDATE` au rôle `sama_ecole_app` (pas de `DELETE` — règle #6) ;
3. créer les deux index uniques **partiels** ci-dessus, que le scaffolding EF n'écrit pas seul.

**Fin du Volume 3.**
