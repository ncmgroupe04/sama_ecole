# Écran Coran/Franco-Arabe (Phase 3) — Spécification

**Date :** 29/09/2026
**Statut :** Validé en brainstorming, en attente de plan d'implémentation.
**Périmètre :** Construire l'écran `/coran` — saisie et consultation de `QuranProgress` (suivi de
mémorisation) et `QuranEvaluation` (évaluation orale) — sur l'API déjà livrée en Phases 1-2
(`docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md` et
`…-franco-arabic-cqrs-api-design.md`, routes dans `openapi.yaml` sous `/api/v1/quran`). Aucun
changement d'API, aucune migration : ce lot est du frontend pur, sur le modèle de l'écran Internat
(`docs/superpowers/specs/2026-09-18-module-internat-design.md`).

## 1. Contexte

Le module Coran/Franco-Arabe suit la même trajectoire que l'Internat : réglage réservé
(`SchoolSettings.IsCoranModuleEnabled`) posé en Phase 1, CQRS/API construits en Phase 2, écran dans
un lot séparé — celui-ci. `ACTIVE_CONTEXT.md` l'a explicitement noté comme reste-à-faire.

**Correction d'une fausse piste :** l'intégration de `Subject.SectionType`/bilinguisme au bulletin,
un temps crue manquante, est en réalité déjà livrée (`GetReportCardPdfQuery.cs`, commit `bd43d593`
et suivants) : `ReportCardDataService.BuildAsync` calcule déjà `isBilingualArabic` depuis
`SchoolSettings.IsCoranModuleEnabled` et résout `Subject.NameAr` en conséquence.
`Subject.SectionType` lui-même reste intentionnellement décoratif (commentaire `Subject.cs` §pré-
existant) — aucune action sur ce point dans ce lot.

