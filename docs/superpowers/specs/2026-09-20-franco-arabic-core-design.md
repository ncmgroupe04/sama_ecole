# Module Franco-Arabe & Écoles Coraniques (Daaras) — Socle de données

**Date :** 20/09/2026
**Statut :** Validé en brainstorming, en attente de plan d'implémentation.
**Périmètre :** Poser le socle de données du futur module Coran/Franco-Arabe
(`SchoolModule.Coran`, `SchoolSettings.IsCoranModuleEnabled`), actuellement un réglage
« réservé » sans écran ni route (voir `docs/superpowers/specs/2026-09-18-module-internat-design.md`
§8, qui renvoyait explicitement à un cadrage séparé). Cette spec couvre **uniquement** : schéma
de données (Matières, SchoolSettings), deux entités préparatoires de suivi coranique, et la
confirmation que le rendu RTL existant les couvre déjà. **Aucun CQRS, contrôleur ni écran** n'est
construit dans ce lot — même logique que l'a été `IsInternatEnabled` avant sa spec dédiée.

## 1. Contexte

Le produit porte déjà une partie du socle sans le savoir :

- `Subject.NameAr` (nullable) existe depuis le module de bulletin bilingue et est déjà rendu en
  RTL dans `ReportCardDocument` via `Bilingual.ArabicBlock`, gardé par `ReportCardDto.IsBilingualArabic`.
- `SchoolSettings.IsCoranModuleEnabled` + `SchoolModule.Coran` sont réservés (aucun écran/route
  ne les consomme).
- `Bilingual` / `PdfFonts.Arabic` (`SamaEcole.Infrastructure.Documents`) fournissent déjà la
  mécanique RTL générique pour QuestPDF.
- `TypeEtablissement` (Privé/Public) est un axe distinct et sans rapport (visibilité du module
  Finance) — à ne pas confondre avec la classification demandée ici.

Aucune spec fonctionnelle n'existe encore dans `docs/Volume_1_Cahier_des_Charges.md` ni
`docs/BACKLOG_TICKETS.md` pour ce module : ce document couvre uniquement le socle de données
demandé, pas le cadrage fonctionnel complet (qui viendra avec les écrans).

## 2. Décisions actées (brainstorming du 20/09/2026)

