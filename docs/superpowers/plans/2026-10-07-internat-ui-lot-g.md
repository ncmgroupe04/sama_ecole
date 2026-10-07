# Internat — lot G : IHM Razor / Alpine sur le modèle Pavillon/Lit — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Faire vivre le module Internat dans l'interface sur les routes `/api/v1/boarding/*` (pavillons, affectation, pensionnaires, sorties, pointage, PDF) et poser les gardes de page par module — pour que le lot F puisse ensuite supprimer les routes héritées.

**Architecture :** Razor + Tailwind + Alpine.js, comme les écrans existants (`Views/Internat/*`, `wwwroot/js/*.js`, tests `node --test` dans `src/SamaEcole.Web/tests/js`). L'écran `/internat` devient un conteneur à onglets ; chaque onglet est un composant Alpine dans son propre fichier. **Aucune logique d'autorisation côté client** : l'API (`[RequireModule]`, rôles, masquage de la fiche médicale) reste la seule barrière ; l'IHM reflète ce que l'API renvoie.

**Tech Stack :** Razor, Alpine.js, Tailwind, `window.api` / `window.auth` (auth.js, api.js), tests JS `node --test` + `harness.mjs`, tests fonctionnels xUnit pour les gabarits de page.

**Spec :** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` §8 (Phase 2) — **corrigée par ce plan, voir G-D1** ; §6 (API) ; §12 (la refonte d'écran y est « hors périmètre » : voir G-D3).

## Global Constraints

- Branche `feat/internat-ui`, **empilée sur `feat/internat-pdf-reports` (lot E)**, elle-même sur D, C (#63), B (#62). Découpage en **PR successives** (G1 → G4), chacune verte avant la suivante. Pas de push par moi.
- Lecture : `Directeur`, `Secretariat`, `Surveillant`. Écriture pavillons / chambres / lits : `Directeur`, `Secretariat`. Affectation, fiche, sorties, pointage : les trois rôles. `Enseignant`/`Finance` n'ont pas accès à `/internat` (menu `canView`), l'API répond 403.
- Fiche médicale : **c'est le serveur qui masque** (`medicalNotes: null`). L'IHM n'affiche le champ que si `auth.role` ∈ {Directeur, Surveillant}, et **n'envoie jamais** `medicalNotes` pour un autre rôle (le serveur répond 403 s'il le reçoit).
- Verrouillage optimiste : chaque écriture renvoie son `rowVersion` ; un 409 `CONCURRENCY_CONFLICT` recharge la ligne avec un message clair. Codes métier à traduire : `BED_UNAVAILABLE`, `RESOURCE_IN_USE`, `LEAVE_ALREADY_OPEN`, `LEAVE_ALREADY_RETURNED`, `LEAVE_IN_PROGRESS`, `ATTENDANCE_CONFLICT`, `ACTIVE_ENTITY_CONFLICT`, `ARCHIVED_ENTITY_EXISTS`.
- Conventions des écrans existants : `x-cloak` avant décision, `skeleton` pendant le chargement, `_ErrorBanner`, `<empty-state>`, `<stat-card>`, `<select-field>`, dates via `date-field`, aucune `confirm()` native (modales). Appels HTTP uniquement via `window.api` (jamais `fetch` direct, sauf PDF : voir G-T9).
- Aucune migration. Seule addition serveur : `GET /api/v1/boarding/boardable-students` (G-T6).
- `dotnet build` avant les tests .NET ; jamais deux suites en parallèle ; journaux dans un fichier.

## Décisions à valider

| # | Décision | Pourquoi |
|---|---|---|
| **G-D1** | **Corriger la Phase 2 de la spec.** `/internat`, `/halqa`, `/oustaz`, `/suivi-coranique` sont **toutes** `[PageModule(Internat)]`. Les menus Halqa / Oustaz / Suivi restent conditionnés par `internatEnabled`. | Le code contredit le §8.1 : ces écrans appellent `/api/v1/internat/*`, gardé par `[RequireModule(Internat)]` (`InternatController`). `QuranController` (`/api/v1/quran`, module **Coran**) n'a **aucun consommateur** dans `wwwroot/js`. Appliquer le §8.3-5 à la lettre (menus par `coranEnabled`) masquerait ces écrans à une école Internat-seul alors que leur API répond, et les montrerait à une école Coran-seul dont l'API répond 403. `coranEnabled` est quand même ajouté au store (prêt pour le jour où un écran consomme `/api/v1/quran`). |
| **G-D2** | **Livrer B + C + G2 (pavillons) dans la même fenêtre.** | Le lot B retire la création de salles `Dortoir` de `/infrastructures` (Q1) ; sans l'onglet Pavillons (G2), **plus aucune IHM ne permet de créer un pavillon**. |
| **G-D3** | **Inclure la refonte de l'écran `/internat`**, que la spec classe « hors périmètre » (§12). | Le lot F ne peut pas supprimer `GET /internat/dashboard`, `/internat/students/search` et `POST /internat/assignments/{id}` tant que l'écran les appelle. C'est le seul consommateur (`internat.js`, 161 lignes). |
| **G-D4** | Le lot F ne supprimera **que ces 3 routes Room**, **pas** le préfixe `/api/v1/internat/*`. | Hizb, Halqa, Oustaz, suivi et bulletin coranique y vivent (`halqa.js`, `instructors.js`, `progress-dashboard.js`) et ne sont pas concernés. La formulation « retrait de `/api/v1/internat/*` » de la spec §10 est trompeuse. |
| G-D5 | `enrollments.js` et `students.js` **ne changent pas** dans ce lot. | `POST /enrollments` garde sa forme (adaptateur du lot C) ; aucune de leurs routes n'est supprimée par F. Seules les colonnes `Enrollment.BoardingStatus/RoomId` disparaissent. |
| G-D6 | La **facturation de la pension** n'a pas de chantier propre : elle est faite au lot C (`includeBoardingFee`, ligne ajoutée une seule fois). Le lot G ajoute seulement la case « Ajouter la pension » dans la modale d'affectation. | Éviter de réécrire ce qui est livré et testé. |
| G-D7 | Nouvelle route `GET /api/v1/boarding/boardable-students?term=` (adaptateur de `SearchBoardableStudents`). | La nouvelle API n'a pas de recherche d'élèves affectables : l'écran en a besoin pour l'affectation. |
| G-D8 | La **recette navigateur** n'est pas faisable par les tests JS : elle reste à faire (par l'équipe, ou par moi via le navigateur si vous l'autorisez), comme pour les autres modules. | Les tests `node --test` couvrent la logique pure, pas le rendu. |

## Structure des fichiers

| Fichier | Rôle |
|---|---|
| `Controllers/PageModuleAttribute.cs`, `Filters/PageModuleFilter.cs` (créer) | Déclare le module d'une page, recopié dans `ViewData` |
| `Views/Shared/_Layout.cshtml` (modif) | `<meta name="required-module">` + chargement de la garde |
| `wwwroot/js/module-route-guard.js` (créer) | Décision pure `decide()` + application (redirection / bandeau) |
| `wwwroot/js/auth.js` (modif) | Store `schoolConfig` : `coranEnabled`, `loadFailed` |
| `Views/Internat/Index.cshtml` (refonte) | Conteneur à onglets Occupation / Pavillons / Pensionnaires / Sorties / Pointage |
| `wwwroot/js/internat.js` (refonte) | Onglet Occupation + modale d'affectation (nouvelle API) |
| `wwwroot/js/internat-dormitories.js`, `internat-boarders.js`, `internat-leaves.js`, `internat-attendance.js` (créer) | Un composant Alpine par onglet |
| `wwwroot/js/internat-helpers.js` (créer) | Fonctions pures partagées (taux d'occupation, traduction des codes d'erreur, correspondance accompagnateur) — testables |
| `Application/Boarding/Boarders/SearchBoardableStudents…` + `BoardingController.cs` (modif) | G-T6 |
| `tests/js/*.test.mjs`, `tests/SamaEcole.FunctionalTests/Pages/PageModuleTests.cs` (créer) | Tests |

---

## G1 — Gardes de page par module (Phase 2 corrigée)

### Task 1 : le store `schoolConfig` connaît `coranEnabled` et `loadFailed`

**Files:** Modify `wwwroot/js/auth.js` (≈ lignes 571-660) · Test `tests/js/school-config-modules.test.mjs`

**Interfaces — Produces :** `Alpine.store('schoolConfig').coranEnabled: boolean` (défaut `false`, vrai si `isCoranModuleEnabled === true`) ; `loadFailed: boolean` (défaut `false`, vrai si `GET /schools/current/settings` échoue, remis à `false` au succès) ; `retry()` relance `_load()`.

- [ ] **Step 1 : tests (échouent)** avec `harness.mjs` (modèle : `onboarding-profile.test.mjs`) : réponse `{ isInternatEnabled: true, isCoranModuleEnabled: false }` → `internatEnabled === true`, `coranEnabled === false`, `loadFailed === false` ; `isCoranModuleEnabled: true` → `coranEnabled === true` ; échec réseau → `internatEnabled === false` (sûr par défaut), `coranEnabled === false`, `loadFailed === true` **et `profileEtablissement` reste `undefined`** ; un second `init()` ne relance pas de fetch ; `retry()` après un échec puis un succès → `loadFailed === false`.
- [ ] **Step 2 : lancer** `npm test --prefix src/SamaEcole.Web -- --test-name-pattern "schoolConfig modules"` — échec.
- [ ] **Step 3 : implémenter** les deux champs dans le store et dans `_load()` (succès / `catch`), plus `retry() { this._initPromise = null; this.loadFailed = false; return this.init(); }`.
- [ ] **Step 4 : relancer — PASS.** **Step 5 : commit** `feat(ihm): le store schoolConfig expose coranEnabled et loadFailed`

### Task 2 : `[PageModule]` et la balise `required-module`

**Files:** Create `Controllers/PageModuleAttribute.cs`, `Filters/PageModuleFilter.cs` · Modify `Controllers/PagesController.cs`, `Views/Shared/_Layout.cshtml`, `Program.cs` (enregistrement du filtre) · Test `tests/SamaEcole.FunctionalTests/Pages/PageModuleTests.cs`

**Interfaces — Produces :** `[AttributeUsage(AttributeTargets.Method)] public sealed class PageModuleAttribute(SchoolModule module) : Attribute { public SchoolModule Module { get; } }` ; le filtre pose `ViewData["RequiredModule"] = module.ToString()` ; `_Layout` émet `<meta name="required-module" content="Internat">` quand la valeur existe.

- [ ] **Step 1 : tests (échouent)** : `GET /internat`, `/halqa`, `/oustaz`, `/suivi-coranique` (HTML anonyme) contiennent `<meta name="required-module" content="Internat">` ; `GET /login`, `/eleves`, `/classes` n'ont **aucune** balise `required-module` . **Test explicite** (pas une réflexion générique, qui ne verrouillerait rien : un contrôleur `Coran` existe) : les 4 actions `Internat`, `Halqa`, `Instructors` et `QuranProgress` de `PagesController` portent **exactement** `[PageModule(SchoolModule.Internat)]`, et le test échoue avec un message citant G-D1 si l'une porte `Coran`. Un second test vérifie que les routes réellement appelées par ces écrans (`/api/v1/internat/…`) relèvent du contrôleur gardé par `Internat` (lecture de l'attribut `[RequireModule]` de `InternatController`).
- [ ] **Step 2 : lancer — échec.** **Step 3 : implémenter** et poser `[PageModule(SchoolModule.Internat)]` sur les 4 actions (les commentaires de `PagesController` disent déjà « `[RequireModule(Internat)]` »).
- [ ] **Step 4 : relancer — PASS.** **Step 5 : commit** `feat(ihm): déclaration du module requis d'une page (PageModule)`

### Task 3 : la garde de page `module-route-guard.js`

**Files:** Create `wwwroot/js/module-route-guard.js` · Modify `Views/Shared/_Layout.cshtml` · Test `tests/js/module-route-guard.test.mjs`

**Interfaces — Produces :** `window.moduleRouteGuard.decide({ requiredModule, store }) → 'show' | 'redirect' | 'unverified'` — `'show'` si aucune meta ou module activé ; `'redirect'` si le module est désactivé **et** `!store.loadFailed` ; `'unverified'` si `store.loadFailed` (jamais de redirection sur une coupure réseau). `window.moduleRouteGuard.apply()` attend `schoolConfig.init()`, applique la décision (`location.replace(window.auth.landingPath())` + toast « Ce module n'est pas activé pour votre établissement », ou bandeau « Impossible de vérifier les modules activés — Réessayer » relié à `retry()`), puis retire `x-cloak` du contenu.

- [ ] **Step 1 : tests (échouent)** de `decide` : sans meta → `show` ; `Internat` + `internatEnabled: true` → `show` ; `Internat` + `internatEnabled: false` + `loadFailed: false` → `redirect` ; **`internatEnabled: false` + `loadFailed: true` → `unverified`** (le cas qui éjecterait à tort un Directeur de Daara) ; `Coran` + `coranEnabled` ; module inconnu → `show` (ne bloque jamais sur une valeur inattendue).
- [ ] **Step 2 : lancer — échec.** **Step 3 : implémenter** `decide` (pure) et `apply`. **Step 4 : relancer — PASS.**
- [ ] **Step 5 : commit** `feat(ihm): garde de page par module (redirection, jamais sur erreur réseau)`

### Task 4 : audit de navigation et correction de la spec

**Files:** Modify `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` (§8.1 et §8.3-5, avec la mention « corrigé le 07/10/2026 : voir lot G, G-D1 ») · Test `tests/js/regression-guards.test.mjs` (ajout)

- [ ] **Step 1 : test (échoue si le code dérive)** dans `regression-guards.test.mjs` : lire `Views/Shared/_Layout.cshtml` et `_QuickNav.cshtml`, vérifier que les liens `/internat`, `/halqa`, `/oustaz`, `/suivi-coranique` sont tous conditionnés par `internatEnabled` et par **aucun** `coranEnabled`.
- [ ] **Step 2 : lancer** — doit **passer** (c'est un verrou de non-régression de G-D1). **Step 3 :** corriger la spec. **Step 4 : commit** `docs(internat): correction de la Phase 2 (les écrans Halqa/Oustaz/Suivi relèvent du module Internat)`

---

## G2 — Onglet Pavillons (création et gestion)

### Task 5 : onglet « Pavillons »

**Files:** Create `wwwroot/js/internat-dormitories.js`, `wwwroot/js/internat-helpers.js` · Modify `Views/Internat/Index.cshtml` (conteneur à onglets, premier onglet = Occupation inchangé pour l'instant) · Test `tests/js/internat-helpers.test.mjs`, `tests/js/internat-dormitories.test.mjs`

**Contenu :** liste (`GET /boarding/dormitories` : nom, genre, surveillant, `RoomCount`, `Capacity`, `OccupiedBeds`, `MaintenanceBeds`, taux) ; création / modification (nom, genre **Garçons / Filles** — `Mixte` n'est jamais proposé, Q2 —, surveillant nom + téléphone, notes) ; détail d'un pavillon (`GET /boarding/dormitories/{id}` : chambres et lits avec occupant) ; ajout d'une chambre avec nombre de lits (`POST /boarding/rooms`), ajout d'un lit, bascule **Maintenance ↔ Disponible** (`PUT /beds/{id}/status`), suppression (modale) et **corbeille** avec restauration. Boutons d'écriture visibles pour `Directeur`/`Secretariat` seulement (`canView`), lecture pour `Surveillant`.

**Interfaces — Produces (helpers purs) :** `occupancyPercent(occupied, capacity)`, `occupancyColor(occupied, capacity)` (mêmes seuils que l'écran actuel : vide / < 80 % / ≥ 80 % / complet), `errorMessage(err)` (traduit les codes de la section Contraintes : `RESOURCE_IN_USE` → « Ce pavillon contient encore des chambres. », etc., repli sur `window.api.toMessage`), `genderLabel(g)`.

- [ ] **Step 1 : tests (échouent)** : helpers (seuils aux bornes 0 / 79 % / 80 % / 100 % et capacité 0 sans division par zéro ; chaque code d'erreur → message) ; composant : `Mixte` absent des genres proposés ; création → `POST /boarding/dormitories` avec le bon corps puis rechargement ; suppression d'un pavillon non vide → message `RESOURCE_IN_USE` affiché, rien n'est supprimé ; restauration → `POST …/restore` ; un 409 `CONCURRENCY_CONFLICT` recharge la ligne.
- [ ] **Step 2 : lancer — échec.** **Step 3 : implémenter.** **Step 4 : relancer — PASS** + suite JS complète (`npm test`).
- [ ] **Step 5 : commit** `feat(ihm): onglet Pavillons (création, chambres, lits, corbeille)`

---

## G3 — Occupation et affectation sur la nouvelle API

### Task 6 : recherche d'élèves affectables dans `/api/v1/boarding`

**Files:** Modify `Controllers/BoardingController.cs`, `Application/Boarding/Boarders/…` (requête mince déléguant à `SearchBoardableStudents`) · Test `tests/SamaEcole.FunctionalTests/Boarding/BoardableStudentsEndpointsTests.cs`

**Interfaces — Produces :** `GET /api/v1/boarding/boardable-students?term=` → liste `{ enrollmentId, studentId, fullName, matricule, classroomName, regime?, boarderId?, bedId?, rowVersion }` (élèves de l'année active, 2 caractères au moins) ; rôles de lecture ; module Internat.

- [ ] **Step 1 : tests (échouent)** : recherche par nom et par matricule ; moins de 2 caractères → liste vide ; un élève déjà logé porte `boarderId`/`bedId`/`rowVersion` (jeton du **séjour**, pas de l'inscription) ; élève d'une autre école jamais renvoyé ; `Enseignant` → 403 ; module désactivé → 403.
- [ ] **Step 2-4 :** échec, implémentation, PASS (+ non-régression de `GET /internat/students/search`). **Step 5 : commit** `feat(internat): recherche des élèves affectables sous /api/v1/boarding`

### Task 7 : onglet « Occupation » et modale d'affectation

**Files:** Modify `wwwroot/js/internat.js`, `Views/Internat/Index.cshtml` · Test `tests/js/internat-occupation.test.mjs`

**Contenu :** KPI (occupation, internes, demi-pensionnaires, chambres complètes) calculés depuis `GET /boarding/dormitories` + détails ; cartes par pavillon → chambres → lits (occupant, maintenance) ; modale d'affectation : recherche (debounce 300 ms, invalidation d'une sélection périmée — comportement actuel conservé), **choix du lit** (liste des lits libres du pavillon, au lieu d'une chambre), régime Interne / Demi-pensionnaire, case « Ajouter la pension » (`includeBoardingFee`, G-D6), transfert (envoie le `rowVersion` du séjour) et libération (`DELETE /unassign-bed/{id}?rowVersion=`). **Plus aucun appel à `/internat/dashboard`, `/internat/students/search` ni `/internat/assignments`.**

- [ ] **Step 1 : tests (échouent)** : l'URL de chaque appel (jamais `/internat/…`) ; sélection périmée invalidée ; confirmer envoie `{ enrollmentId, regime, bedId, includeBoardingFee, rowVersion }` ; un transfert envoie le `rowVersion` du séjour ; `BED_UNAVAILABLE` → message « Ce lit vient d'être attribué » et rechargement ; demi-pensionnaire → `bedId` absent ; **garde-fou de dérive** : un test lit `internat.js` et échoue s'il contient `/internat/dashboard`, `/internat/students/search` ou `/internat/assignments`.
- [ ] **Step 2-4 :** échec, implémentation, PASS + suite JS complète. **Step 5 : commit** `feat(ihm): occupation et affectation sur /api/v1/boarding`

---

## G4 — Pensionnaires, sorties, pointage et PDF

### Task 8 : onglet « Pensionnaires » et fiche

**Files:** Create `wwwroot/js/internat-boarders.js` · Test `tests/js/internat-boarders.test.mjs`

**Contenu :** liste paginée (`GET /boarding/boarders` : filtres pavillon, chambre, régime, statut `active`/`ended`/`awaitingBed`/`onLeave`, recherche) ; panneau fiche (`GET /boarders/{id}`) : identité, hébergement, contact d'urgence, personnes habilitées (≤ 10), **fiche médicale** (champ affiché seulement pour Directeur / Surveillant, valeur `null` ⇒ « non renseignée »), sorties récentes ; modification du profil (`PUT …/profile`, `rowVersion`) **sans `medicalNotes` dans le corps** pour les autres rôles ; bouton **« Fiche PDF »**.

- [ ] **Step 1 : tests (échouent)** : le champ médical est absent du DOM pour `Secretariat` et le corps du `PUT` ne contient pas `medicalNotes` ; présent pour `Directeur` et `Surveillant` ; `medicalNotes: null` de l'API ⇒ libellé « non renseignée » (jamais le mot « null ») ; 403 du serveur ⇒ message dédié ; pagination ; filtre `onLeave`.
- [ ] **Step 2-4 :** échec, implémentation, PASS. **Step 5 : commit** `feat(ihm): onglet Pensionnaires, fiche et profil (fiche médicale par rôle)`

### Task 9 : PDF (fiche et registre)

**Files:** Modify `internat-helpers.js`, `internat-boarders.js` · Test `tests/js/internat-pdf.test.mjs`

Les routes PDF exigent l'en-tête `Authorization` : un lien `<a href>` ne peut pas les ouvrir. **Réutiliser le mécanisme du bulletin coranique** (`progress-dashboard.js:52`, `hizb-report/pdf`) — le lire d'abord et en extraire un helper commun `openPdf(url)` plutôt que de le copier.

- [ ] **Step 1 : tests (échouent)** : `openPdf` appelle l'URL attendue avec le jeton, ouvre le blob dans un nouvel onglet, révoque l'URL objet ; sur 404/403/422 affiche le message de l'API (jamais un onglet vide) ; le registre envoie `?date=` au format `yyyy-MM-dd`.
- [ ] **Step 2-4 :** échec, implémentation, PASS. **Step 5 : commit** `feat(ihm): impression de la fiche pensionnaire et du registre du pavillon`

### Task 10 : onglet « Sorties »

**Files:** Create `wwwroot/js/internat-leaves.js` · Test `tests/js/internat-leaves.test.mjs`

**Contenu :** registre (`GET /boarding/leaves` : filtres statut Pending/Active/Overdue/Returned, pavillon, période, « accompagnateur non habilité ») ; déclaration (pensionnaire, dates, motif Week-end/Santé/Famille/Autre, accompagnateur, détail) avec **avertissement visuel (jamais un blocage, Q5)** quand l'accompagnateur ne figure pas parmi les personnes habilitées — règle identique à `CompanionMatcher` (espaces et casse ignorés, nom complet) ; pastille « Non habilité » dans le registre ; retour (`PUT …/return`, date du jour par défaut, `rowVersion`) ; statuts affichés en français (À venir / En cours / En retard / Rentré).

- [ ] **Step 1 : tests (échouent)** : **parité de la correspondance** avec les cas de `CompanionMatcherTests` (`"  fatou   DIOP "` ↔ `Fatou Diop` oui ; `Fatou` ↔ `Fatou Diop` non ; liste vide non) ; la déclaration reste possible malgré l'avertissement ; `LEAVE_ALREADY_OPEN`, `LEAVE_ALREADY_RETURNED` traduits ; libellés de statut ; la date de retour par défaut est celle du jour.
- [ ] **Step 2-4 :** échec, implémentation, PASS. **Step 5 : commit** `feat(ihm): onglet Sorties (déclaration, retour, accompagnateur non habilité)`

### Task 11 : onglet « Pointage »

**Files:** Create `wwwroot/js/internat-attendance.js` · Test `tests/js/internat-attendance.test.mjs`

**Contenu :** choix du pavillon et de la nuit (aujourd'hui par défaut, jamais future) ; feuille d'appel (`GET /boarding/attendance`) triée chambre → lit, absents en permission pré-remplis et **verrouillés sur « absent »** ; saisie présent / absent + note ; envoi collectif (`POST /boarding/attendance`) et **affichage du résultat ligne par ligne** (`NOT_IN_DORMITORY`, `DUPLICATE_ENTRY`, `BOARDER_ON_LEAVE` en messages lisibles ; les lignes enregistrées ne sont pas renvoyées) ; `ATTENDANCE_CONFLICT` ⇒ rechargement de la feuille ; bouton **« Registre PDF »** (tâche 9).

- [ ] **Step 1 : tests (échouent)** : un pensionnaire `onLeave` ne peut pas être coché présent ; seules les lignes **modifiées** partent ; un résultat mixte affiche les rejets sans masquer les succès ; date future refusée avant l'appel ; `422` de date hors année affiché.
- [ ] **Step 2-4 :** échec, implémentation, PASS. **Step 5 : commit** `feat(ihm): onglet Pointage de nuit (feuille d'appel et résultat par ligne)`

---

## Task 12 : documentation, suite complète, recette, PR

- [ ] **Step 1 : docs.** `ACTIVE_CONTEXT.md` (entrée lot G, correction de la Phase 2, « Reste : lot F — uniquement les 3 routes Room + les colonnes »), `docs/Volume_5_UIUX_Design.md` (écran Internat à onglets), `Volume_4` (route `boardable-students`), centre d'aide si l'écran y est décrit. Aucune migration.
- [ ] **Step 2 : suite complète SÉQUENTIELLE** (.NET : unitaires, fonctionnels, intégration ; `npm test`), journaux dans un fichier ; lancer aussi les classes entières voisines. Rappel : l'intégration dure ~1 h, plus de 2 h si la RAM libre est basse.
- [ ] **Step 3 : recette navigateur** (G-D8) sur les 5 onglets avec les 3 rôles (Directeur, Secrétariat, Surveillant) + un Enseignant (accès refusé) + module désactivé (redirection) + coupure réseau simulée (bandeau, pas de redirection). Liste de contrôle fournie dans la PR.
- [ ] **Step 4 : PR(s)** en brouillon, une par groupe G1 → G4, base = branche précédente de la chaîne ; description avec décisions, résultats réels, « aucune migration ». Push à la charge de l'équipe.

---

## Auto-revue

- **Couverture :** gardes de page (spec §8.3 corrigé) → G1 ; création des pavillons (sinon plus d'IHM après le lot B) → G2 ; affectation / occupation sur la nouvelle API (prérequis du lot F) → G3 ; fiche, sorties, pointage, PDF (lots C, D, E exposés) → G4 ; fiche médicale par rôle → T8 ; Q5 accompagnateur → T10.
- **Hors lot :** suppression des routes et colonnes héritées (F) ; portail parents, SMS, rapports d'occupation historiques (spec §12) ; screens `/halqa`, `/oustaz`, `/suivi-coranique` (inchangés).
- **Risque principal :** la taille (≈ 5 écrans) — d'où le découpage en PR successives et la recette navigateur. Le lot **ne se termine pas** à « tests verts » : le rendu doit être vu.
- **Cohérence des noms :** `window.moduleRouteGuard.decide/apply`, `schoolConfig.coranEnabled/loadFailed/retry`, `PageModuleAttribute`, `internat-helpers.js` (`occupancyPercent`, `occupancyColor`, `errorMessage`, `genderLabel`, `openPdf`), onglets `internat-dormitories|boarders|leaves|attendance.js` — identiques dans toutes les tâches.
