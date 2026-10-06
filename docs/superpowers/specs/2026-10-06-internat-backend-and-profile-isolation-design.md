# Internat — Modèle Pavillon/Lit et étanchéité IHM par profil — Spécification

**Date :** 06/10/2026
**Statut :** Prête pour relecture. Questions de conception tranchées le 06/10/2026 (§11). Rien n'est implémenté.
**Branches :** spec sur `docs/internat-backend-and-profile-isolation-design` ; implémentation back sur
`feat/internat-backend` (Phase 1) ; implémentation front sur une branche distincte (Phase 2).
**Remplace partiellement :** `docs/superpowers/specs/2026-09-18-module-internat-design.md` (décisions #1, #2, #4 —
voir §2). Le reste de cette spec (facturation de la pension, garde de module, rôles) reste en vigueur.

## 1. Contexte et périmètre

Le module Internat est **déjà livré** (migration `AddInternatBoarding`, `InternatController`, écran `/internat`) sous
un modèle volontairement minimal : une chambre est un `Room` de type `Dortoir`, l'occupation est un **comptage** des
`Enrollment` pointant vers cette chambre, le régime (`BoardingStatus`) est porté par `Enrollment`. Ce modèle ne sait
pas répondre à ce que demandent les internats réels : quel **lit** précis, quel **pavillon** (garçons/filles,
surveillant responsable), fiche **médicale** et personnes habilitées à récupérer l'élève, **registre des
sorties/permissions**, **pointage de nuit**, registre imprimable par pavillon.

Cette spec couvre deux phases :

- **Phase 1 — Backend Internat** : entités, migration (avec reprise des données existantes), endpoints CQRS,
  documents QuestPDF.
- **Phase 2 — Étanchéité IHM par profil** : un établissement dont le profil n'inclut pas l'Internat/le Coran ne voit
  ni leurs menus ni leurs écrans, et un profil `InternatDaara` les met au premier plan.

Hors périmètre : refonte de l'écran `/internat` (lot front séparé, après Phase 1), portail parents, notifications
SMS aux familles lors d'une sortie/retour, facturation de la pension au prorata (décision #7 de la spec du 18/09
maintenue : aucune modification des lignes de pension déjà facturées).

## 2. Décisions

### 2.1 Décisions de la spec du 18/09 révisées

| Ancienne décision | Nouvelle décision | Pourquoi |
|---|---|---|
| #1 Réutiliser `Building`/`Room` | Hiérarchie dédiée `Dormitory` → `DormitoryRoom` → `Bed`. `Building`/`Room` restent réservés aux locaux d'enseignement. | Un pavillon porte un genre, un surveillant, des notes — sans rapport avec un bâtiment de salles de classe. |
| #2 Pas d'entité `Bed` | `Bed` existe. | Affecter à un lit précis, gérer un lit en maintenance, imprimer un registre nominatif. |
| #4 Régime sur `Enrollment` | Régime porté par `BoardingEnrollment` (§3.4), rattaché à l'`Enrollment` (année scolaire). | Le séjour a son propre cycle de vie (dates, fiche médicale, sorties) ; l'`Enrollment` reste un objet financier/scolaire. |

Maintenues : #3 (rôles), #5 (`/infrastructures` ne crée plus de dortoirs — voir §4.3), #6 (pension facturée via
`FeeCategory.IsBoardingFee` + `EnrollmentFeeLine`), #7 (une libération ne retire jamais une ligne déjà facturée).

### 2.2 Décisions nouvelles