| # | Question | Décision |
|---|---|---|
| 1 | Titre arabe d'une matière | Réutiliser `Subject.NameAr` — **aucun** nouveau champ `ArabicTitle` (duplication). |
| 2 | Regroupement de matières | Nouveau `Subject.SectionType` (enum `French` défaut / `Arabic` / `IslamicStudies`), purement descriptif, `Coefficient` inchangé. |
| 3 | Classification d'établissement | Nouveau `SchoolSettings.SchoolType` (enum `Standard` défaut / `FrancoArabic` / `Daara`), **purement informatif** — n'active rien, `IsCoranModuleEnabled` reste le seul interrupteur consommé par `[RequireModule]` (aucune activation silencieuse, même philosophie que les délégations Secrétariat/Finance). |
| 4 | `QuranEvaluation.ExamId` | Aucune entité « examen Coran » n'existe et n'est pas demandée ailleurs (Volume J ne couvre que CFEE/BFEM/BAC) : remplacé par `EvaluationDate` — pas de FK vers une entité sans justification métier fournie. |
| 5 | Périmètre du lot | Schéma + entités + config uniquement. Pas de CQRS, pas de contrôleur, pas d'écran — cadrage fonctionnel complet différé à une spec dédiée, sur le modèle Internat. |
| 6 | Verrouillage optimiste | `QuranProgress` et `QuranEvaluation` portent toutes deux un jeton `xmin` (règle #5 : `QuranEvaluation` est une note, `QuranProgress` peut être corrigée par plusieurs enseignants). |

## 3. Modèle de données

### 3.1 `Subject.SectionType` (enum, nouveau)

`French` (défaut) `/ Arabic / IslamicStudies`. Stocké en string
(`HasConversion<string>().HasMaxLength(20)`, même idiome que tout le reste du modèle — voir
`GradeConfiguration.EvaluationType`). Colonne NOT NULL avec défaut `'French'` : toute matière
existante est taguée silencieusement, sans changement de comportement puisqu'aucun code ne lit
encore ce champ. Coexiste avec `Coefficient` et la hiérarchie `ParentSubjectId` existante sans
modification de l'un ou l'autre.

### 3.2 `SchoolSettings.SchoolType` (enum, nouveau)

`Standard` (défaut) `/ FrancoArabic / Daara`. Stocké en string, même idiome. Colonne NOT NULL
avec défaut `'Standard'`. Indépendant de `IsCoranModuleEnabled` et de `TypeEtablissement`
(décision #3) — sert de classification pour un futur affinage de configuration/reporting, pas
un interrupteur.

### 3.3 `QuranProgress` (entité, nouvelle)

Suivi individuel de mémorisation.

```
QuranProgress : AuditableEntity, ITenantEntity
  SchoolId       Guid
  StudentId      Guid        // FK -> Student, OnDelete(Restrict) comme les FK tenant existantes
  JuzNumber      int         // 1..30
  HizbNumber     int         // 1..60
  SurahNumber    int         // 1..114
  Status         QuranMemorizationStatus   // InProcess (défaut) / Memorized / Revised
  EvaluationDate DateOnly?
  Notes          string?
```

`Status` stocké en string (même idiome). `RowVersion`/`xmin` configuré (décision #6) : plusieurs
enseignants peuvent suivre la mémorisation du même élève, un écrasement silencieux romprait la
règle #5. Soft delete hérité d'`AuditableEntity`, aucun ajout nécessaire.

### 3.4 `QuranEvaluation` (entité, nouvelle)

Notes d'examen oral.

```
QuranEvaluation : AuditableEntity, ITenantEntity
  SchoolId        Guid
  StudentId       Guid        // FK -> Student, OnDelete(Restrict)
  EvaluationDate  DateOnly    // remplace ExamId (décision #4)
  MemoryMistakes  int
  TajwidMistakes  int
  Hesitations     int
  FinalScore      decimal
```

`RowVersion`/`xmin` configuré — c'est une note au sens de la règle #5.

### 3.5 Migration

Une migration additive unique (nom proposé : `AddQuranCoreModule`) :

- `subjects.section_type` (text, défaut `'French'`, NOT NULL).
- `school_settings.school_type` (text, défaut `'Standard'`, NOT NULL).
- Tables `quran_progress`, `quran_evaluations` (`Id`, colonnes ci-dessus, champs d'audit, xmin).
- **RLS obligatoire** (règle #2, `RlsCoverageTests`) : `quran_progress` et `quran_evaluations`
  ajoutées au `TenantTables` local de **cette** migration, `ENABLE ROW LEVEL SECURITY` + policy
  `USING (school_id = current_setting(...))`, même idiome que `AddClassJournal`/
  `AddInternatBoarding`.
- Grants au rôle `sama_ecole_app` : `SELECT, INSERT, UPDATE` uniquement — **jamais `DELETE`**
  (règle #6, données historisées, soft delete seul).
- Index (SchoolId, StudentId) sur les deux nouvelles tables, comme `GradeConfiguration`.
- Aucun changement sur `subjects`/`school_settings` côté RLS : déjà dans `TenantTables` depuis
  leurs migrations d'origine.

### 3.6 Ce qui n'est PAS construit dans ce lot

- Aucune entité `QuranExam` / session d'examen (décision #4).
- Aucun Command/Query MediatR, aucun contrôleur, aucun écran — cadrage différé (décision #5).
- Aucun nouveau générateur PDF : le rendu RTL des matières (`NameAr`) fonctionne déjà de bout en
  bout dans les bulletins existants ; un futur document (carnet de mémorisation, certificat
  d'examen oral) devra réutiliser `Bilingual.ArabicBlock`/`SideBySide` + `PdfFonts.Arabic`, jamais
  réinventer la mise en page RTL.
- Aucune modification de `IsCoranModuleEnabled`/`SchoolModule.Coran`/`ModuleAuthorizationHandler` :
  ils restent inchangés, réservés, tels quels.

## 4. Sécurité

- Les deux nouvelles tables portent `SchoolId` + Global Query Filter EF (automatique via
  `ITenantEntity`) + policy RLS (§3.5) — les deux protections de la règle #2, dès cette migration,
  même en l'absence de tout endpoint qui les exploite encore.
- Aucune route n'est ouverte dans ce lot : pas de surface d'attaque nouvelle. La garde
  `[RequireModule(SchoolModule.Coran)]` existante s'appliquera au futur contrôleur, sans
  modification ici.

## 5. Tests

- **`RlsCoverageTests`** (existant, auto-découverte du modèle EF) : passera automatiquement dès
  que `quran_progress`/`quran_evaluations` implémentent `ITenantEntity` et portent leur policy —
  aucune modification du test lui-même.
- **Nouveau test d'isolation dédié** (mirroir de `ClassJournalIsolationTests`/
  `ResetSchoolDataTests` catégorie `MultiTenant`) : preuve qu'une école ne peut ni lire ni écrire
  les `QuranProgress`/`QuranEvaluation` d'une autre, y compris via une session propriétaire vs.
  applicative.
- **Unitaires** : configuration EF (xmin posé, conversions string des enums, valeurs par défaut
  `SectionType`/`SchoolType` sur une matière/école neuve).
- Suite complète (`dotnet test`) lancée avant de clore le lot, sur demande explicite de
  l'utilisateur — aucune régression attendue, changements strictement additifs.

## 6. Hors périmètre (rappel)

- Tout CQRS, contrôleur ou écran pour ce module (spec dédiée à venir, sur le modèle Internat).
- Toute entité `QuranExam`/session d'examen formelle.
- Toute activation automatique du module via `SchoolType` (décision #3).
- Tout nouveau générateur PDF (carnet de mémorisation, certificat d'examen oral).
- Toute modification de `TypeEtablissement`, `IsCoranModuleEnabled`, `SchoolModule.Coran` ou
  `ModuleAuthorizationHandler`.
