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

### 4.4 Domaine Pédagogique

`SchoolYears`, `Terms`, `Levels`, `ClassRooms`, `Subjects`, `Teachers`, `TeacherAssignments`, `Students`, `Guardians`, `StudentGuardians`, `Enrollments`, `EnrollmentDocuments`, `WaitingListEntries`, `StudentTransfers`, `Attendances`, `Grades`, `GradeDetails`, `ReportCards`, `ReportCardDetails`.

### 4.5 Domaine Finance

`FeeCategories`, `SchoolFees`, `FeeChangeHistory`, `Payments`, `PaymentDetails`, `Receipts`, `ExpenseCategories`, `Expenses`, `ExpenseAttachments`, `FinancialReports`.

### 4.6 Domaine Inventaire

`inventory_categories`, `inventory_items`, `stock_movements`, `item_assignments` (spécification détaillée en §5.9).

### 4.7 Domaine Infrastructure applicative

`AuditLogs`, `Notifications`, `NotificationRecipients`, `BackupHistory` (traçabilité des sauvegardes automatiques côté infrastructure, Volume 9), `ApplicationLogs`.

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
| `Email` | `varchar(255)` | UNIQUE (global — un email n'appartient qu'à un compte sur toute la plateforme) |
| `PasswordHash` | `varchar(500)` | NOT NULL (géré par ASP.NET Core Identity) |
| `FullName`, `Phone` | `varchar` | — |
| `RoleId` | `uuid` | FK `Roles.Id` |
| `Status` | `varchar(20)` | CHECK IN (`ACTIVE`,`SUSPENDED`,`BLOCKED`) — Volume 1 §1.1 |
| Index | `IX_Users_SchoolId`, `IX_Users_Email` (unique) | |

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

| Colonne | Type | Contraintes |
|---|---|---|
| `Id` | `uuid` | PK |
| `TrackingReference` | `varchar(20)` | UNIQUE, index — communiquée au demandeur pour suivre sa demande sans authentification |
| `DirectorFullName` | `varchar(200)` | NOT NULL |
| `DirectorEmail` | `varchar(200)` | NOT NULL |
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

Un diagramme entité-association complet (ERD) doit être maintenu à jour dans le dépôt de code (ex. via `dbdiagram.io` ou export EF Core), et non uniquement dans ce document texte.

---

## 8. Stratégie de migration

Toutes les évolutions de schéma passent exclusivement par les **migrations Entity Framework Core** (`dotnet ef migrations add`). Aucune modification manuelle de schéma en production. Chaque migration est :

- testée en environnement de staging avant application en production ;
- réversible (migration `Down()` systématiquement écrite et testée) ;
- accompagnée d'une sauvegarde automatique déclenchée avant application (Volume 9, Chapitre « Sauvegardes »).

**Fin du Volume 3.**