| # | Sujet | Décision |
|---|---|---|
| N1 | Capacités | **Dérivées**, jamais saisies deux fois. `DormitoryRoom.Capacity` = nombre de lits actifs (hors lits supprimés) ; `Dormitory.Capacity` = somme de ses chambres. Les DTO exposent bien `Capacity`, mais la colonne n'existe pas. `POST /rooms` accepte `bedCount` pour générer les lits 1..N d'un coup. |
| N2 | `Bed.Status` | La colonne ne stocke que `Available` ou `Maintenance` (contrainte `CHECK`). `Occupied` est **projeté** à la lecture depuis l'existence d'un `BoardingEnrollment` actif sur ce lit. Même principe que la spec du 18/09 : pas de compteur/état stocké qui double une source de vérité. |
| N3 | Un lit ↔ un occupant | Garanti par la **base** : index unique partiel sur `boarding_enrollments(BedId) WHERE IsActive AND BedId IS NOT NULL`. Le comptage applicatif de la spec du 18/09 est remplacé. |
| N4 | Historique des lits | Un changement de lit **met à jour** `BoardingEnrollment.BedId` (même séjour) ; la trace est dans `audit_logs` (`IAuditableRequest`), comme pour `ChangeEnrollmentStatus`. Pas de table d'historique. |
| N5 | Fin de séjour | `DELETE /unassign-bed/{boarderId}` n'efface rien : `IsActive = false`, `EndDate = aujourd'hui`, `BedId = null`. Le rôle applicatif n'a de toute façon plus `DELETE` (migration `RevokeDeleteOnBusinessTables`). |
| N6 | `BoardingLeave.Status` | Exposé mais **non stocké** : calculé à partir de `LeaveDate`, `ExpectedReturnDate`, `ActualReturnDate` et de la date du jour (§6.4). Pas de tâche planifiée pour basculer `Overdue`. |
| N7 | Demi-pensionnaires | `BoardingEnrollment.Regime ∈ {Interne, DemiPensionnaire}`. `BedId` n'est obligatoire que pour `Interne` (un demi-pensionnaire ne dort pas à l'internat). Un `Interne` sans lit = « en attente d'affectation ». |
| N8 | Préfixe d'API | `/api/v1/boarding/...` (Volume 4 §0 : toutes les routes sont préfixées `/api/v1/`). Le brouillon de feuille de route écrivait `/api/boarding/...`. |
| N9 | Garde de module | Le contrôleur est `[RequireModule(SchoolModule.Internat)]`, comme `InternatController`. |

## 3. Modèle de données

Toutes les tables ci-dessous portent `SchoolId`, héritent de `AuditableEntity` + `ITenantEntity` (donc Global Query
Filter `SchoolId` + `IsDeleted` appliqué automatiquement), ont un verrou optimiste `xmin` (AGENTS.md règle #5) et des
FK `OnDelete(Restrict)`. Les identités réutilisables (`Dormitory.Name`, `DormitoryRoom.Name`, `Bed.BedNumber`) suivent
le contrat soft-delete de la PR1 : index unique **partiel** `WHERE "IsDeleted" = false`, garde
`ARCHIVED_ENTITY_EXISTS`/`ACTIVE_ENTITY_CONFLICT` à la création/restauration, restauration par commande typée.
Tous les enums sont persistés en string (`HasConversion<string>()`).

### 3.1 `Dormitory` (table `dormitories`)

| Colonne | Type | Règle |
|---|---|---|
| `Id`, `SchoolId` | uuid | — |
| `Name` | text(100) | Requis. Unique par école (partiel, vivants). Ex. « Pavillon Oustaz Ahmad ». |
| `Gender` | enum `DormitoryGender` | `Garcons`, `Filles` (+ `Mixte`, **réservé à la reprise de données**, voir §4 et §11-Q2). |
| `SupervisorName` | text(150)? | Surveillant responsable, texte libre (V1). |
| `SupervisorPhone` | text? | Validé par `MustBeValidSenegalPhone`. |
| `SupervisorUserId` | uuid? | **Liaison optionnelle** à un compte utilisateur. Le handler vérifie qu'il existe un `UserSchool` `(UserId, SchoolId courant)` et que le rôle est `Surveillant` (sinon 422). Si renseigné, `SupervisorName` est dérivé du compte à la lecture et la saisie libre est ignorée. |
| `Notes` | text(1000)? | — |

`Capacity` : dérivée (N1).

### 3.2 `DormitoryRoom` (table `dormitory_rooms`)

`Id`, `SchoolId`, `DormitoryId` (FK composite `(SchoolId, DormitoryId)`), `Name` (« Chambre 102 », unique par
pavillon, partiel), `Capacity` dérivée (N1). Index `(SchoolId, DormitoryId)`.

### 3.3 `Bed` (table `beds`)

`Id`, `SchoolId`, `DormitoryRoomId` (FK composite), `BedNumber` (int ≥ 1, unique par chambre, partiel), `Status`
(`BedStatus` : `Available`, `Occupied`, `Maintenance` — voir N2 ; `CHECK (Status IN ('Available','Maintenance'))`).

### 3.4 `BoardingEnrollment` (table `boarding_enrollments`) — le « pensionnaire »

| Colonne | Type | Règle |
|---|---|---|
| `Id`, `SchoolId` | uuid | — |
| `StudentId` | uuid | Repris de la feuille de route. FK composite `(SchoolId, StudentId)`. |
| `EnrollmentId` | uuid | **Ajout** : porte l'année scolaire et le lien financier (`TotalDue`, `EnrollmentFeeLine`). FK composite `(SchoolId, EnrollmentId)`. Le handler vérifie `enrollment.StudentId == StudentId`. |
| `Regime` | enum `BoardingRegime` | `Interne`, `DemiPensionnaire` (N7). Distinct du `BoardingStatus` historique, qui contenait aussi `Externe` : un externe n'a simplement pas de `BoardingEnrollment` actif. |
| `BedId` | uuid? | FK composite vers `beds`. Index unique partiel (N3). |
| `StartDate` | date | Défaut : date du jour. |
| `EndDate` | date? | Posée à la fin de séjour (N5). `EndDate ≥ StartDate`. |
| `IsActive` | bool | `true` tant que le séjour court. |
| `MedicalNotes` | text(2000)? | **Donnée de santé** — règles de visibilité au §5.2. |
| `EmergencyContactName` / `EmergencyContactPhone` | text? | Téléphone validé par `MustBeValidSenegalPhone` (déjà utilisé pour `GuardianPhone`). |
| `AllowedExitPersons` | `jsonb` (collection possédée) | Liste de `{Name, Relationship, Phone}`, 10 maximum. |

Index uniques partiels : `(SchoolId, EnrollmentId) WHERE IsActive AND NOT IsDeleted` (un séjour actif par inscription) ;
`(BedId) WHERE IsActive AND BedId IS NOT NULL` (N3).

### 3.5 `BoardingLeave` (table `boarding_leaves`) — registre des sorties

`Id`, `SchoolId`, `BoardingEnrollmentId` (FK composite ; exposé en API sous le nom `boarderId`), `LeaveDate` (date),
`ExpectedReturnDate` (date, `≥ LeaveDate`), `ActualReturnDate` (date?, posée par `PUT .../return`, `≥ LeaveDate`),
`Reason` (enum `BoardingLeaveReason` : `Weekend`, `Sante`, `Famille`, + `Autre`), `AccompaniedBy` (text(150)),
`IsCompanionUnlisted` (bool, **figé à la déclaration** : vrai si `AccompaniedBy` ne correspondait à aucune personne habilitée à cet instant — la traçabilité ne doit pas changer si la liste est modifiée plus tard),
`ReasonDetail` (text(500)?). `Status` : **non stocké** (N6). Contrainte tenue par la base : **une seule sortie ouverte à la fois**
par pensionnaire (index unique partiel sur `(BoardingEnrollmentId) WHERE ActualReturnDate IS NULL AND NOT IsDeleted`).

### 3.6 `BoardingAttendance` (table `boarding_attendances`) — pointage de nuit

`Id`, `SchoolId`, `BoardingEnrollmentId`, `Date` (date), `IsPresent` (bool), `Note` (text(300)?). Index unique
`(BoardingEnrollmentId, Date)`. V1 = **nuitées uniquement** (pas de colonne `Slot`, voir §11-Q4).

### 3.7 RLS, purges, soft delete

- **RLS** : les six tables sont ajoutées à `TenantTables` d'une migration dédiée, avec la policy
  `{table}_tenant_isolation`, sur le modèle de `AddStudentSubjectExemptions` (AGENTS.md règle #2 : policy **et** filtre
  EF).
- **Purges** : `reset_school_data` et `delete_school_year` doivent être patchées pour supprimer ces tables **avant**
  `enrollments`/`students` (FK `Restrict`), sur le modèle de `AddStudentSubjectExemptionsToPurges` (insertion ancrée,
  idempotente, `Down()` exact). Sans cela, ces deux opérations échouent en `23503` dès qu'un pensionnaire existe.
- **Soft delete** : `Dormitory`, `DormitoryRoom`, `Bed` exposent suppression/restauration ; un lit occupé, une chambre
  contenant un lit occupé et un pavillon contenant une chambre occupée **ne se suppriment pas** (409
  `RESOURCE_IN_USE`, **nouveau code d'erreur** à ajouter au catalogue — il n'existe pas aujourd'hui). `BoardingEnrollment`/`BoardingLeave`/`BoardingAttendance` ne se suppriment pas : on clôt
  (N5) ou, pour une saisie erronée, le Directeur corrige via soft delete (aligné sur la règle #4 d'AGENTS.md).

## 4. Migrations et reprise des données

Principe : **expand → bascule → contract**, jamais de modification d'une migration déjà appliquée.

### 4.1 Migration 1 — `AddBoardingDormitoryModel` (additive)

Crée les six tables, index, policies RLS, et patche les deux fonctions de purge (§3.7). Dans la même migration,
**reprise des données existantes**, en SQL idempotent :

1. Chaque `Building` possédant au moins un `Room` de type `Dortoir` → un `Dormitory` (`Name` = nom du bâtiment).
   `Gender` = `Garcons`/`Filles` si tous les élèves inscrits dans ses chambres ont le même `Student.Gender`
   (`"M"`/`"F"`), sinon `Mixte`. Un pavillon vide reçoit `Mixte`.
2. Chaque `Room` `Dortoir` → un `DormitoryRoom` **qui réutilise le même `Id`** (traçabilité et FK faciles).
3. Pour chaque chambre : `max(Room.Capacity, nombre d'internes de l'année active)` lits numérotés 1..N. (Les occupants peuvent
   dépasser la capacité si celle-ci a été réduite après coup ; le dépassement est consigné, pas bloquant.)
4. Chaque `Enrollment` avec `BoardingStatus ≠ Externe` et statut ≠ `Cancelled` → un `BoardingEnrollment` :
   `Regime` ← `BoardingStatus`, `StartDate` ← date d'inscription. **Année scolaire active** : séjour actif, lit
   attribué dans l'ordre d'inscription si le régime est `Interne` et `RoomId` non nul, sinon `BedId` nul (« en
   attente » ; un `DemiPensionnaire` n'a jamais de lit, N7). **Autre année** : séjour **clos** (`IsActive = false`,
   `EndDate` = fin de l'année, `BedId` nul), pour qu'une inscription passée n'occupe jamais un lit. Les occupants de
   chambres supprimées ou qui ne sont plus de type `Dortoir` sont repris sans lit.
5. Une requête de contrôle (testée) vérifie : `#BoardingEnrollment actifs = #Enrollment pensionnaires`, et aucun lit
   attribué deux fois.

