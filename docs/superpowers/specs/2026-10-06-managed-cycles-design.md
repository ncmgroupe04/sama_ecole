# Conception — Réglage « Cycles gérés » (SchoolSettings)

**Date :** 2026-10-06
**Statut :** conception validée par le commanditaire (y compris les 4 points du §9, tranchés le 2026-10-06) ; revue de l'équipe attendue. **Aucun code avant la fusion de la PR #56** (voir §8).
**Périmètre de livraison :** une PR dédiée, branche `feat/managed-cycles-settings`, tirée de `main` après la fusion de la PR #56 (Onboarding & Tiered Pricing).

## 1. Problème

Les écrans qui proposent des niveaux ou des classes (création de classe, inscription, examens officiels) ne savent pas quels **cycles** l'établissement gère réellement. Ils le devinent :

- `classrooms.js` (`visibleLevelOptions`), `enrollments.js` (`visibleClassrooms`) et `exams.js` (`examTypeOptions`) testent chacun `profileEtablissement === 'ElementairePrimaire'` pour cacher Collège/Lycée ;
- `subjects.js` (`activeCycles`) déduit les cycles des matières déjà saisies — c'est de l'affichage, pas une règle ;
- `SchoolCycleProfile` (Primaire / Collège / Lycée / Bicycle / Complexe) décrit l'offre commerciale d'une demande d'inscription ; il n'existe que pour le parcours self-service et n'est pas modifiable.

Conséquence : seul le profil Élémentaire est restreint. Un Daara « pur » (ni Collège ni Lycée) ou un Collège sans Lycée ne peut pas désencombrer ses écrans. Décision produit du 2026-10-06 : **ne pas** masquer Collège/Lycée d'autorité pour le profil Internat / Daara (il couvre aussi des Daaras mixtes qui préparent au BFEM ou au BAC), mais laisser l'établissement déclarer ses cycles.

## 2. Décisions validées

1. **Un réglage explicite**, propriété de l'établissement : `SchoolSettings.ManagedCycles`. Il ne dépend pas du `ProfileType`.
2. **Stockage** : une colonne texte « valeurs séparées par des virgules », comme `WorkingDays`. Pas d'enum de drapeaux : l'ordre des membres d'un enum ne peut pas casser la donnée, et la colonne reste lisible en base.
3. **Valeurs** : les noms de `CycleType` — `Maternelle`, `Primaire`, `College`, `Lycee`. Il n'y a **pas** de cycle « Crèche » : Crèche et Maternelle sont deux *niveaux* du cycle `Maternelle` (voir `ClassroomCycle.CycleFor`). Le libellé affiché est « Maternelle / Crèche ».
4. **Confort d'affichage, pas verrou de création** : le serveur ne refuse pas la création d'une classe dans un cycle non géré (même philosophie que le filtre actuel des profils). Un niveau déjà enregistré reste visible à l'édition.
5. **Garde-fou de désactivation (règle 6)** : décocher un cycle qui contient des classes vivantes est **refusé par le serveur** (409) et expliqué par l'interface. Ce n'est pas un verrouillage : c'est ce qui empêche des classes de disparaître des écrans.
6. **L'Onboarding initialise, il n'écrase jamais** : la fin de l'Onboarding pose les cycles ; un changement de profil ultérieur (`POST /schools/current/settings/establishment-profile`) ne les modifie pas.

## 3. Modèle de données

### 3.1 Colonne

`school_settings."ManagedCycles"` — `text`, NOT NULL, défaut `'Maternelle,Primaire,College,Lycee'`.

