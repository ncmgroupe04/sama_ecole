# Franco-Arabe — Bloc 5 : Cahier de roulement / registre d'appel bilingue

> **Statut : VALIDÉE le 2026-10-14** (décisions en §9). Branche : `feat/franco-arabe-cahier-de-roulement` (depuis `main` bfab0361, Blocs 1-4 inclus).
> Date : 2026-10-14. Les points marqués **[À VALIDER]** attendent une décision.

## 1. Constat sur l'existant (ce qui contraint la conception)

| Sujet | État actuel | Conséquence |
|---|---|---|
| Granularité de l'appel | Par **séance** (`AttendanceSheet` : classe, matière, date, créneau). La journée est **calculée** par `DayAttendanceClassifier`, jamais stockée. | Le cahier doit agréger, et il faut une règle pour saisir « une case » sans séance. |
| Statuts | `AttendanceStatus` = Present, JustifiedAbsence, UnjustifiedAbsence, Late. Le retard est une présence tardive. | Justifiée / injustifiée / retard existent déjà : on les réutilise, on n'en crée pas d'autres. |
| Justificatifs | `AbsenceJustification` : registre libre, non relié aux lignes d'appel. | Le cahier ne le lit pas en v1 ; le motif est porté par la saisie du cahier. |
| Rapport d'assiduité | `AttendanceReportAggregator` + export PDF/CSV, par élève et par classe, taux = (présents + retards) ÷ lignes d'appel saisies. | On réutilise la logique de classification ; on n'étend pas ce rapport. Pas de grille mensuelle, pas de niveau, pas de trimestre. |
| Jours de classe | `SchoolSettings.WorkingDays` (défaut lun-sam) et bornes `SchoolYear` / `Term`. **Aucun jour férié ni vacances.** | Dénominateur « jours de classe » fiable impossible sans nouvelle entité. |
| Niveaux | `Classroom.Level` en texte libre, `ClassroomGradeLevels` ; aucune entité Level. | Le regroupement par niveau passe par ce texte normalisé. |
| Établissement | `School` : Name, IA, IEF, NomLycee, logo… **Aucun nom d'école en arabe, ni IA/IEF en arabe.** | Nouveaux champs + migration. |
| Alternance FR/AR | `SectionScheduleAnalyzer` (Bloc 4) : demi-journées, coupure à 13 h. | La demi-journée du cahier suit la même coupure. |
| « Grille horaire officielle » | Aucune entité de ce nom (seulement l'emploi du temps `ScheduleSlot`). | Voir §9, question 6. |
| PDF | QuestPDF, police Noto Naskh, `Bilingual.cs`, `BulletinArabicLabels`, modèle `ExamSynthesisDocument`. | Patron prêt : modèle → interface → générateur → handler → `InlinePdf`. |
| i18n front | Aucun mécanisme ; RTL ponctuel par écran (`grades.js` : objet de libellés + `dir`). | On suit le patron de `grades.js`. |
| Tests UI | Pas de Playwright dans le dépôt ; tests `node --test` sur des scripts évalués dans `node:vm`. | « Tests UI » = tests JS du harness + recette navigateur manuelle (voir §8). |

## 2. Décisions de conception

### 2.1 Unité : la demi-journée
Le cahier de roulement classique et l'alternance FR/AR raisonnent en **matin / après-midi**. Chaque jour de classe a deux cases (`Morning`, `Afternoon`, coupure à 13 h comme `SectionScheduleAnalyzer`). Les compteurs et taux sont en **demi-journées**; l'affichage « jours » = demi-journées ÷ 2.

### 2.2 Source des cases : dérivé + saisie qui prime **[À VALIDER]**
Le statut effectif d'une case (élève, date, demi-journée) est :

1. la **saisie du cahier** (`RollBookEntry`) si elle existe ;
2. sinon le statut **dérivé** des appels par séance existants, en appliquant `DayAttendanceClassifier` aux séances de cette demi-journée (Present / Late / PartialAbsence / FullAbsence ; absence justifiée si toutes les lignes absentes sont `JustifiedAbsence`) ;
3. sinon **non renseigné**.

Pourquoi : les écoles primaires ne font souvent pas d'appel par matière, il leur faut une saisie directe ; et on ne réécrit jamais les fiches d'appel des enseignants. Chaque case indique son origine (point = saisie cahier, sinon dérivé). Un bouton « revenir au dérivé » désactive la saisie (soft delete).
Alternative écartée : réécrire les lignes `StudentAttendance` depuis la grille (perte de traçabilité, conflits avec les enseignants).
Les séances sans `ScheduleSlotId` ne permettent pas de connaître la demi-journée : elles ne sont pas dérivées (case « non renseigné ») et la grille le signale.

### 2.3 Calendrier de fermeture (nouveau)
Entité `SchoolClosure` (tenant) : `SchoolYearId`, `StartDate`, `EndDate`, `Kind` (Holiday, Vacation, Other), `Label`, `LabelAr?`. Jours de classe d'un mois = jours de `WorkingDays` ∩ bornes de l'année − fermetures. Édition dans Paramètres (onglet calendrier).
**v1 : le cahier lit ce calendrier, mais `WorkingDayGuard` et le rapport d'assiduité existant ne changent pas** (pas de régression sur un flux en production). Une case saisie un jour fermé reste acceptée et signalée.

### 2.4 Formules **[À VALIDER]**
Pour un élève, une classe ou un niveau sur une période (mois, ou `Term`) :

- `N` = demi-journées **renseignées** (une case effective existe). On affiche aussi la **couverture** = N ÷ demi-journées de classe, pour ne pas faire passer un cahier vide pour un taux de 100 %.
- `P` présent, `R` retard, `AJ` absence justifiée, `ANJ` absence non justifiée. Une absence partielle compte comme présent (comme le rapport actuel) et alimente un compteur à part.
- **Taux de présence** = (P + R) ÷ N
- **Taux d'absence** = (AJ + ANJ) ÷ N
- **Taux d'assiduité** = (P + R + AJ) ÷ N, c'est-à-dire que seules les absences non justifiées pénalisent.

Agrégation par classe et par niveau : **somme des compteurs** puis division (pas de moyenne de taux). Un élève n'est compté que pour les demi-journées où il est inscrit dans la classe (transferts en cours de période : hors périmètre, noté en limite connue).

### 2.5 Métadonnées institutionnelles
Ajout sur `School` : `NameAr`, `InspectionAcademieAr`, `InspectionEducationFormationAr` (nullable, sans défaut requis, donc sans risque pour `provision_school_director`). Édition : onglet « Établissement » de Paramètres (entité, configuration, migration, DTO, commande, handler, validateur, `settings.js`, vue).
Si un champ arabe est vide, le PDF imprime la ligne arabe avec un blanc à compléter à la main plutôt que de transcrire du français en arabe.

## 3. Modèle de données

```
roll_book_entries            (tenant, soft delete, append-friendly)
  Id, SchoolId, SchoolYearId, ClassroomId, StudentId,
  Date (DateOnly), HalfDay (Morning|Afternoon),
  Status (AttendanceStatus), LateMinutes?, Reason?,
  RecordedByUserId, + colonnes d'audit
  UNIQUE (SchoolId, StudentId, Date, HalfDay) WHERE IsDeleted = false
  FK composites (SchoolId, X) vers students, classrooms, school_years

school_closures              (tenant, soft delete)
  Id, SchoolId, SchoolYearId, StartDate, EndDate, Kind, Label, LabelAr?

schools : + NameAr, InspectionAcademieAr, InspectionEducationFormationAr
```

Migration (à la main) : RLS + policy `{table}_tenant_isolation`, `GRANT SELECT, INSERT, UPDATE` à `sama_ecole_app` (jamais DELETE), correctif de `reset_school_data` et `delete_school_year` pour les deux nouvelles tables, mise à jour de `docs/architecture/soft-delete-index-inventory.md`. Garde existante : `RlsCoverageTests`.

## 4. Couche applicative (CQRS MediatR, aucune logique dans les contrôleurs)

- `RollBook/RollBookCellResolver` : fusion saisie / dérivé pour une demi-journée (pure, testable sans base).
- `RollBook/HalfDayDeriver` : réutilise `DayAttendanceClassifier` sur le sous-ensemble de séances.
- `RollBook/SchoolCalendar` : jours de classe d'une période (WorkingDays, bornes, fermetures).
- `RollBook/AttendanceRateCalculator` : compteurs et taux (§2.4), agrégation élève / classe / niveau.
- Requêtes : `GetRollBookGrid` (classe + mois), `GetRollBookStats` (élève | classe | niveau × mois | trimestre), `GetRollBookPdf`, `ListClosures`.
- Commandes : `UpsertRollBookEntries` (lot, une classe, une transaction), `RevertRollBookEntry`, `Create/Update/DeleteClosure` (suppression logique), `UpdateCurrentSchool` étendue.
- Portée d'un Enseignant : `AttendanceScopeAuthorizer` existant, bornée à ses classes.

## 5. API (`schoolId` toujours issu du JWT)

| Verbe | Route | Rôles |
|---|---|---|
| GET | `/api/v1/roll-book?classroomId&month=YYYY-MM` | Directeur, Secrétariat, Surveillant, Enseignant (borné), SuperAdmin |
| PUT | `/api/v1/roll-book/entries` (lot) | Directeur, Secrétariat, Surveillant, Enseignant (borné) |
| DELETE | `/api/v1/roll-book/entries/{id}` (revient au dérivé) | idem |
| GET | `/api/v1/roll-book/stats?scope=student\|class\|level&period=month\|term&…` | Directeur, Secrétariat, SuperAdmin |
| GET | `/api/v1/roll-book/pdf?classroomId&month\|termId&lang=fr\|ar\|bi` | Directeur, Secrétariat, Surveillant, SuperAdmin |
| GET/POST/PUT/DELETE | `/api/v1/school-calendar/closures` | lecture : tous les rôles du module ; écriture : Directeur |

Tous les contrôleurs portent `[RequireModule(SchoolModule.Pedagogy)]` (sinon `ModuleCoverageTests` échoue). Erreurs normalisées (règle #9). Le rendu bilingue (§6) s'active avec `IsCoranModuleEnabled`, comme `ExamSynthesisBuilder` ; une école hors Franco-Arabe a un cahier français uniquement.

## 6. Export PDF (QuestPDF)

`RollBookDocument` + `RollBookPdfGenerator`, enregistré dans `DependencyInjection`, rendu via `PdfRenderGuard`, servi par `InlinePdf` et l'aperçu `_PdfPreviewModal`. Même patron que `ExamSynthesisDocument`.

- **Format** : A4 paysage, tableau avec en-têtes répétés sur chaque page ; classes de plus de ~35 élèves sur plusieurs pages.
- **Bandeau** : République, devise, Ministère (FR et AR via `BulletinArabicLabels`), puis IA, IEF, établissement en FR et AR (§2.5), logo, classe, niveau (FR/AR), mois, année scolaire.
- **Corps** : une colonne par jour de classe divisée en M / S (matin / après-midi), jours fermés grisés avec le libellé de fermeture ; marques P, R, AJ, ANJ, « — » pour non renseigné ; colonnes de totaux par élève (P, R, AJ, ANJ, taux) ; ligne de totaux par jour.
- **Pied** : légende, couverture, signatures (maître, directeur) et cachet (`OfficialStampUrl`, `DirectorSignatureUrl`).
- **Variante trimestrielle** : récapitulatif mois par mois + total du trimestre par élève, plus une page de synthèse par niveau.
- **Langues** : `fr` (LTR), `ar` (document entièrement RTL via `ContentFromRightToLeft`), `bi` (par défaut en Franco-Arabe : grille LTR, en-têtes et libellés FR/AR côte à côte via `SideBySide`, nom de l'élève en FR avec le nom arabe dessous). Pas de mélange FR/AR mot à mot dans un même `Text()`.
- **Libellés** : `RollBookArabicLabels` (modèle `ExamArabicLabels`) ; noms des niveaux (CI, CP, CE1… 6e) traduits par un dictionnaire statique. **Traductions à faire relire par un locuteur arabe** (même réserve que `BulletinArabicLabels`).
- La conformité exacte aux standards d'une inspection donnée n'est pas vérifiable depuis le code : voir question 4.

## 7. Interface (Razor + Alpine + Tailwind, `roll-book.js`)

- Page `/cahier-de-roulement`, `[PageModule(Pedagogy)]`, entrée dans le pôle « Surveillance » de la sidebar, `_QuickNav`, `help.js`.
- Sélecteurs classe + mois, bascule FR / AR / bilingue **propre à l'écran** (objet `LABELS` FR/AR + `dir`, patron de `grades.js`), police Cairo pour l'arabe.
- Grille : colonne élève figée, 2 sous-cases par jour, saisie au clic (cycle Présent → Retard → Abs. J → Abs. NJ → vide) et au clavier, enregistrement en lot, indicateur dérivé / saisi.
- Onglet « Statistiques » : taux élève / classe / niveau, mois / trimestre.
- Paramètres : champs arabes de l'établissement, gestion des fermetures.
- Documentation : `openapi.yaml`, `docs/Volume_4_API_Design.md`, `docs/GUIDE_FONCTIONNEL_MODULES.md`, `ACTIVE_CONTEXT.md`, `CHANGELOG.md`.

## 8. Plan de tests

- **Unitaires** (xUnit + FluentAssertions) : `CellResolver` (saisie prime, dérivé, non renseigné), `HalfDayDeriver` (coupure 13 h, séance sans créneau), `SchoolCalendar` (jours ouvrés, fermetures, bornes), `RateCalculator` (formules §2.4, agrégation par somme, N = 0, couverture), libellés arabes, PDF lu avec PdfPig (texte arabe présent, en-tête FR/AR, légende).
- **Intégration** (Testcontainers, vrai PostgreSQL) : RLS et isolation tenant `[Trait("Category","MultiTenant")]` sur les deux tables, index unique partiel, soft delete + revert, `RlsCoverageTests`, fonctions de purge.
- **Fonctionnels** (HTTP) : rôles et 403, portée Enseignant, `MODULE_DISABLED`, `ModuleCoverageTests`, PDF `application/pdf` non vide, lot de saisie atomique.
- **UI** : tests `node --test` sur `roll-book.js` (cycle de statuts, libellés FR/AR, bascule `dir`, calcul de lot), puis **recette navigateur manuelle** sur base jetable (FR, AR, bilingue, mode sombre). Il n'y a pas de test Playwright automatisé dans ce dépôt ; en créer une infrastructure dépasserait le Bloc 5.
- Selon la consigne du dépôt, je lance les tests ciblés avec `--filter`, pas la suite complète.

## 9. Décisions validées (2026-10-14)

1. Saisie du cahier prioritaire sur le dérivé, avec retour au calcul automatique (§2.2).
2. Formules du §2.4 retenues (assiduité = P + R + AJ, dénominateur = demi-journées renseignées, couverture affichée).
3. `WorkingDayGuard` inchangé en v1 ; les fermetures ne servent qu'au cahier.
4. Pas de modèle d'inspection fourni : mise en page du §6 « au mieux ».
5. Le **Surveillant peut exporter le PDF** (tableau §5 mis à jour).
6. « Grille horaire » = alternance FR/AR du Bloc 4, coupure 13 h.
7. Livraison en **3 PR empilées** : 5a champs arabes + fermetures + migration ; 5b grille, saisie, statistiques, écran ; 5c exports PDF.

## 10. Hors périmètre

Notifications aux parents, transferts de classe en cours de mois, lien avec `AbsenceJustification`, attribution du calendrier de fermeture à `WorkingDayGuard`, export SIMEN (pas d'API publique), appel de l'internat (déjà couvert par les sessions de pointage), infrastructure de tests Playwright.
