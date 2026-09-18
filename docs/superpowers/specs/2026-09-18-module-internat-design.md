# Module Internat — Spécification

**Date :** 18/09/2026
**Statut :** Validé en brainstorming, en attente de plan d'implémentation.
**Périmètre :** Rendre opérationnel le module Internat (`SchoolModule.Internat`,
`SchoolSettings.IsInternatEnabled`), actuellement un réglage « réservé » sans écran ni route
(commit `12ffbb2`). Le module Coran/Franco-Arabe n'est PAS concerné par cette spec — hors
périmètre, à cadrer séparément si besoin.

## 1. Contexte

Le toggle `IsInternatEnabled` existe côté `SchoolSettings` depuis le 17/09/2026, désactivé par
défaut, documenté comme « n'ouvre rien aujourd'hui ». Aucune spec fonctionnelle n'existe dans
`docs/Volume_1_Cahier_des_Charges.md` ni `docs/BACKLOG_TICKETS.md` pour l'Internat : ce document
en tient lieu.

Modules existants directement réutilisés :
- **Infrastructures** (`Building`/`Room`, `docs/Volume_1_Cahier_des_Charges.md`, écran
  `/infrastructures`) : local physique, `Room.Type` (enum `RoomType`, persisté en string),
  `Room.Capacity` (int).