### 4.2 Bascule (même PR de code que la Migration 1, dans le lot C)

`GetInternatDashboard`, `SearchBoardableStudents`, `CreateEnrollment` (champs `BoardingStatus`/`RoomId`/
`IncludeBoardingFee`) et le badge de `GetStudentDetail` lisent et écrivent via une **application service unique**
`IBoardingAssignmentService` (Application) — jamais deux implémentations de la règle d'affectation. À partir de là,
`Enrollment.BoardingStatus` et `Enrollment.RoomId` ne sont plus écrits (marqués `[Obsolete]`), pour éviter une double
écriture à synchroniser.

`CreateEnrollmentCommand` conserve son champ `BoardingStatus` côté API le temps de la transition : `Interne`/
`DemiPensionnaire` crée le `BoardingEnrollment` dans la **même transaction** que l'inscription ; la garde 422 « module
désactivé » de la spec du 18/09 §4 est conservée.

### 4.3 Migration 2 — `DropLegacyBoardingColumns` (contract, PR suivante)

Supprime `enrollments.boarding_status` et `enrollments.room_id` (+ FK composite). Les routes `/api/v1/internat/*` sont
retirées une fois l'écran migré. La valeur `RoomType.Dortoir` n'est **pas** retirée de l'enum (elle peut subsister dans
d'anciennes données ou sauvegardes) : le code cesse simplement de l'utiliser. Condition de lancement : requête de
divergence à zéro en préproduction. Le CRUD de salles de `/infrastructures` interdit désormais de créer une salle
`Dortoir` (422) — les dortoirs se créent via `/api/v1/boarding/dormitories`.