Aucun sous-rôle « Enseignant Coran » n'existe : mêmes rôles que Notes (`GradesController`), déjà
actés en Phase 2 (décision #1/#2 de la spec CQRS) — pas de garde fine par affectation
enseignant↔matière.

## 2. Décisions actées (brainstorming du 29/09/2026)

| # | Question | Décision |
|---|---|---|
| 1 | Structure de l'écran | Un seul écran `/coran`, sélecteur de classe, deux onglets : « Suivi de mémorisation » (QuranProgress) et « Évaluations orales » (QuranEvaluation). Pas de dimension Matière/Période — le suivi coranique n'est pas rattaché à un trimestre. |
| 2 | Présentation par élève | Une ligne par élève de la classe (comme `StudentGradeRowDto`) : nom, matricule, résumé de la dernière observation, bouton d'expansion vers l'historique complet, bouton « + Ajouter ». |
| 3 | Saisie/correction | Modale par élève (Juz/Hizb/Sourate/Statut/Notes ou Date/Erreurs/Note), plutôt qu'une édition inline — les champs de `QuranProgress` ne tiennent pas dans une cellule de tableau. Correction : même modale pré-remplie, `PUT` avec le `RowVersion` lu à l'ouverture de l'historique ; 409 affiché via `window.api.toMessage()`, comme tous les autres écrans. |
| 4 | Champs immuables en correction | Juz/Hizb/Sourate/Élève (Progress) et Élève (Evaluation) restent en lecture seule dans la modale de correction — reflet strict de la Phase 2 (décision #8 de la spec CQRS), aucune règle nouvelle. |
| 5 | Rôles | Écriture (ajout/correction) : Directeur + Enseignant. Lecture seule : + Secrétariat. Identique à la garde déjà posée côté API — aucune garde supplémentaire côté client au-delà du masquage des boutons d'action. |
| 6 | Navigation | Nouvelle entrée « Coran » dans `sidebarNav()`, masquée par défaut comme Internat (pas affichée-puis-cachée comme Pédagogie/Finance), visible si `IsCoranModuleEnabled === true` **ET** rôle courant ∈ {Directeur, Enseignant, Secretariat}. |
| 7 | Icône | `book` (sprite Fluent existant, `_IconSprite.cshtml`) — libre, pas encore utilisée. Pas de nouvelle icône ajoutée au sprite pour ce lot. |

## 3. Frontend

### 3.1 Route et navigation

- `PagesController` : nouvelle action `[HttpGet("/coran")] public IActionResult Coran() => View("~/Views/Coran/Index.cshtml");`, sur le modèle exact de `Internat()`.
- `auth.js`, store `schoolConfig` : nouvelle propriété `coranEnabled` (lue depuis `SchoolSettings.IsCoranModuleEnabled` au même endroit que `internatEnabled`, même logique « masqué par défaut »), et un getter `coranEnabled` exposé au composant racine comme les autres (`get coranEnabled() { return Alpine.store('schoolConfig').coranEnabled; }`).
- `_Layout.cshtml` : lien `/coran` juste après « Matières », gardé par `x-show="canView(['Directeur','Enseignant','Secretariat']) && coranEnabled"`, icône `book`, même structure que le lien Internat.

### 3.2 Écran `/coran` (`Views/Coran/Index.cshtml`, `wwwroot/js/coran.js`)

Composant Alpine `coranPage()` :
- **Chargement initial** : `GET /classrooms` (déjà utilisé par `grades.js`) pour peupler le sélecteur de classe. Aucune classe sélectionnée au départ — écran vide avec message d'invite, comme Notes sans sélection.
- **Sélection d'une classe** → charge en parallèle `GET /quran/progress/classroom/{id}` et `GET /quran/evaluations/classroom/{id}` (chacun retourne une ligne par élève avec sa liste d'observations — `ClassQuranProgressRowDto[]`/`ClassQuranEvaluationRowDto[]`). Les deux requêtes sont indépendantes : l'échec de l'une n'empêche pas l'affichage de l'autre onglet (erreur isolée par onglet, pas un bandeau global qui casserait les deux).
- **Deux onglets** (`activeTab: 'progress' | 'evaluations'`), bascule simple sans rechargement — les deux jeux de données sont déjà en mémoire après la sélection de classe.
- **Tableau par onglet** : une ligne par élève (tri alphabétique, déjà garanti par l'API) :
  - Colonne résumé : dernière entrée de `entries` (l'API ne les trie pas explicitement par date — le client trie par `evaluationDate` décroissant, une entrée sans date `EvaluationDate` — cas valide de `QuranProgress` — passe en dernier).
  - Bouton d'expansion (chevron) → ligne dépliée listant TOUTES les entrées de l'élève (tableau interne : Juz/Hizb/Sourate/Statut/Date/Notes ou Date/Erreurs mémoire/Erreurs tajwid/Hésitations/Note), chacune avec un bouton « Corriger » (icône `pencil`).
  - Bouton « + Ajouter » par ligne élève, visible seulement si rôle autorisé (`canEnterQuran`, calqué sur `canEnterGrades`).
- **Aucune donnée** (classe sans élève ou élève sans historique) : ligne/état vide explicite, jamais un tableau qui semble cassé.

### 3.3 Modale de saisie/correction

Une modale par onglet (deux schémas de champs distincts, mais un seul patron de code) :
- **Création** : tous les champs éditables, valeurs par défaut sensées (Statut = « En cours », Date = aujourd'hui pour Évaluation, vide pour Progress). `POST /quran/progress` ou `POST /quran/evaluations`.
- **Correction** : Juz/Hizb/Sourate (Progress) ou aucun champ identité (Evaluation, elle n'a que l'Élève comme immuable) affichés en lecture seule ; le reste éditable. `PUT /quran/progress/{id}` ou `PUT /quran/evaluations/{id}` avec le `rowVersion` de l'entrée ouverte.
- **Validation client** : bornes Juz 1-30 / Hizb 1-60 / Sourate 1-114 (reflet des bornes serveur — évite un aller-retour pour une erreur triviale), mais la validation SERVEUR reste la seule autorité (422 affiché tel quel si le client laisse passer un cas limite).
- **Erreurs** : 422 → message de validation du serveur ; 409 → « Cette observation a été modifiée par un autre utilisateur. » (même formulation que la garde de concurrence de l'Internat), les deux via `window.api.toMessage()`.
- Fermeture/réouverture : état neuf à chaque ouverture (pas de résidu d'une saisie précédente), même précaution que `internat.js` (`openAssignModal`).

### 3.4 Aide et documentation en ligne

`help.js` déjà mentionne le module Coran/Franco-Arabe en tant que capacité (« Servir les
établissements franco-arabes ou coraniques… ») — ajouter une entrée décrivant l'écran lui-même
(emplacement, deux onglets, qui peut saisir), sur le modèle des entrées Internat existantes.

## 4. Tests

- **JS unitaires** (`tests/js/coran.test.mjs`, sur le modèle de `harness.mjs`) : la fonction de tri
  « dernière entrée » (entrée sans date en dernier), la construction du payload de création/
  correction (immutabilité des champs identité en mode correction), le mapping 409/422 →
  message affiché.
- **Fonctionnels/E2E** : aucun nouveau test serveur — l'API est déjà couverte (Phase 2 §5). Un test
  manuel de bout en bout (Directeur ajoute un suivi, un Enseignant le consulte, un Secrétariat ne
  peut pas écrire) suffit à valider le branchement, comme pour l'écran Internat en son temps.

## 5. Hors périmètre (rappel)

- Toute modification de l'API `/api/v1/quran` (Phases 1-2, inchangées).
- Toute modification du bulletin ou de `Subject.SectionType` (déjà livré, voir §1).
- Tableau de bord/KPIs, import en masse, export PDF du suivi coranique — aucun ticket ne les
  demande.
- Toute vérification d'affectation enseignant↔matière (décision Phase 2 #1, non rouverte ici).