- **Frais** (`FeeCategory` libre + `ClassFee` = barème par classe, `EnrollmentFeeLine` = instantané
  figé à l'inscription). `Enrollment.TotalDue` est aujourd'hui calculé **exclusivement** à partir
  des `ClassFee` de la classe choisie (`CreateEnrollmentCommandHandler.BuildFeeLinesAsync`),
  appliqués à **tous** les élèves de cette classe sans distinction.

## 2. Décisions actées (brainstorming du 17-18/09/2026)

| # | Question | Décision |
|---|---|---|
| 1 | Modèle physique | Réutiliser `Building`/`Room` — pas de hiérarchie séparée. |
| 2 | Modèle du lit | Comptage simple (`Room.Capacity`), **pas** d'entité `Bed`. |
| 3 | Rôles gestionnaires | Directeur + Secretariat + Surveillant. |
| 4 | Portée du régime | Sur l'inscription (`Enrollment`), par année scolaire — comme `IsRepeating`. |
| 5 | Périmètre écran | `/infrastructures` reste la seule source de création Building/Room ; `/internat` ne fait que l'affectation. |
| 6 | Facturation pension | Effet réel sur `TotalDue`, via un flag `FeeCategory.IsBoardingFee` — pas de nouveau moteur financier. |
| 7 | Libération (retour à Externe) | La ligne de pension déjà facturée **reste due** — aucun retrait automatique, aucun prorata. |

## 3. Modèle de données

### 3.1 `RoomType` (enum, `SamaEcole.Domain.Enums.CommonEnums`)

Ajout de `Dortoir` : `SalleDeClasse, Laboratoire, Bureau, Dortoir, Autre`. Stocké en string
(`HasConversion<string>()` déjà en place dans `RoomConfiguration`) : **aucune migration de
colonne nécessaire**, uniquement un ajout de valeur d'enum côté C#.

### 3.2 `FeeCategory`

Nouvelle colonne `IsBoardingFee` (bool, défaut `false`) — même statut que `IsRecurring` : un
choix de l'école, pas une énumération codée en dur de catégories. L'école désigne UNE catégorie
existante (typiquement « Pension ») comme frais d'internat. Le montant reste porté par `ClassFee`
(barème par classe), sans nouveau concept financier.

### 3.3 `Enrollment`

Deux colonnes nullables :
- `BoardingStatus` (enum `Externe` (défaut) `/ DemiPensionnaire / Interne`).
- `RoomId` (Guid?, FK vers `Room`, `OnDelete(Restrict)` comme les autres FK tenant) —
  significatif seulement si `BoardingStatus != Externe`.

Portée annuelle : une réinscription reconfirme ou change le régime, exactement comme
`IsRepeating`. Aucun report automatique d'une année sur l'autre.

### 3.4 Migration

Une migration additive unique (nom proposé : `AddInternatBoarding`) :
- `enrollments.boarding_status` (text, défaut `'Externe'`), `enrollments.room_id` (uuid, nullable,
  FK).
- `fee_categories.is_boarding_fee` (bool, défaut `false`).
- Aucun changement RLS : `enrollments`, `rooms` et `fee_categories` sont déjà dans `TenantTables`.

### 3.5 Ce qui n'est PAS construit

- Pas d'entité `Bed` (décision #2).
- Pas de nouvelle table d'historique pour les changements de chambre/régime — `IAuditableRequest`
  + `audit_logs` (mécanisme générique déjà utilisé par `ChangeEnrollmentStatusCommand`,
  `CancelEnrollmentCommand`) suffit.
- Pas de mécanisme de remboursement/prorata à la libération (décision #7) — si un remboursement se
  justifie, il passe par le geste de correction Secrétariat/Admin déjà prévu par la règle #4
  d'AGENTS.md, hors périmètre de ce module.
- Pas de CRUD Building/Room dans `/internat` (décision #5).

## 4. Sécurité

- `[RequireModule(SchoolModule.Internat)]` sur le futur `InternatController` — `SchoolModule` et
  `ModuleAuthorizationHandler` existent déjà (commit `12ffbb2`), seul le contrôleur est nouveau.
- Rôles autorisés à écrire (affecter/réaffecter/libérer, changer le régime) : `Directeur,
  Secretariat, Surveillant` — même trio que `ParentSummonsController`.
- Garde serveur si le module est désactivé : `CreateEnrollmentCommandValidator` **rejette en 422**
  toute valeur de `BoardingStatus` autre que `Externe` (et tout `RoomId` non nul) quand
  `SchoolSettings.IsInternatEnabled == false` — jamais un silence qui accepterait puis ignorerait
  la valeur (même philosophie que la garde déjà en place pour `SchoolModule.Pedagogy` sur
  `/grades/mentions`).
- `GetInternatDashboardQuery` et `SearchBoardableStudentsQuery` : mêmes rôles, lecture seule.

## 5. Backend — CQRS

### 5.1 `CreateEnrollmentCommand` (existant, étendu)

Nouveaux champs : `BoardingStatus` (défaut `Externe`), `RoomId?`, `IncludeBoardingFee` (bool,
défaut `false`).

Handler (`CreateEnrollmentCommandHandler`) :
1. Si `BoardingStatus != Externe` et `RoomId` fourni : recompte l'occupation active de la chambre
   dans la **même transaction** que la création de l'inscription → **422** si
   `occupants >= Capacity` (message actionnable, chambre identifiée).
2. `BuildFeeLinesAsync` inchangé pour les `ClassFee` ordinaires ; ajoute, **seulement si**
   `IncludeBoardingFee == true`, une ligne pour chaque catégorie flaguée `IsBoardingFee` qui a un
   `ClassFee` sur la classe choisie (typiquement une seule catégorie « Pension », mais rien
   n'empêche techniquement d'en flaguer plusieurs). Les élèves `Externe` de la même classe ne
   voient jamais ces lignes.

### 5.2 `ChangeBoardingAssignmentCommand` (nouveau)

`InternatController`, `IRequest<EnrollmentBoardingDto>`, `IAuditableRequest`.

Entrée : `EnrollmentId`, `RoomId?` (`null` = libération), `BoardingStatus`, `IncludeBoardingFee`.

Handler :
1. Charge l'`Enrollment` (tenant courant, année active) — 404 sinon.
2. Si `RoomId` non nul : recompte l'occupation active de la chambre cible en transaction → **422**
   « Cette chambre a atteint sa capacité maximale » si pleine (le compte exclut l'élève courant
   s'il occupe déjà cette même chambre, pour permettre une confirmation sans changement réel).
3. Écrit `BoardingStatus`/`RoomId` sur l'`Enrollment` sous son verrou `xmin` existant → **409**
   « Le dossier de cet élève a été modifié par un autre utilisateur » si la ligne a changé
   entre-temps (double clic, un autre utilisateur).
4. Si `IncludeBoardingFee == true` : pour chaque catégorie flaguée `IsBoardingFee` qui a un
   `ClassFee` sur la classe de l'élève ET n'a **pas déjà** de `EnrollmentFeeLine` correspondante
   sur cette inscription, ajoute la ligne et incrémente `TotalDue` d'autant, dans la même
   transaction que l'écriture xmin. **Jamais de doublon** sur un second transfert de chambre.
5. Libération (`RoomId = null`) : ne touche **jamais** aux lignes déjà facturées (décision #7).

### 5.3 `GetInternatDashboardQuery` (nouveau)

Retourne, pour l'année scolaire active :
- KPIs globaux : occupation (occupés/capacité totale des dortoirs), répartition par régime
  (Interne / Demi-pensionnaire), nombre de chambres complètes vs. avec places libres.
- Liste des `Room` de type `Dortoir` (rattachées à un `Building`), avec pour chacune : capacité,
  occupants actuels (nom, classe, téléphone du tuteur), places libres.

### 5.4 `SearchBoardableStudentsQuery` (nouveau)

Autocomplétion élève par nom/prénom/matricule, restreinte aux inscriptions **actives de l'année en
cours**. Retourne pour chaque résultat : identité, classe, régime actuel, chambre actuelle le cas
échéant (pour le badge et l'avertissement de transfert côté UI).

## 6. Frontend

### 6.1 Navigation (`auth.js`)

Nouvelle entrée « Internat » (icône `fa-bed`) dans `sidebarNav()`, visible si
`SchoolSettings.isInternatEnabled === true` **ET** rôle courant ∈ {Directeur, Secretariat,
Surveillant}. Contrairement à `pedagogyEnabled`/`financeEnabled` (actifs par défaut, masqués si
`false`), Internat est **masqué par défaut** et affiché seulement si explicitement activé —
logique inversée à implémenter avec soin dans `sidebarNav()`.

### 6.2 Écran `/internat`

- **Barre KPI** : occupation globale, répartition par régime, chambres complètes/disponibles
  (section 5.3).
- **Cartes par chambre** (dortoirs déclarés dans `/infrastructures`) : jauge d'occupation colorée
  (vert = places libres, rouge = complet), accordéon/drawer des occupants (nom, prénom, classe,
  téléphone tuteur), bouton « + Affecter un élève » (désactivé si `occupants == capacity`) et
  « Changer de chambre / Libérer ».
- **Modale d'affectation rapide** :
  - Chambre cible pré-sélectionnée et verrouillée.
  - Recherche élève par autocomplétion (`SearchBoardableStudentsQuery`), restreinte à l'année
    active.
  - Badge du statut actuel dès sélection (Externe / Demi-pensionnaire / Interne + chambre si
    applicable).
  - Avertissement si déjà logé ailleurs : « L'élève X sera transféré depuis la Chambre Y vers
    cette chambre ».
  - Commutateur de régime, défaut Interne.
  - Case « Proposer l'ajout du frais de pension au dossier de l'élève », cochée par défaut.
  - Bouton « Confirmer l'affectation » → `ChangeBoardingAssignmentCommand`.
  - Erreurs : 422 (« Cette chambre a atteint sa capacité maximale ») et 409 (« Le dossier de cet
    élève a été modifié par un autre utilisateur ») affichées telles quelles, sans traduction
    supplémentaire côté client — même patron que les autres écrans (`window.api.toMessage()`).

### 6.3 Fiche élève & Inscription/Réinscription

- Section « Régime & Hébergement » dans le formulaire d'inscription/réinscription : select
  `BoardingStatus`, select chambre **conditionnel** (visible seulement si non-Externe, filtré aux
  `Room` de type `Dortoir` avec `occupants < capacity`), case pension précochée
  (`IncludeBoardingFee`).
- Badge sur la fiche élève : « Interne — Pavillon A / Ch. 102 » (nom du `Building` + nom de la
  `Room`), affiché uniquement si `BoardingStatus != Externe`.
- Ces deux champs sont soumis à la même garde 422 que la section 4 si le module est désactivé
  côté serveur.

## 7. Tests

- **Unitaires** : `CreateEnrollmentCommandValidator` (rejet 422 si module désactivé et régime ≠
  Externe), calcul de la ligne de pension (présente seulement si `IncludeBoardingFee` +
  `ClassFee` existant ; absente sinon), non-duplication sur un second `ChangeBoardingAssignment`.
- **Fonctionnels** (HTTP → MediatR → PostgreSQL réel, catégorie `MultiTenant` incluse) :
  - `RlsCoverageTests` : `enrollments.room_id`/`boarding_status`, `fee_categories.is_boarding_fee`
    couverts par les policies existantes (pas de nouvelle table, vérification de non-régression).
  - Capacité : deux affectations concurrentes sur le dernier lit d'une chambre → une seule réussit,
    l'autre reçoit 422.
  - Concurrence : deux `ChangeBoardingAssignmentCommand` simultanés sur la même inscription → 409
    sur le second.
  - Garde module : `IsInternatEnabled = false` → 403 sur `InternatController`, 422 sur une
    inscription qui tente `BoardingStatus != Externe`.
  - Libération : la ligne de pension déjà facturée persiste après un retour à Externe.

## 8. Hors périmètre (rappel)

- Le module Coran/Franco-Arabe (`SchoolModule.Coran`) — cadrage séparé.
- Toute nouvelle mécanique de remboursement/prorata à la libération.
- Le CRUD Building/Room dans `/internat` (reste dans `/infrastructures`).
- L'entité `Bed` / le suivi nominatif d'un lit précis.
