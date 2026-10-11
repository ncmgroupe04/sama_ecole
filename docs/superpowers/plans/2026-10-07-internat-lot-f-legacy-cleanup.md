# Internat — lot F : retrait de l'ancien modèle (routes, colonnes) — Plan de cadrage

> **STATUT : PLAN À VALIDER. Aucune ligne de code n'est écrite.** Branche `feat/internat-legacy-cleanup` (worktree `sama_ecole-internat-cleanup`), empilée sur `feat/internat-ui` (G4 qualifié, `707fa8dc`, puis `8d3e64ab` pour une correction de documentation). Rien n'est poussé.

**Goal :** supprimer ce que le modèle Pavillon/Lit a remplacé : les 3 routes `Room` de `/api/v1/internat/*` et leurs handlers, puis les colonnes `enrollments.BoardingStatus` et `enrollments.RoomId` (avec leur FK composite et leur index), sans casser l'inscription, la fiche élève ni les écrans Halqa/Oustaz/Suivi.

**Spec :** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` §4.3 (migration 2, « contract ») et §10 (lot F). **Ce plan corrige et précise deux points de la spec**, voir F-D2 et F-D5.

---

## 1. État vérifié dans le code (pas supposé)

| Constat | Preuve | Conséquence |
|---|---|---|
| **`enrollments.js` appelle encore `GET /internat/dashboard`** (`loadAvailableRooms`, sélecteur de chambre du formulaire d'inscription). | `enrollments.js:268` | **Bloquant.** Supprimer la route casserait le formulaire d'inscription pour tout élève interne. Mon affirmation « plus aucun écran n'appelle les 3 routes » (PR G3, `ACTIVE_CONTEXT.md`) était **fausse** ; le test de dérive de G3 ne scannait que 4 fichiers `internat*.js`. Corrigée dans `ACTIVE_CONTEXT.md` (`8d3e64ab`) et dans la description de la PR G3. |
| Seul consommateur des 2 autres routes (`students/search`, `assignments/{id}`) : l'ancien écran, déjà remplacé. | grep `wwwroot/js` + `Views` | Aucun blocage. |
| `FindBoardableStudentsQuery` (G3) est **indépendante** de `SearchBoardableStudentsQuery`. | grep | `SearchBoardableStudents` se supprime sans précaution. (La PR G3 disait « délègue » : corrigé.) |
| Dans `src`, la **propriété** `Enrollment.BoardingStatus`/`RoomId` n'est référencée que par `EnrollmentConfiguration` (mappage). Tout le reste utilise l'**enum** `BoardingStatus` comme vocabulaire de requête/réponse. | grep, `[Obsolete]` + CS0618 | La suppression des propriétés est petite côté `src` ; l'enum **reste** (F-D3). |
| Tests qui posent ces propriétés : 6 fichiers, ~16 lignes (`CreateEnrollmentCommandValidatorTests`, `GetInternatDashboardQueryTests`, `BoardingReconciliationTests`, `EnrollmentBoardingTests`, `BoarderQueriesTests`, `BoardingBackfillMigrationTests`). | grep | À réécrire en SQL brut ou à supprimer avec leur sujet. |
| `reset_school_data` et `delete_school_year` **ne référencent pas** les colonnes héritées ; la migration `AddBoardingToPurges` (lot A) inscrit déjà les six tables d'internat dans les deux fonctions. | `20261006230355_AddBoardingToPurges.cs`, grep des migrations | **Aucune modification de purge n'est nécessaire** (F-D6). |
| Retirer un mappage EF **sans** migration fait diverger le modèle de la base (`PendingModelChangesWarning`, ~850 tests en échec). | pièges notés aux lots A/B | Conditionne la forme du découpage (F-D1). |
| Depuis la migration de réconciliation (lot C), **plus rien n'écrit ni ne lit** ces colonnes : elles sont un instantané figé. | décision D8 du lot C | Les supprimer détruit la seule trace de l'état de l'ancien écran (F-D4). |

## 2. Décisions à valider

| # | Décision proposée | Pourquoi / alternative |
|---|---|---|
| **F-D1** | **Trois PR empilées : F0 (migrer le formulaire d'inscription) → F1 (retrait du code, SANS migration) → F2 (migration `DropLegacyBoardingColumns`).** F1 retire les propriétés du CLR mais garde les colonnes **mappées en propriétés fantômes (shadow)** : le modèle EF reste identique, aucune migration, aucun `PendingModelChangesWarning`. F2, très petite, supprime les colonnes **après une livraison stable de F1**. | Seule façon d'avoir un retour arrière sans perte entre « le code n'utilise plus les colonnes » et « les colonnes n'existent plus ». *Alternative :* une seule PR F1+F2 (moins de livraisons) — acceptable **si** vous acceptez que le retour arrière ne restitue plus qu'une dérivation approximative des colonnes (F-D4). |
| **F-D2** | **F0 : ajouter `GET /api/v1/boarding/available-rooms`** (une requête : chambres ayant au moins un lit libre, avec pavillon, genre, places libres) et y brancher `loadAvailableRooms`. Le formulaire continue d'envoyer `boardingStatus` + `roomId` à `POST /enrollments` (G-D5 inchangé : l'adaptateur du lot C choisit le lit). | *Alternative :* appeler `/boarding/dormitories` puis `/boarding/dormitories/{id}` pour chaque pavillon (0 route ajoutée, mais N+1 appels à l'ouverture du formulaire). **Cela corrige la spec**, qui laissait l'écran d'inscription hors du périmètre de la migration d'écran. |
| **F-D3** | **L'enum `BoardingStatus` et les champs `boardingStatus`/`roomId` des requêtes et DTO restent** (`POST /enrollments`, `StudentIdentityDto`, badge de la fiche élève). Seules disparaissent les **propriétés d'entité** et les colonnes. | C'est le contrat d'API de l'inscription et de la fiche élève (compat. Volume 4 §31.2, G-D5). Le retirer est un changement d'API, hors lot. |
| **F-D4** | **Pas de table d'archive** (elle devrait porter RLS, tenant et purges pour ne contenir que deux colonnes figées). À la place : **export des deux colonnes avant F2** (requête fournie dans le runbook de la PR) et **`Down()` qui re-dérive** `BoardingStatus`/`RoomId` depuis les séjours actifs (`boarding_enrollments` → lit → chambre) : approximation documentée, pas une restitution exacte. | Une table de plus à protéger pour un usage d'archive unique est disproportionnée. *Si vous voulez une garantie de restitution exacte :* table d'archive tenant-isolée supprimée au lot suivant — dites-le. |
| **F-D5** | **Condition de lancement de F2 : remplacer la « requête de divergence à zéro » de la spec §4.3** (elle ne veut plus rien dire : les colonnes sont figées depuis le lot C). Porte de sécurité proposée, à zéro en préproduction ET en production avant F2 : **inscriptions de l'année active avec `BoardingStatus <> 'Externe'`, non annulées, sans séjour actif correspondant**. Un résultat non nul signale soit une réconciliation manquée, soit une **ancienne instance qui écrit encore les colonnes**. | Détecte exactement le risque réel. |
| **F-D6** | **Purges : aucun changement.** On ajoute seulement un test qui exécute `reset_school_data` et `delete_school_year` **après F2** avec des séjours, sorties et pointages (les tests existants tournent déjà sur la chaîne de migrations complète). | La demande « adapter les purges » est déjà satisfaite par le lot A ; les purges ne touchent pas aux colonnes. |
| **F-D7** | **`RoomType.Dortoir` reste dans l'enum**, les lignes `rooms` de type Dortoir aussi (spec §4.3). | Anciennes données et sauvegardes. |
| **F-D8** | **Périmètre des routes : seulement `dashboard`, `students/search`, `assignments/{id}`.** Le reste de `/api/v1/internat/*` (instructeurs, Halqa, Hizb, suivi, bulletin) est **conservé** (G-D4). | Hizb/Halqa/Oustaz n'ont rien à voir avec `Room`. |
| **F-D9** | **Prérequis de déploiement de F2 : toutes les instances sont en F1 ou plus.** Une instance antérieure mappe encore les colonnes : après F2, **chacune de ses requêtes sur `enrollments` échouerait** (colonne absente). Même discipline que le lot C (arrêt des anciennes instances, pas de déploiement progressif sur F2). | Sans cela, F2 provoque une panne générale de l'inscription. |

## 3. Ordre de livraison

1. **B + C + G (G1 à G4)** en production, **ensemble** (décision G-D2). Ce plan ne change rien à ce prérequis.
2. **F0** (formulaire d'inscription + `available-rooms`) : peut partir avec G4 ou juste après.
3. **F1** (retrait des 3 routes et des propriétés) : une livraison, sans migration.
4. **Observation** d'une livraison complète en production (la porte F-D5 à zéro).
5. **F2** (migration de suppression), toutes instances arrêtées/relevées (F-D9).

## 4. Tâches (TDD ; un commit par tâche ; build avant chaque suite ; suites séquentielles)

### F0 — migrer le dernier consommateur

**Task 1 — Test de dérive élargi (rouge d'abord).** `src/SamaEcole.Web/tests/js/` : un test lit **tous** les `wwwroot/js/*.js` (commentaires retirés) et échoue s'il contient `/internat/dashboard`, `/internat/students/search` ou `/internat/assignments`. *Attendu : ROUGE sur `enrollments.js`.* (Remplace la liste de 4 fichiers de G3.)

**Task 2 — `GET /api/v1/boarding/available-rooms`.** Application : `ListAvailableRoomsQuery` (lecture seule, tenant courant, module Internat, rôles de lecture). Réponse : `{ roomId, roomName, dormitoryId, dormitoryName, gender, freeBeds }`, chambres avec ≥ 1 lit `Available` (ni occupé, ni maintenance, ni supprimé), pavillons et chambres supprimés exclus, tri pavillon → chambre. Tests d'intégration : lits libres comptés correctement, lit en maintenance exclu, chambre pleine exclue, pavillon supprimé exclu, **autre école jamais renvoyée**. Tests fonctionnels : 403 pour Enseignant/Finance, 403 `MODULE_DISABLED`, 200 pour les trois rôles de lecture.

**Task 3 — `enrollments.js`.** `loadAvailableRooms` appelle la nouvelle route ; le libellé du sélecteur (`Views/Enrollments/Index.cshtml:343`) est adapté (`dormitoryName` à la place de `buildingName`, `freeBeds` à la place de `capacity - occupantsCount`). Tests JS : URL appelée, filtrage, échec = sélecteur vide **avec** le `silence-volontaire` justifié conservé. Task 1 repasse au vert. *Le payload envoyé à `POST /enrollments` ne change pas.*

**Task 4 — Suites** (unitaires, JS, fonctionnels, **intégration complète car une requête Application est ajoutée**), `ACTIVE_CONTEXT.md`, description de PR F0.

### F1 — retrait du code (aucune migration)

**Task 5 — Retirer les 3 routes.** `InternatController` : `dashboard`, `students/search`, `assignments/{id}` et `ChangeBoardingAssignmentRequest`. Application : `GetInternatDashboard`, `SearchBoardableStudents`, `ChangeBoardingAssignment`. Tests supprimés avec leur sujet : `GetInternatDashboardQueryTests`, `SearchBoardableStudentsQueryTests`, `ChangeBoardingAssignmentTests` — **après avoir relevé ce qu'ils couvrent que les tests `Boarding/*` ne couvrent pas** (frais de pension, transfert, libération : à reporter, pas à perdre). Test fonctionnel : les 3 routes répondent 404 ; `instructors`, `progress-dashboard`, `my-halqa`, `hizb-*` répondent toujours. `regression-guards` (« aucune route appelée n'est absente du serveur ») reste vert.

**Task 6 — Propriétés fantômes.** `Enrollment.BoardingStatus`/`RoomId` retirées du CLR ; `EnrollmentConfiguration` les déclare en **shadow** (`Property<string>("BoardingStatus")` avec défaut `Externe`, `Property<Guid?>("RoomId")`, FK composite et index inchangés). *Test d'abord :* un test de non-dérive (le modèle courant == l'instantané de migration) vert avant et après ; `LegacyBoardingColumnsGuardTests` remplacé par un test qui affirme l'**absence** des propriétés CLR. `#pragma warning disable CS0618` supprimés. Tests concernés réécrits en SQL brut (`BoardingReconciliationTests`, `BoardingBackfillMigrationTests` : ils testent des migrations déjà appliquées et lisent les colonnes via SQL).

**Task 7 — Docs et suites.** `Volume_4_API_Design.md` §31.2 (routes retirées), `ACTIVE_CONTEXT.md`, suite complète **dont intégration** (le modèle de persistance est touché), description de PR F1.

### F2 — suppression des colonnes (migration)

**Task 8 — Porte de sécurité.** Requête F-D5 versionnée dans le runbook de la PR + test d'intégration qui prouve qu'elle compte bien un cas piégé (inscription `Interne` sans séjour) et zéro sur un jeu cohérent.

**Task 9 — Migration `DropLegacyBoardingColumns`.** Générée (`dotnet ef migrations add`, `ConnectionStrings__Migrations` factice), jamais écrite à la main côté modèle : supprime la FK composite, l'index `(SchoolId, RoomId)` puis les deux colonnes ; `Down()` recrée colonnes, FK et index et **re-dérive** les valeurs (F-D4). Les propriétés fantômes disparaissent de `EnrollmentConfiguration`. Tests d'intégration : migration appliquée sur une base avec séjours ; `Down()` puis `Up()` rejouables ; **`reset_school_data` et `delete_school_year` réussissent avec séjours, sorties et pointages** (F-D6).

**Task 10 — Suites complètes + description de PR F2** avec prérequis de déploiement (F-D9) et runbook (export des deux colonnes, porte F-D5, arrêt des anciennes instances).

## 5. Risques

- **Panne de l'inscription** si F2 est appliqué alors qu'une ancienne instance tourne (F-D9) : tenue par le prérequis de déploiement, pas par du code.
- **Perte de couverture** à la suppression des tests des 3 routes : atténuée par le relevé de la Task 5.
- **Mes propres affirmations** : le lot G a laissé passer un consommateur. Task 1 ferme la classe d'erreur (scan de tous les fichiers). La recette navigateur du formulaire d'inscription (élève interne) est **obligatoire** pour F0, comme pour G.
- **Durée** : l'intégration dure ~1 h (2 h 30 si la RAM libre est basse), à lancer seule, jamais pendant une compilation.

## 6. Hors lot

Portail parents, SMS, rapports d'occupation historiques (spec §12) ; retrait de `RoomType.Dortoir` ; renommage de l'enum `BoardingStatus` ; recette navigateur de G1 à G4 (checklists dans leurs PR).