## 5. Sécurité

### 5.1 Rôles

| Action | Rôles |
|---|---|
| Lecture (pavillons, pensionnaires, sorties, pointage) | `Directeur`, `Secretariat`, `Surveillant` |
| Écriture pavillons/chambres/lits | `Directeur`, `Secretariat` |
| Affectation, fin de séjour, sorties, retours, pointage | `Directeur`, `Secretariat`, `Surveillant` |
| Fiche médicale en lecture/écriture | `Directeur`, `Surveillant` uniquement (§5.2) |
| Restauration (corbeille) | `Directeur`, `Secretariat` |

### 5.2 Données de santé et d'identité

`MedicalNotes` est une donnée de santé d'un mineur. Règles :

- `Secretariat` reçoit `MedicalNotes = null` dans `GET /boarders/{id}` et ne peut pas l'écrire (champ ignoré → 403 si
  tenté explicitement) ; la fiche PDF individuelle l'omet pour ce rôle.
- Le **registre PDF du pavillon** (affiché/partagé) n'inclut **jamais** `MedicalNotes` ni téléphones : identité,
  chambre, lit, présence uniquement.
- `MedicalNotes` ne doit pas apparaître en clair dans `audit_logs`. **À vérifier à l'implémentation** que la
  sérialisation d'`IAuditableRequest` n'embarque pas les corps de requête ; sinon, exclure le champ par attribut.
- Un élève ne peut loger que dans un pavillon de son genre (`Student.Gender` ↔ `Dormitory.Gender`), sauf `Mixte`
  → 422 `GENDER_MISMATCH`.

### 5.3 Garde de module

`[RequireModule(SchoolModule.Internat)]` : 403 `MODULE_DISABLED` sur toutes les routes si le module est désactivé.
Les PDF sont des routes du même contrôleur, donc couverts.

## 6. API et cas d'usage (CQRS / MediatR)

