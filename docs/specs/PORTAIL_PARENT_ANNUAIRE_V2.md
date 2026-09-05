# SAMA ECOLE

# SPÉCIFICATION — Portail Parent & Annuaire V2 (PPA-V2)

**Version :** 1.0
**Statut :** Proposition — spécification technique, aucune implémentation livrée par ce document.
**Changements de cette version :** Première version. Prolonge l'Annuaire Public V1 existant
(`SamaEcole.Application/PublicDirectory`, migration `AddPublicSchoolDirectory`) sans le remplacer, et
introduit un Portail Parent entièrement nouveau — anticipé mais non implémenté à ce jour
(Volume_7_Security.md §17 liste « Parent d'élève » comme rôle de roadmap).

**Portée** : deux volets indépendants, livrables séparément.
1. Annuaire Public V2 — fiche d'établissement enrichie (cycles Maternelle à Secondaire), modération
   des avis, workflow d'inscription directe.
2. Portail Parent Privé — nouveau rôle, modèle de données, isolation RLS, accès aux bulletins A5 et
   relevés d'assiduité, flux d'authentification dédié.

---

## Table des matières

1. [Contexte et état actuel (V1)](#1-contexte-et-état-actuel-v1)
2. [Annuaire Public V2](#2-annuaire-public-v2)
   - 2.1 [Fiche d'établissement enrichie](#21-fiche-détablissement-enrichie)
   - 2.2 [Modération des avis](#22-modération-des-avis)
   - 2.3 [Workflow d'inscription directe](#23-workflow-dinscription-directe)
3. [Portail Parent Privé](#3-portail-parent-privé)
   - 3.1 [Rôle Parent et authentification](#31-rôle-parent-et-authentification)
   - 3.2 [Modèle de données](#32-modèle-de-données)
   - 3.3 [Isolation multi-tenant et RLS](#33-isolation-multi-tenant-et-rls)
   - 3.4 [Accès aux bulletins A5 et relevés d'assiduité](#34-accès-aux-bulletins-a5-et-relevés-dassiduité)
   - 3.5 [Endpoints API](#35-endpoints-api)
4. [Plan de migration et compatibilité V1](#4-plan-de-migration-et-compatibilité-v1)
5. [Risques et hors-périmètre](#5-risques-et-hors-périmètre)

---

## 1. Contexte et état actuel (V1)

L'annuaire public (`/annuaire`, ticket implicite JGK non numéroté à ce jour) existe déjà en lecture
seule :

| Élément | Fichier | Rôle |
|---|---|---|
| `PublicSchoolDto` / `PaginatedPublicSchools` | `SamaEcole.Application/PublicDirectory/PublicSchoolDto.cs` | Contrat servi à un visiteur anonyme. |
| `GetPublicSchoolsQuery` | `.../Queries/GetPublicSchools/` | `GET /api/v1/public/schools` — pagination (plafond **48**), filtres Search/City/Region/Cycle. |
| `GetPublicSchoolDetailQuery` | `.../Queries/GetPublicSchoolDetail/` | `GET /api/v1/public/schools/{id}` — 404 indifférencié si l'école n'existe pas OU n'a pas consenti. |
| `PublicSchoolListing` | `SamaEcole.Domain/Entities/PublicSchoolListing.cs` | Entité **sans clé**, `ToView("public_school_directory")`. |
| Vue `public_school_directory` | Migration `20260802035529_AddPublicSchoolDirectory` | Source de vérité unique de ce qui est exposé. |

Extrait de la vue actuelle (inchangée par ce document, seulement **étendue** en §2.1) :

```sql
CREATE OR REPLACE VIEW public_school_directory AS
SELECT
    s."Id", s."Name", s."City", s."Region", s."PublicDescription", s."LogoUrl",
    s."Address", s."Phone", s."Email",
    COALESCE(
        (SELECT array_agg(DISTINCT c."Cycle"::text ORDER BY c."Cycle"::text)
         FROM classrooms c WHERE c."SchoolId" = s."Id" AND c."IsDeleted" = FALSE),
        ARRAY[]::text[]
    ) AS "Cycles"
FROM schools s
WHERE s."IsPubliclyListed" = TRUE AND s."IsDeleted" = FALSE AND s."Status" = 'Active';
```

**Principe de sécurité fondateur, hérité tel quel par tout ce document** : `schools` est la racine du
tenant (pas de `SchoolId` propre), donc aucune RLS classique n'y est possible sans casser la console
Super Admin et l'authentification. La vue **ajoute** une surface publique étroite, elle ne retire aucun
droit. Elle tourne avec les privilèges du propriétaire (`security_invoker` par défaut = false), un
contournement RLS documenté et borné à des colonnes non sensibles. Chaque extension proposée ci-dessous
suit la **même discipline** : jamais affaiblir une policy existante, toujours ajouter une vue, une
fonction `SECURITY DEFINER`, ou une policy dédiée et minimale.

`CycleType` (`SamaEcole.Domain/Enums/CommonEnums.cs`) couvre déjà tout le spectre demandé :

```csharp
public enum CycleType { Primaire, College, Lycee, Maternelle }
```

`CycleLabels.ToLabels()` les restitue déjà dans l'ordre pédagogique Maternelle → Primaire → Collège →
Lycée. **Aucun nouveau cycle n'est nécessaire** : la fiche V2 (§2.1) enrichit la PRÉSENTATION par cycle,
pas la liste des cycles eux-mêmes.

Recherche exhaustive confirmée (aucun résultat) : **aucun système d'avis/notation, aucun workflow
d'inscription directe élève→école n'existe aujourd'hui.** `SubmitRegistrationRequestCommand` existant
sert à faire candidater un **nouvel établissement** (un Directeur qui inscrit son ÉCOLE sur la
plateforme) — sans rapport avec un parent qui inscrit son ENFANT dans une école déjà membre. Les deux
fonctionnalités de ce document sont donc entièrement nouvelles.

---

## 2. Annuaire Public V2

### 2.1 Fiche d'établissement enrichie

**Aujourd'hui**, `Cycles` n'est qu'une liste de libellés (`["Maternelle", "Primaire"]`). **En V2**, la
fiche détaille, PAR cycle, les niveaux réellement ouverts — toujours **dérivé des classrooms actifs**,
jamais saisi à la main (même garde-fou qu'aujourd'hui : un Directeur ne peut pas prétendre offrir un
niveau qu'il n'ouvre pas réellement).

```csharp
public record PublicSchoolDetailDto(
    Guid Id, string Name, string? City, string? Region, string? Description,
    string? LogoUrl, string? Address, string? Phone, string? Email,
    IReadOnlyList<PublicCycleDetailDto> Cycles,
    // §2.2 — note moyenne et nombre d'avis APPROUVÉS uniquement (jamais les avis en attente/rejetés).
    decimal? AverageRating,
    int ReviewCount);

public record PublicCycleDetailDto(
    string Cycle,              // libellé via CycleLabels (« Maternelle », « Collège »…)
    IReadOnlyList<string> Levels); // niveaux ouverts dans ce cycle (ex. « 6ème », « 5ème »…), triés
```

Extension de la vue (rétrocompatible — `Cycles` reste servi tel quel pour `GetPublicSchoolsQuery`, qui
n'a besoin que de la liste plate pour son filtre ; la version détaillée par niveau n'est calculée que
pour la fiche unitaire, `GetPublicSchoolDetailQuery`, pour ne pas alourdir la pagination de liste) :

```sql
-- Nouvelle vue dédiée à la fiche détaillée, distincte de public_school_directory (qui reste inchangée
-- pour la liste paginée). Jointure sur classrooms pour le détail par niveau.
CREATE VIEW public_school_directory_detail
WITH (security_invoker = false) AS
SELECT
    s."Id",
    c."Cycle"::text AS "Cycle",
    array_agg(DISTINCT c."Level" ORDER BY c."Level") AS "Levels"
FROM schools s
JOIN classrooms c ON c."SchoolId" = s."Id" AND c."IsDeleted" = FALSE
WHERE s."IsPubliclyListed" = TRUE AND s."IsDeleted" = FALSE AND s."Status" = 'Active'
GROUP BY s."Id", c."Cycle";
```

> **Point d'attention pour l'implémentation** : vérifier le nom réel de la colonne « niveau » sur
> `Classroom` (`Level`, `Grade`… à confirmer dans `SamaEcole.Domain/Entities/Classroom.cs`) avant
> d'écrire la migration — ce document décrit l'INTENTION, pas une migration prête à appliquer telle
> quelle.

### 2.2 Modération des avis

**Aucun compte n'est requis pour laisser un avis** (comme la plupart des annuaires grand public) —
seule une **modération a posteriori par l'établissement concerné** protège la fiche, avec le même
honeypot anti-spam que `SubmitRegistrationRequestCommand`.

#### Modèle de données

```csharp
public class SchoolReview : ITenantEntity   // Id, SchoolId : mêmes conventions que toute entité tenant
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }
    public required string AuthorDisplayName { get; set; }   // saisie libre, jamais un compte lié
    public int Rating { get; set; }                           // 1 à 5, validé par RuleFor().InclusiveBetween(1, 5)
    public required string Comment { get; set; }               // NoHtml(), même règle que DirectorFullName etc.
    public ReviewStatus Status { get; set; }                   // PendingModeration | Approved | Rejected
    public Guid? ModeratedByUserId { get; set; }
    public DateTimeOffset? ModeratedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string? IpAddress { get; set; }                     // même usage que RegistrationRequest : rate limiting/audit
}

public enum ReviewStatus { PendingModeration, Approved, Rejected }
```

#### RLS — écriture publique, lecture/modération réservées à l'école

`school_reviews` PORTE un `SchoolId` réel (contrairement à `registration_requests`, qui n'en a aucun
puisque l'école n'existe pas encore) : la RLS générique (`SchoolId = current_setting('app.current_school_id')`)
bloquerait donc un auteur anonyme, dont la session n'a JAMAIS de `app.current_school_id`. Plutôt qu'une
fonction `SECURITY DEFINER` (plus de surface à auditer), **deux policies distinctes sur la même table**
suffisent — un principe déjà validé par `EnableRowLevelSecurity` (une policy par commande, pas une seule
policy fourre-tout) :

```sql
ALTER TABLE school_reviews ENABLE ROW LEVEL SECURITY;

-- Lecture/modification : réservée à l'école concernée (Directeur/Secrétariat de CETTE école), comme
-- toute table tenant.
CREATE POLICY school_reviews_tenant_isolation ON school_reviews
    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);

-- Écriture publique : SEULE une soumission ANONYME (Status forcé à PendingModeration côté application,
-- jamais fourni par le client) vers une école PUBLIQUEMENT LISTÉE est acceptée. Cette policy s'ajoute à
-- la précédente (OR logique entre policies permissives de même commande) : un utilisateur authentifié
-- d'une AUTRE école ne gagne toujours aucun accès aux avis d'une école tierce.
CREATE POLICY school_reviews_public_submission ON school_reviews
    FOR INSERT
    WITH CHECK (
        "SchoolId" IN (SELECT "Id" FROM schools WHERE "IsPubliclyListed" = TRUE AND "IsDeleted" = FALSE AND "Status" = 'Active')
        AND "Status" = 'PendingModeration'
    );
```

Le rôle applicatif (`sama_ecole_app`) exécute déjà toutes les requêtes avec ce même rôle, RLS activée
— nul besoin d'un rôle Postgres distinct : c'est la **combinaison des deux policies** qui porte toute la
sécurité, exactement comme `refresh_tokens` (`FixRefreshTokenRlsPolicy`) combine une policy technique
avec le reste du modèle.

#### Endpoints

| Méthode | Route | Authz | Description |
|---|---|---|---|
| `POST` | `/api/v1/public/schools/{schoolId}/reviews` | Anonyme, rate-limited + honeypot | Soumet un avis (`Status` forcé à `PendingModeration`). |
| `GET` | `/api/v1/schools/reviews?status=` | Directeur, Secretariat | Liste les avis de SA propre école, tous statuts. |
| `PATCH` | `/api/v1/schools/reviews/{id}/moderate` | Directeur, Secretariat | `{ status: Approved \| Rejected, rejectionReason? }` — motif obligatoire si `Rejected` (même convention que `ChangeUserStatusCommand`). |

`AverageRating`/`ReviewCount` (§2.1) ne comptent QUE les avis `Approved` — un avis en attente ou rejeté
reste invisible du public, y compris dans l'agrégat.

### 2.3 Workflow d'inscription directe

Un parent qui a trouvé une école dans l'annuaire doit pouvoir signaler son intérêt SANS créer de compte
— la conversion en inscription réelle (`Enrollment`) reste un acte du Secrétariat, jamais automatique
(cohérent avec AGENTS.md règle #3 : un matricule n'est généré que par un acte applicatif maîtrisé).

```csharp
public class DirectAdmissionRequest : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }               // école CIBLE, déjà existante (≠ RegistrationRequest)
    public required string ChildFullName { get; set; }
    public DateOnly? ChildBirthDate { get; set; }
    public string? DesiredCycle { get; set; }         // CycleType en texte, validé contre les cycles RÉELLEMENT ouverts (§2.1)
    public required string ParentFullName { get; set; }
    public required string ParentPhone { get; set; }  // MustBeValidSenegalPhone, même règle que l'inscription établissement
    public string? ParentEmail { get; set; }
    public string? Message { get; set; }
    public DirectAdmissionStatus Status { get; set; } // Pending | Contacted | Accepted | Declined
    public DateTimeOffset SubmittedAt { get; set; }
    public string? IpAddress { get; set; }
}
```

Même schéma RLS à deux policies que §2.2 (policy tenant + policy d'insertion publique conditionnée à
`IsPubliclyListed`), transposé telle quelle sur `direct_admission_requests`.

| Méthode | Route | Authz | Description |
|---|---|---|---|
| `POST` | `/api/v1/public/schools/{schoolId}/admission-requests` | Anonyme, rate-limited + honeypot | Dépose la demande. |
| `GET` | `/api/v1/admission-requests?status=` | Secretariat, Directeur | Liste les demandes reçues par SON école. |
| `POST` | `/api/v1/admission-requests/{id}/convert` | Secretariat, Directeur | Ouvre le formulaire d'inscription standard PRÉ-REMPLI (`ChildFullName`, etc.) — ne crée PAS l'élève directement : le Secrétariat garde la main sur le matricule et les pièces justificatives, comme toute inscription actuelle. |
| `PATCH` | `/api/v1/admission-requests/{id}/status` | Secretariat, Directeur | `Contacted` / `Declined`, motif obligatoire pour `Declined`. |

---

## 3. Portail Parent Privé

### 3.1 Rôle Parent et authentification

```csharp
public enum Role { SuperAdmin, Directeur, Secretariat, Finance, Enseignant, Surveillant, Parent }
```

**Choix structurant : un compte Parent est une ligne `users` ordinaire (`Role = Parent`), PAS une table
séparée.** Raisons :

- Réutilise TEL QUEL tout ce qui existe déjà pour l'authentification — JWT (`AuthController`), refresh
  tokens en cookie HttpOnly, politique de mot de passe (12+ caractères, Volume_7 §2), verrouillage après
  5 échecs, réinitialisation de mot de passe, journalisation `Auth/Login`. **Aucun nouvel endpoint de
  connexion n'est nécessaire** : `/api/v1/auth/login` fonctionne sans modification, le `role` émis dans
  le JWT vaut simplement `Parent`.
- Un parent peut avoir des enfants dans PLUSIEURS écoles différentes — comme un compte Directeur
  multi-établissements (`AddMultiSchoolMembership`, `GET /my-schools`, `POST /switch-school`). `SchoolId`
  reste donc **`NULL`** sur le compte lui-même (même position que `SuperAdmin`), le JWT ne porte
  `schoolId` QUE pour l'école de l'enfant actuellement sélectionné (§3.3).

**Provisionnement — volontairement PAS de self-service.** Contrairement à l'inscription d'établissement
(`SubmitRegistrationRequestCommand`), un compte Parent n'est créé QUE par une invitation du Secrétariat
ou du Directeur depuis la fiche d'un élève existant :

```
POST /api/v1/students/{studentId}/guardian-invitations
    { parentFullName, parentEmail, parentPhone, relationshipType }
```

Génère un jeton d'invitation à usage unique et courte durée de vie (même mécanique que
`PasswordResetToken` : hash stocké, jamais le jeton en clair, expiration courte), envoyé par e-mail/SMS.
Le parent choisit son mot de passe en suivant le lien — création du compte `users` (ou rattachement d'un
compte Parent déjà existant pour un AUTRE enfant : rechercher par e-mail avant de créer, pour ne jamais
dupliquer un parent qui a déjà un compte via une première école) et de la ligne `guardian_students`
(§3.2) en une seule transaction, comme `ProvisionSchoolDirector`.

**Pourquoi pas de self-service** : contrairement à un Directeur qui crée SA PROPRE école (aucune donnée
tierce en jeu tant que la demande n'est pas approuvée), un self-service Parent permettrait à n'importe
qui de PRÉTENDRE être le parent d'un élève réel et de réclamer l'accès à son dossier — un risque de vie
privée que l'inscription self-service d'établissement n'a pas. Le lien parent↔élève ne peut être posé
que par un adulte déjà en position de confiance (le staff de l'école qui a inscrit l'enfant).

### 3.2 Modèle de données

`docs/Volume_3_DDS.md` §4.4 liste déjà `Guardians`/`StudentGuardians` comme catalogue **aspirationnel**
— ce document en fixe enfin le schéma réel :

```csharp
public class GuardianStudent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid SchoolId { get; set; }         // école DE L'ÉLÈVE — dénormalisé, indispensable à la RLS (§3.3)
    public Guid ParentUserId { get; set; }     // FK -> users (Role = Parent)
    public Guid StudentId { get; set; }        // FK -> students
    public GuardianRelationship Relationship { get; set; } // Pere | Mere | Tuteur | Autre
    public bool IsPrimaryContact { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

**`Student.GuardianName` / `GuardianPhone` / `GuardianEmail` (texte libre) sont CONSERVÉS tels quels** —
ils alimentent des écrans et exports existants (listes de classe, `GetStudentsExportPdfQuery`…) qui
n'ont pas besoin d'un compte connecté pour fonctionner, et un élève peut très bien avoir un tuteur
renseigné en texte SANS que ce tuteur ait (ou veuille) un compte Portail Parent. `GuardianStudent` est un
lien ADDITIONNEL, pas un remplacement — les deux peuvent diverger (ex. le champ texte mentionne un
oncle, mais c'est la mère, avec un compte, qui suit les bulletins).

Contrainte d'unicité : `UNIQUE (ParentUserId, StudentId)` — un même lien ne peut être créé deux fois par
deux invitations concurrentes.

### 3.3 Isolation multi-tenant et RLS

**Prérequis d'infrastructure — nouveau claim JWT et nouvelle variable de session.** Le mécanisme actuel
(`TenantConnectionInterceptor` pose `app.current_school_id` depuis le seul claim `schoolId`) suffit pour
borner un Parent à L'ÉCOLE de l'enfant actuellement sélectionné, mais PAS à distinguer ses propres
enfants des autres élèves de la MÊME école. Deux ajouts :

1. **Claim JWT `sub`** (déjà émis, c'est l'userId) réutilisé comme variable de session
   `app.current_user_id`, pour toute policy qui doit vérifier « suis-je bien CE parent » —
   `TenantConnectionInterceptor.BuildApplyTenantCommand` pose une seconde variable au même moment que
   `app.current_school_id` :

    ```csharp
    command.CommandText = """
        SELECT set_config('app.current_school_id', @schoolId, false),
               set_config('app.current_user_id', @userId, false)
        """;
    ```

2. **`GuardianStudent` porte sa PROPRE policy RLS**, restreinte au parent connecté :

    ```sql
    CREATE POLICY guardian_students_own_links ON guardian_students
        USING ("ParentUserId" = NULLIF(current_setting('app.current_user_id', true), '')::uuid
               OR "SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    ```

   Le `OR` côté lecture couvre DEUX besoins légitimes : le parent qui consulte SES propres liens (quelle
   que soit l'école — d'où l'appui sur `app.current_user_id`, PAS sur `app.current_school_id`), et le
   Secrétariat/Directeur qui gère les invitations DE SON école (RLS ordinaire par `SchoolId`). Le `WITH
   CHECK` en écriture reste volontairement plus strict : seule l'école concernée peut créer/modifier un
   lien, jamais le parent lui-même (il ne peut pas s'auto-attribuer un enfant).

**Ce que la RLS NE couvre PAS — et pourquoi c'est un choix, pas un oubli.** Une fois le JWT du parent
scopé sur l'école de l'enfant sélectionné, la RLS existante sur `students`/`grades`/`student_attendances`
(par `SchoolId`) s'applique DÉJÀ correctement — mais elle ne protège que la frontière ÉCOLE, pas la
frontière ÉLÈVE : rien n'empêche structurellement un parent de demander le bulletin d'un AUTRE élève de
la MÊME école en devinant son `StudentId`. Réécrire la RLS de chaque table pédagogique/financière pour y
ajouter une clause « OU je suis un parent lié à cet élève précis » démultiplierait la surface à auditer
sur des dizaines de tables déjà couvertes par un modèle éprouvé (`RlsCoverageTests`) — et une policy mal
écrite y serait bien plus dangereuse qu'un bug applicatif isolé.

**Décision : la vérification élève-précis se fait à l'application**, dans chaque Handler du Portail
Parent, AVANT toute lecture pédagogique — un garde-fou explicite, testable unitairement, réutilisable :

```csharp
public class RequireGuardianLinkGuard(IApplicationDbContext dbContext, ICurrentUser currentUser)
{
    public async Task EnsureLinkedAsync(Guid studentId, CancellationToken ct)
    {
        var linked = await dbContext.GuardianStudents
            .AnyAsync(gs => gs.ParentUserId == currentUser.UserId && gs.StudentId == studentId, ct);

        if (!linked) throw new ForbiddenException("Cet élève n'est pas rattaché à votre compte.");
    }
}
```

C'est exactement le même raisonnement que `ReportCardDataService` aujourd'hui (« un élève d'une autre
école y est structurellement introuvable — 404, jamais un bulletin fuité », d'après son commentaire de
tête) : la RLS gère la frontière ÉCOLE de façon structurelle, un garde applicatif explicite gère la
frontière ÉLÈVE. **Toute revue de code de l'implémentation réelle doit vérifier que CE garde est appelé
au tout début de CHAQUE Handler du Portail Parent**, sans exception — c'est le point de défaillance
unique de tout ce volet.

### 3.4 Accès aux bulletins A5 et relevés d'assiduité

**Réutilisation, pas duplication** — les deux services existants portent déjà toute la logique métier :

- `ReportCardDataService.BuildAsync(studentId, termId, …)` (`SamaEcole.Application/ReportCards/Queries/GetReportCardPdf/`)
  construit le `ReportCardDto` complet (moyennes, rangs, assiduité du trimestre, mentions). Le PDF A5
  est généré par `ReportCardPdfGenerator`/`ReportCardDocument` (`SamaEcole.Infrastructure/Documents/`,
  `page.Size(PageSizes.A5)`).
- `AttendanceReportAggregator.ComputeAsync(startDate, endDate, classId?, ct)` calcule déjà le taux de
  présence — `(Present + Late) / TotalCalls`, `null` (jamais 0) si aucune fiche d'appel n'existe sur la
  période.

**Extension nécessaire, additive** : `AttendanceReportAggregator` ne filtre aujourd'hui que par classe
(`classId?`). Ajouter un paramètre optionnel `studentId?` (surcharge non cassante — un appel existant
sans ce paramètre garde exactement son comportement) pour permettre au Portail Parent de n'agréger
qu'UN SEUL élève sans reconstruire un calcul parallèle :

```csharp
public Task<AttendanceAggregate> ComputeAsync(
    DateOnly startDate, DateOnly endDate, Guid? classId, Guid? studentId, CancellationToken ct)
```

Nouvelles Queries, chacune commençant par `RequireGuardianLinkGuard.EnsureLinkedAsync` (§3.3) :

```csharp
public record GetParentReportCardPdfQuery(Guid StudentId, Guid TermId) : IRequest<ReportCardPdfResult>;
public record GetParentAttendanceSummaryQuery(Guid StudentId, DateOnly From, DateOnly To) : IRequest<AttendanceAggregate>;
```

### 3.5 Endpoints API

| Méthode | Route | Authz | Description |
|---|---|---|---|
| `POST` | `/api/v1/auth/login` | Anonyme | **Inchangé** — un compte Parent s'y connecte comme tout autre rôle. |
| `GET` | `/api/v1/parent-portal/my-children` | Parent | Liste TOUS les enfants liés, toutes écoles confondues — nécessite un contournement RLS analogue à `get_global_audit_logs` (§ ci-dessous). |
| `POST` | `/api/v1/parent-portal/select-child` | Parent | `{ studentId }` → émet un jeton scopé sur l'école de CET enfant (`schoolId` + nouveau claim `activeStudentId`, confort d'affichage UNIQUEMENT — jamais une preuve d'autorisation, revérifiée à chaque endpoint via §3.3). Même mécanique que `switch-school`. |
| `GET` | `/api/v1/parent-portal/students/{studentId}/report-cards/{termId}/pdf` | Parent (lien vérifié) | Bulletin A5, réutilise `ReportCardDataService`. |
| `GET` | `/api/v1/parent-portal/students/{studentId}/attendance?from=&to=` | Parent (lien vérifié) | Relevé d'assiduité, `AttendanceReportAggregator` étendu. |

`GET /my-children` traverse plusieurs écoles à la fois — la RLS normale (une seule `app.current_school_id`
par session) ne peut structurellement pas répondre à cette question. Même famille de solution que
`get_global_audit_logs` (migration `AddPlatformAdminViews`) : une fonction `SECURITY DEFINER`, minimale
et bornée au seul parent appelant (jamais un paramètre client, toujours `app.current_user_id`) :

```sql
CREATE FUNCTION public.get_guardian_children(parent_user_id_from_session_only boolean DEFAULT true)
RETURNS TABLE ("StudentId" uuid, "StudentFullName" text, "SchoolId" uuid, "SchoolName" text, "Relationship" text)
LANGUAGE sql SECURITY DEFINER SET search_path = public
AS $$
    SELECT st."Id", st."FullName", s."Id", s."Name", gs."Relationship"::text
    FROM guardian_students gs
    JOIN students st ON st."Id" = gs."StudentId"
    JOIN schools s ON s."Id" = gs."SchoolId"
    WHERE gs."ParentUserId" = NULLIF(current_setting('app.current_user_id', true), '')::uuid
      AND NOT st."IsDeleted";
$$;
```

Le paramètre `parent_user_id_from_session_only` est un garde-fou de LECTURE DU CODE plutôt qu'un vrai
paramètre fonctionnel (toujours `true`, jamais transmis par le client) : il documente, pour quiconque lit
la signature sans le corps, que cette fonction ne prend JAMAIS un identifiant en entrée — elle ne peut
lire QUE la session courante, contrairement à une fonction qui accepterait un `parent_user_id uuid` en
paramètre (ce qui permettrait à un appelant malveillant de le forger). Un futur relecteur pressé qui ne
lit que la signature d'une fonction `SECURITY DEFINER` doit immédiatement voir qu'aucun identifiant
n'est acceptable en entrée.

---

## 4. Plan de migration et compatibilité V1

Ordre recommandé, chaque étape déployable indépendamment (aucune ne casse la précédente) :

1. `AddPublicSchoolReviews` — table `school_reviews`, RLS à deux policies (§2.2), endpoints associés.
2. `AddDirectAdmissionRequests` — table `direct_admission_requests`, même schéma RLS (§2.3).
3. `AddPublicSchoolDirectoryDetail` — vue `public_school_directory_detail` (§2.1), extension additive de
   `PublicSchoolDto` → `PublicSchoolDetailDto` (le endpoint de LISTE, `GetPublicSchoolsQuery`, garde son
   contrat actuel inchangé ; seul `GetPublicSchoolDetailQuery` évolue).
4. `AddParentRole` — ajout de `Role.Parent` à l'enum (valeur ajoutée en fin de liste, comme `Maternelle`
   l'a été sur `CycleType`, pour ne renuméroter aucune valeur existante si l'enum est persisté en entier
   quelque part — à vérifier : ce projet persiste `Role` en `text`, donc l'ordre n'a de toute façon aucun
   impact, mais l'habitude reste la bonne).
5. `AddGuardianStudentsLink` — table `guardian_students`, nouvelle policy RLS, extension de
   `TenantConnectionInterceptor` pour poser `app.current_user_id`.
6. `AddGuardianInvitations` — table des jetons d'invitation (même forme que `password_reset_tokens`),
   endpoint `POST /students/{id}/guardian-invitations`.
7. `AddGuardianChildrenFunction` — fonction `get_guardian_children` (§3.5).
8. Extension non cassante de `AttendanceReportAggregator` (§3.4) + nouvelles Queries Portail Parent.

**Compatibilité V1** : `PublicSchoolDto` (liste) n'est jamais modifié — seul un NOUVEAU type
(`PublicSchoolDetailDto`) apparaît pour la fiche unitaire. Tout consommateur existant de
`GET /api/v1/public/schools` continue de fonctionner sans changement.

---

## 5. Risques et hors-périmètre

**Explicitement hors périmètre de ce document** — à spécifier séparément si retenu :
- Paiement de frais en ligne par le parent.
- Messagerie parent ↔ enseignant/école.
- Notifications push/SMS proactives (nouvelle note, absence du jour).
- Vérification d'authenticité d'un avis au-delà de la modération manuelle (un avis reste un texte libre
  non lié à une preuve d'inscription réelle — limite assumée, commune à la plupart des plateformes
  d'avis).

**Risques identifiés :**

| Risque | Impact | Mitigation proposée |
|---|---|---|
| Garde `RequireGuardianLinkGuard` (§3.3) oublié sur un futur endpoint Portail Parent | Fuite du dossier d'un élève à un parent tiers de la MÊME école | Test de garde-fou dédié (même famille que `RlsCoverageTests`) qui énumère tous les Handlers du namespace `ParentPortal` et échoue si l'un d'eux ne référence pas le garde en première ligne. |
| Modération d'avis biaisée (une école rejette systématiquement les avis négatifs) | Perte de confiance dans l'annuaire | Journaliser CHAQUE modération dans `audit_logs` (module `SchoolReviews`, déjà couvert par le filtre Module de l'écran Sécurité & Logs — voir la consolidation Console Super Admin livrée en parallèle de ce document) : un Super Admin peut repérer un taux de rejet anormal sans nouvel écran dédié. |
| `activeStudentId` du JWT utilisé PAR ERREUR comme preuve d'autorisation dans un futur endpoint | Même classe de risque que le garde oublié | Documenté explicitement en §3.5 : ce claim est un confort d'affichage, jamais une autorisation — à rappeler dans la revue de code de toute PR touchant `ParentPortalController`. |
| Compte Parent dupliqué (même personne, deux comptes, un par école) | Confusion utilisateur, deux mots de passe à retenir | §3.1 : toute invitation DOIT rechercher un compte Parent existant par e-mail avant d'en créer un nouveau. |

---

*Fin du document.*