Ordre canonique de sérialisation : `Maternelle, Primaire, College, Lycee` (jamais l'ordre de saisie), pour qu'une même sélection donne toujours la même chaîne.

Contraintes :

- au moins un cycle (`CHECK ("ManagedCycles" <> '')`) ;
- jamais de valeur inconnue ni de doublon : garanti par l'application (pas de `CHECK` regex en base — même choix que `WorkingDays`).

### 3.2 Constante et aide pure

`SchoolSettingsDefaults.ManagedCycles = "Maternelle,Primaire,College,Lycee"` et une classe pure `ManagedCycleSet` (Application, à côté de `SchoolWeek`) : `TryParse(IEnumerable<string>)` (refuse vide, inconnu, doublon), `Serialize`, `FromStored`, `ToNames`, `Contains(CycleType)`. Pure, sans accès base, testable seule.

### 3.3 Migration `AddManagedCyclesToSchoolSettings` (réversible)

```sql
ALTER TABLE school_settings ADD COLUMN "ManagedCycles" text NOT NULL DEFAULT 'Maternelle,Primaire,College,Lycee';
-- Reprise : on PRÉSERVE le comportement actuel.
UPDATE school_settings SET "ManagedCycles" = 'Maternelle,Primaire'
 WHERE "ProfileEtablissement" = 'ElementairePrimaire';
```

> **Correction par rapport à la formulation « tous les cycles pour toutes les écoles existantes »** : les écoles qui ont déjà choisi le profil Élémentaire sont *aujourd'hui* restreintes à Maternelle + Primaire par les trois tests `isElementaireProfile`. Leur donner tous les cycles ferait réapparaître Collège/Lycée chez elles — un changement visible. L'objectif « zéro changement » impose donc cette reprise.

`Down` : `DROP COLUMN`. Aucune autre table touchée. `school_settings` est déjà sous RLS (aucune policy à ajouter) et la colonne ne change pas ses `GRANT`.

## 4. Backend (CQRS, `SamaEcole.Application`)

### 4.1 Lecture

`SchoolSettingsDto` gagne `IReadOnlyList<string> ManagedCycles` en **dernier paramètre à défaut** (`= null` → tous les cycles), pour ne casser aucun appelant existant. `GetSchoolSettingsQuery` et `Defaults()` le renseignent.

### 4.2 Écriture — commande dédiée

`SetManagedCyclesCommand(IReadOnlyList<string> Cycles)` → `PUT /api/v1/schools/current/settings/managed-cycles`, **Directeur uniquement** (même patron que `grading-scale` et `establishment-profile` : un endpoint dédié, plutôt qu'un 31ᵉ champ de `UpdateSchoolSettingsCommand`).

Validation (FluentValidation, 422) : liste non vide, valeurs ∈ `CycleType`, sans doublon.

Handler :

1. charge `SchoolSettings` de l'école courante (crée la ligne si absente, comme `ApplyEstablishmentProfile`) ;
2. calcule les cycles **retirés** (ancien ensemble − nouveau) ;
3. pour chaque cycle retiré, compte les classes **vivantes** (`Classroom.Cycle == cycle`, hors supprimées logiquement — le Global Query Filter s'en charge) ;
4. s'il y en a : `BusinessRuleException` **409** code `CYCLE_HAS_CLASSROOMS`, message en français nommant le cycle et le nombre de classes (« Le cycle Collège compte 4 classes. Supprimez-les ou déplacez-les avant de le désactiver. »), `details` = `{ cycle, classroomCount }[]` ;
5. sinon enregistre `ManagedCycleSet.Serialize(...)` et renvoie `SchoolSettingsDto`.

Les classes vivantes incluent les classes sans élève : le Directeur peut les supprimer (suppression logique) avant de décocher. Le contrôle est refait **dans le handler**, pas seulement par l'écran.

Journalisation d'audit : `IAuditableRequest`, comme les autres commandes de réglages.

### 4.3 Onboarding

`SelectProfileCommandHandler` (PR #56) pose `ManagedCycles` en même temps que le profil :

| Profil | Cycles initiaux |
|---|---|
| `Elementaire` | `Maternelle,Primaire` |
| `EnseignementGeneral`, `FrancoArabe`, `InternatDaara`, `ComptabiliteRapports` | tous |

`ApplyEstablishmentProfileCommandHandler` **ne touche pas** à `ManagedCycles` (décision 6).

### 4.4 Ce qui ne change pas

Aucun refus de création de classe, d'inscription ou de session d'examen n'est ajouté côté API (décision 4). `CreateExamDossierCommandHandler.ExpectedCycle` continue de refuser le mismatch de cycle d'un dossier — règle indépendante.

## 5. Frontend (Razor + Alpine)

### 5.1 Store `schoolConfig` (`auth.js`)

Charge `managedCycles` (tableau de noms) avec les autres réglages, expose `isCycleManaged(cycle)` et `managedCycles`. Valeur par défaut **tant que non chargé ou en erreur** : tous les cycles (sûr par défaut : on n'affiche jamais moins que ce que l'école avait). Le test `isElementaireProfile` du store reste disponible tant que d'autres consommateurs en dépendent ; les trois ci-dessous sont migrés.

### 5.2 Les trois consommateurs

| Fichier | Aujourd'hui | Après |
|---|---|---|
| `classrooms.js` › `visibleLevelOptions` | profil Élémentaire → sans Collège/Lycée | niveaux dont le cycle (`Crèche`/`Maternelle` → `Maternelle`) est géré ; le niveau **déjà enregistré** de la classe éditée reste proposé |
| `enrollments.js` › `visibleClassrooms` | profil Élémentaire → classes Maternelle/Primaire | classes dont `cycle` est géré ; la classe d'une inscription existante reste visible |
| `exams.js` › `examTypeOptions` | profil Élémentaire → CFEE seul | CFEE si `Primaire` géré, BFEM si `College`, BAC si `Lycee` (même correspondance que `ExpectedCycle`) ; valeur par défaut = premier type disponible |

### 5.3 Paramètres

Un bloc **« Cycles gérés »** (cases à cocher Maternelle / Crèche, Primaire, Collège, Lycée) dans l'onglet **Modules** de Paramètres, Directeur seulement. En cas de 409 `CYCLE_HAS_CLASSROOMS`, l'écran affiche le message du serveur et **remet la case cochée**. Au moins une case reste cochée (bouton d'enregistrement désactivé sinon, et 422 côté serveur).

## 6. Tests

- **Unitaires** : `ManagedCycleSet` (parse, ordre canonique, vide, inconnu, doublon, aller-retour) ; validateur de `SetManagedCyclesCommand`.
- **Intégration (PostgreSQL réel, rôle applicatif)** : migration — reprise Élémentaire → `Maternelle,Primaire`, autres profils et école sans profil → tous les cycles, `Down`/`Up` rejouable ; handler — décocher un cycle vide réussit, décocher un cycle avec classes vivantes → `CYCLE_HAS_CLASSROOMS` sans rien écrire, une classe supprimée logiquement ne bloque pas, isolation entre écoles ; Onboarding — Élémentaire reçoit 2 cycles, les autres tous ; `ApplyEstablishmentProfile` n'écrase pas un réglage fait à la main.
- **Fonctionnels (API)** : PUT par le Directeur (200), par un autre rôle (403), liste vide (422), 409 avec le détail.
- **JS (`node --test`)** : `isCycleManaged` et valeur par défaut ; les trois consommateurs (niveau existant conservé, cycle retiré masqué, types d'examen) ; blocage de la case au 409.
- **Recette navigateur** (comme pour l'Onboarding) : parcours Directeur Élémentaire / Daara pur / Daara mixte, mobile 390 px. Elle a déjà révélé un bug réel sur l'Onboarding : elle fait partie de la définition de « terminé ».

## 7. Documentation

`docs/Volume_3_DDS.md` (colonne, §5 de `school_settings`), `docs/Volume_4_API_Design.md` (route et codes d'erreur `CYCLE_HAS_CLASSROOMS`), texte de la carte Daara de l'Onboarding si nécessaire, retrait des commentaires devenus faux dans `classrooms.js` / `exams.js` / `enrollments.js`.

## 8. Déroulement et dépendances

1. **Attendre la fusion de la PR #56** : la migration ajoute une colonne sur `school_settings` (conflit certain sur `ApplicationDbContextModelSnapshot.cs` sinon) et l'initialisation par profil se branche dans `SelectProfileCommandHandler`, qui n'existe que dans la #56.
2. Branche `feat/managed-cycles-settings` depuis `main`.
3. Ordre de livraison : (a) `ManagedCycleSet` + tests → (b) migration + tests → (c) commande/handler/DTO + tests → (d) hook Onboarding → (e) store + 3 consommateurs + tests JS → (f) bloc Paramètres → (g) documentation → (h) recette navigateur → suite complète.
4. Une seule PR ; elle ne regroupe ni le masquage d'autres écrans ni le rebranchement de la sidebar sur la souscription.

## 9. Points tranchés (2026-10-06)

1. **Création de classe hors cycles gérés** : l'API reste souple (aucun refus côté serveur) ; l'interface guide l'usage courant. Seule exception côté serveur : la règle 6, au moment de **désactiver** un cycle.
2. **Classes accélérées / passerelle** : rattachement strict au `Classroom.Cycle` actuel, sans cas particulier.
3. **Emplacement du bloc** : onglet **Modules** de Paramètres.
4. **Sidebar / rapports** : Examens officiels, Rapports institutionnels, etc. restent **hors périmètre** de cette PR, pour ne pas étendre la surface de test.