Contrôleur mince (`BoardingController`, règle #8), un `Command`/`Query` par route dans
`SamaEcole.Application/Boarding/...`. Écritures sensibles `IAuditableRequest`. Erreurs au format normalisé du
Volume 4 §0.4. Listes paginées (`page`, `pageSize`).

### 6.1 Pavillons, chambres, lits

| Route | Cas d'usage | Notes |
|---|---|---|
| `GET /api/v1/boarding/dormitories` | `ListDormitoriesQuery` | Par pavillon : capacité, lits occupés, lits en maintenance, **taux d'occupation**. Filtre `gender`. |
| `POST /api/v1/boarding/dormitories` | `CreateDormitoryCommand` | 409 `ARCHIVED_ENTITY_EXISTS` si le nom existe dans la corbeille. `Mixte` refusé à la création (422). |
| `PUT /api/v1/boarding/dormitories/{id}` | `UpdateDormitoryCommand` | `rowVersion` requis (409 si périmé). Changer `Gender` d'un pavillon contenant des pensionnaires → 422. |
| `POST /api/v1/boarding/rooms` | `CreateDormitoryRoomCommand` | Corps : `dormitoryId`, `name`, `bedCount` (1–40). Crée la chambre et ses lits. |
| `POST /api/v1/boarding/beds` | `CreateBedCommand` | Ajout d'un lit à une chambre ; `bedNumber` auto (max + 1) si omis. |

**Compléments proposés (absents de la feuille de route, nécessaires pour que le modèle soit exploitable) :**
`PUT /beds/{id}/status` (`Available` ↔ `Maintenance` ; refusé si lit occupé), `PUT /rooms/{id}`,
`DELETE /dormitories|rooms|beds/{id}` (soft delete, garde `RESOURCE_IN_USE`), `POST .../{id}/restore`.

### 6.2 Pensionnaires

| Route | Cas d'usage | Règles |
|---|---|---|
| `POST /api/v1/boarding/assign-bed` | `AssignBedCommand` | Corps : `enrollmentId`, `regime`, `bedId?`, `includeBoardingFee`, `rowVersion?`. Crée le `BoardingEnrollment` s'il n'existe pas, sinon le **transfère** (`rowVersion` requis → 409). Voir règles ci-dessous. |
| `DELETE /api/v1/boarding/unassign-bed/{boarderId}?rowVersion=` | `EndBoardingCommand` | N5. Ne touche jamais une ligne de pension déjà facturée. 409 si une sortie est ouverte (retour à enregistrer d'abord). |
| `GET /api/v1/boarding/boarders` | `ListBoardersQuery` | Filtres `dormitoryId`, `roomId`, `regime`, `status` (`active`/`ended`/`awaitingBed`/`onLeave`), `search` (nom, matricule). Année active par défaut. |
| `GET /api/v1/boarding/boarders/{id}` | `GetBoarderQuery` | Fiche complète + sorties ouvertes/récentes + `rowVersion`. §5.2 pour les champs médicaux. |

**Complément proposé :** `PUT /api/v1/boarding/boarders/{id}/profile` (`UpdateBoarderProfileCommand` :
`medicalNotes`, contact d'urgence, `allowedExitPersons`). Sans lui, les champs de santé et d'habilitation de la
feuille de route ne sont jamais saisissables.

**Règles de `AssignBedCommand`** (dans l'ordre, tout dans **une** transaction) :

1. Valeur d'enum définie, sinon 422 (même garde de forme que `ChangeBoardingAssignmentCommand`).
2. L'`Enrollment` appartient à l'année **active** et n'est pas `Cancelled`, sinon 404/422 (reprise du handler actuel).
3. `Interne` ⇒ `bedId` requis ; `DemiPensionnaire` ⇒ `bedId` interdit → 422.
4. Lit existant, du tenant, `Available` → sinon 422 `BED_IN_MAINTENANCE` ou 404.
5. Genre compatible (§5.2) → 422 `GENDER_MISMATCH`.
6. Lit déjà occupé → 409 `BED_UNAVAILABLE`. Une course entre deux affectations sur le dernier lit est tranchée par
   l'index unique (N3) ; la violation `23505` est traduite en ce même 409, jamais en 500.
7. `rowVersion` posé via `SetOriginalConcurrencyToken` avant modification ; périmé → 409.
8. Si `includeBoardingFee` : même logique que `BoardingFeeLineBuilder` (jamais de doublon, jamais de retrait) ;
   `TotalDue` incrémenté dans la même transaction.

### 6.3 Sorties et pointage

| Route | Cas d'usage | Règles |
|---|---|---|
| `POST /api/v1/boarding/leaves` | `DeclareLeaveCommand` | Pensionnaire actif ; pas de sortie déjà ouverte (409) ; `ExpectedReturnDate ≥ LeaveDate`. Si `AccompaniedBy` ne correspond à aucune `AllowedExitPersons`, la sortie est **acceptée** et `IsCompanionUnlisted = true` est **persisté** (décision Q5) : avertissement visuel à la saisie, pastille dans le registre des sorties et dans la fiche PDF. Aucun blocage — souplesse terrain, notamment pour une urgence de santé. |
| `PUT /api/v1/boarding/leaves/{id}/return` | `RecordReturnCommand` | Pose `ActualReturnDate` (≤ aujourd'hui) ; idempotent refusé : déjà rentré → 409. `rowVersion`. |
| `POST /api/v1/boarding/attendance` | `RecordAttendanceCommand` | Pointage **collectif** : `{ date, dormitoryId, entries: [{ boarderId, isPresent, note? }] }`. Upsert par `(boarder, date)` ; date ≤ aujourd'hui, année active, jour pointable. Un pensionnaire en sortie ce soir-là est accepté uniquement avec `isPresent = false` (la ligne indique « En permission » si la note est vide). Réponse : résultat par ligne. |

**Complément proposé :** `GET /api/v1/boarding/attendance?dormitoryId=&date=` (feuille d'appel du soir, avec pré-remplissage
des absents en permission) — indispensable à l'écran de pointage collectif.

### 6.4 Statut d'une sortie (N6)

Calculé par une fonction pure `BoardingLeaveStatus.Of(leave, today)` (testée en unitaire) :
`Returned` si `ActualReturnDate` non nulle ; sinon `Pending` si `today < LeaveDate` ; sinon `Overdue` si
`today > ExpectedReturnDate` ; sinon `Active`. La requête de liste applique le même prédicat en SQL pour le filtre
`status`. La date du jour provient de `TimeProvider` (déjà injecté dans plusieurs handlers, ex. `EntryTicketCommands`), jamais
de `DateTime.Now`, pour rester testable ; le fuseau de l'école est celui déjà employé par ces handlers.

## 7. Documents QuestPDF

Même architecture que les générateurs existants : interface `IBoarderSheetPdfGenerator` /
`IDormitoryRegisterPdfGenerator` dans `Application/Common/Interfaces`, `*Document` + `*PdfGenerator` dans
`Infrastructure/Documents`, enregistrement dans `DependencyInjection`, requêtes `GetBoarderSheetPdfQuery` /
`GetDormitoryRegisterPdfQuery`.

| Route | Contenu |
|---|---|
| `GET /api/v1/boarding/boarders/{id}/pdf` | Fiche individuelle : identité (nom arabe si renseigné, via le composant bilingue existant), classe, pavillon/chambre/lit, dates de séjour, contact d'urgence, personnes habilitées, **fiche médicale selon le rôle** (§5.2), historique des sorties de l'année. |
| `GET /api/v1/boarding/dormitories/{id}/register-pdf?date=` | Registre imprimable : tableau chambre/lit/nom/classe, colonne de pointage vide pour la nuit demandée (ou remplie si le pointage existe), sorties en cours signalées. **Sans** donnée médicale ni téléphone (§5.2). |

Les PDF sont générés à la demande, jamais stockés ; en-têtes `Cache-Control: no-store` (donnée nominative de mineurs).

## 8. Phase 2 — Étanchéité IHM par profil

### 8.1 État actuel (vérifié dans le code)

- Les pages (`/internat`, `/halqa`, `/oustaz`, `/suivi-coranique`) sont des **gabarits anonymes** servis par
  `PagesController` : le navigateur n'envoie pas d'en-tête `Authorization` en naviguant, donc le serveur ne connaît
  ni l'école ni le profil au moment du `GET` de la page. Un statut HTTP 404 décidé côté serveur est donc impossible ;
  la décision se prend **côté client**, après lecture des réglages. La vraie protection reste côté API
  (`[RequireModule]` → 403 `MODULE_DISABLED`, RLS).
- Il existe **deux notions de profil** : `ProfileEtablissement` (réglages ; 5 valeurs ; pilote les presets
  `EstablishmentProfilePresets`) et `ProfileType` (souscription ; `Elementaire`, `FrancoArabe`, `InternatDaara`,
  `EnseignementGeneral`, `ComptabiliteRapports`), reliés par `ProfileTypeMapping`. Le texte de la feuille de route
  utilise `ProfileType` ; c'est ce vocabulaire qui est employé ci-dessous.
- **Anomalie à corriger** (`Views/Shared/_Layout.cshtml`, lignes 216, 220, 225) : les menus
  `/suivi-coranique`, `/oustaz` et `/halqa` sont conditionnés par `internatEnabled`, alors que leur API
  (`QuranController`) est gardée par `SchoolModule.Coran`. Conséquence : une école Franco-Arabe (Coran activé, Internat
  désactivé) a l'API mais pas le menu ; une école avec Internat seul voit des menus dont l'API répond 403.

### 8.2 Règle de visibilité

**Décision Q6 (06/10/2026) :** les interrupteurs de module (`IsInternatEnabled`, `IsCoranModuleEnabled`) font foi.
La sidebar, la garde de page et les contrôles d'accès API vérifient le même état effectif du module pour l'école ;
le profil n'est qu'un **pré-positionnement** à l'Onboarding (`EstablishmentProfilePresets`). Il n'y a pas de blocage
dur par profil : un Directeur `Elementaire` peut activer l'Internat dans Paramètres › Modules, et l'obtient alors
partout, API et IHM de façon cohérente. Le tableau ci-dessous décrit donc l'état **par défaut** après Onboarding.

| `ProfileType` | Internat (`/internat`) | Coran (`/suivi-coranique`, `/halqa`, `/oustaz`) | Rôle dans l'interface |
|---|---|---|---|
| `Elementaire` | masqué par défaut, route → redirection | masqué par défaut, route → redirection | — |
| `EnseignementGeneral` | masqué | masqué | — |
| `ComptabiliteRapports` | masqué | masqué | — |
| `FrancoArabe` | masqué | **visible** | Coran secondaire |
| `InternatDaara` | **visible** | **visible** | **Modules principaux** : atterrissage Directeur sur `/suivi-coranique` (déjà en place, `PROFILE_LANDINGS`), groupe « Internat / Coran » remonté en tête de la section « Gestion scolaire » |

Les lignes `EnseignementGeneral`, `ComptabiliteRapports` et `FrancoArabe` découlent des presets existants ; la feuille
de route ne tranchait que `Elementaire` et `InternatDaara`.

### 8.3 Mécanisme

1. **Un seul endroit déclare le module d'une page** : un attribut `[PageModule(SchoolModule.X)]` sur l'action de
   `PagesController`. Un filtre d'action le recopie dans le `ViewData` et `_Layout` émet
   `<meta name="required-module" content="Internat">`. Aucune table route→module dans le JS à tenir à jour ; les
   sous-routes (`/internat/...`) héritent par préfixe d'action.
2. **Store `schoolConfig`** (`auth.js`) : ajouter `coranEnabled` (déjà fourni par `/schools/current/settings` via
   `isCoranModuleEnabled`) et `loadFailed` (vrai si l'appel a échoué).
3. **Garde de page** `module-route-guard.js`, chargé par `_Layout` : si la meta existe, attendre
   `schoolConfig.init()` ; module activé → afficher ; module désactivé → `location.replace` vers l'atterrissage du
   rôle (`auth.js`) + toast « Ce module n'est pas activé pour votre établissement ». Contenu masqué jusqu'à la
   décision (`x-cloak`) pour éviter le flash.
4. **Erreur réseau ≠ module désactivé.** Si `loadFailed`, la garde **ne redirige pas** : bandeau « Impossible de
   vérifier les modules activés — Réessayer ». Sinon une coupure réseau (fréquente, cf. `network-guard.js`) éjecterait
   à tort un Directeur de Daara de son module principal. (Même principe que l'état `undefined` de
   `profileEtablissement`, qui ne force jamais une redirection.)
5. **Navigation** : `/suivi-coranique`, `/oustaz`, `/halqa` conditionnés par `coranEnabled`, `/internat` par
   `internatEnabled`, aussi dans `_QuickNav.cshtml`. Les autres usages à auditer et aligner (liste vérifiée) :
   `Enrollments/Index.cshtml` (section régime), `Fees/Index.cshtml` (case pension), `Subjects/Index.cshtml` (blocs
   conditionnés par `isFrancoArabeProfile || isDaaraInternatProfile`, donc par le **profil** et non par un module : ce sont
   des blocs de pédagogie bilingue, pas des écrans Internat/Coran ; hors périmètre de cette phase, à auditer à part).

### 8.4 Ce que cette phase ne garantit pas

La redirection est de l'ergonomie, pas de la sécurité : un utilisateur qui force l'URL récupère le gabarit HTML vide
de données ; chaque appel d'API répond 403 `MODULE_DISABLED` et la RLS isole les écoles. Cette phase ne doit jamais
être présentée comme une barrière d'accès.

## 9. Tests

**Unitaires** : validators (genre, régime ↔ lit, dates de sortie), `BoardingLeaveStatus.Of` (4 états + bornes),
mapping genre `M/F` ↔ pavillon, règles de capacité dérivée.

**Fonctionnels** (HTTP → MediatR → PostgreSQL réel, catégorie `MultiTenant` incluse) :
- `RlsCoverageTests` : les six nouvelles tables sont couvertes (policy + filtre) ; isolation entre deux écoles sur
  chaque `GET`/`POST`.
- **Course sur le dernier lit** : deux `AssignBedCommand` simultanés → un succès, un 409 `BED_UNAVAILABLE`.
- **Concurrence xmin** : deux transferts simultanés du même pensionnaire → 409 sur le second.
- **Une sortie ouverte à la fois** ; retour déjà enregistré → 409.
- **Libération** : une ligne de pension déjà facturée persiste après `unassign-bed`.
- **Garde de module** : `IsInternatEnabled = false` → 403 `MODULE_DISABLED` sur toutes les routes, PDF compris.
- **Données de santé** : `Secretariat` ne reçoit pas `MedicalNotes` (JSON et PDF) ; le registre PDF n'en contient pas.
- **Rôles** : `Enseignant`/`Finance` → 403 sur chaque route.
- **Migration 1** : jeu de données hérité (dortoirs, inscriptions, chambre sur-occupée, interne sans chambre) → contrôle
  du §4.1 point 5 ; ré-exécution idempotente ; patch des purges (`reset_school_data`, `delete_school_year` réussissent
  avec un pensionnaire présent).
- **Soft delete** : cycle Create → Delete → Trash → Restore, `RESOURCE_IN_USE` sur un lit occupé,
  `ARCHIVED_ENTITY_EXISTS` sur un nom archivé.
- **Droits SQL** : `sama_ecole_app` ne peut pas `DELETE` sur les six tables (déjà garanti par `ALTER DEFAULT
  PRIVILEGES`, à vérifier par le test existant).
- **Phase 2** : test de réflexion — chaque action de page portant `[PageModule(X)]` a au moins un contrôleur API
  `[RequireModule(X)]` ; un test JS (ou Playwright si déjà outillé) vérifie les cinq profils × quatre routes.

## 10. Découpage en lots

| Lot | Contenu | Branche/PR |
|---|---|---|
| A | Entités, configurations EF, Migration 1 (tables, RLS, purges, reprise) + tests de migration | `feat/internat-backend` |
| B | Pavillons/chambres/lits (CQRS, soft delete, compléments §6.1) | idem |
| C | Pensionnaires, `IBoardingAssignmentService`, bascule de `CreateEnrollment`/dashboard/fiche élève | idem |
| D | Sorties et pointage | idem |
| E | PDF | idem |
| F | Migration 2 (contract) + retrait de `/api/v1/internat/*` — après migration de l'écran | PR séparée |
| G | Phase 2 : étanchéité IHM | branche front dédiée |

Un lot = une PR relue ; la suite complète reste verte entre chaque lot.

## 11. Décisions de conception tranchées (06/10/2026)

| # | Sujet | Décision |
|---|---|---|
| Q1 | Anciennes salles `Dortoir` | Filtrées/masquées dans la gestion classique des salles (`/infrastructures`, sélecteurs de salles) pour éviter la confusion avec `Dormitory`. Implémentation : `GET /rooms` et les sélecteurs excluent `Type = Dortoir` ; la création d'une salle `Dortoir` est refusée (422, §4.3). Les lignes restent en base (reprise §4.1, rollback possible). |
| Q2 | Genre des dortoirs repris | `Mixte` accepté pour la reprise de données. Il reste **refusé** à la création et à la modification via l'API : le Directeur qualifie une fois chaque pavillon repris. |
| Q3 | Demi-pensionnaires | Aucun lit (N7). |
| Q4 | Pointage V1 | Nuitées uniquement ; repas dans une évolution ultérieure (ajout additif d'un `Slot`). |
| Q5 | Accompagnateur non habilité | Saisie jamais bloquée ; avertissement visuel + `IsCompanionUnlisted` persisté dans le registre (§3.5, §6.3). |
| Q6 | Profil vs interrupteurs | Les interrupteurs de module font foi, pour l'IHM comme pour l'API (§8.2). |
| Q7 | Surveillant responsable | Texte libre (nom, téléphone) en V1, avec liaison optionnelle à un compte `Surveillant` (§3.1). |

**Point résiduel :** le filtre « surveillant ne voit que ses pavillons » (via `SupervisorUserId`) n'est **pas** inclus
en V1 : un `Surveillant` voit tous les pavillons de l'école, comme dans la spec du 18/09.

## 12. Hors périmètre (rappel)

Refonte de l'écran `/internat`, portail parents, SMS/notifications de sortie, facturation au prorata, rapports
statistiques d'occupation historiques, intégration avec le suivi coranique au-delà de l'atterrissage par profil.
