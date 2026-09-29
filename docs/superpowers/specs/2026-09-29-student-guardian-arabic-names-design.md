# Noms bilingues Élève & Tuteur (Français/Arabe) — Module Franco-Arabe

**Date :** 29/09/2026
**Statut :** Validé en brainstorming, en attente de plan d'implémentation.
**Périmètre :** Ajouter un nom arabe optionnel en miroir de `Student.FullName` et
`Student.GuardianName`, sur le modèle déjà posé par `Subject.NameAr`
(`docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md`). Ce lot est le premier
d'une décomposition en quatre sous-projets indépendants du module bilingue FR/AR (recherche
bilingue, i18n UI complète LTR/RTL, PDF bilingues) — les trois autres font l'objet de specs
séparées, non couvertes ici.

## 1. Contexte

Une demande fonctionnelle initiale (voir historique de conversation, 29/09/2026) proposait un
éclatement complet `FirstNameFr`/`LastNameFr`/`FirstNameAr`/`LastNameAr`. Exploration du code :

- `Student.FullName` est un **champ unique** (`required string`, `HasMaxLength(200)`,
  `StudentConfiguration.cs`), utilisé dans **364 occurrences** à travers ~40 fichiers
  (Application, PDF, vues Razor, JS, tests). Il n'existe aucune séparation prénom/nom
  aujourd'hui, ni pour `Student` ni pour le tuteur.
- `Student.GuardianName` (`string?`, `MaximumLength(200)` + `.NoHtml()` côté validators
  `CreateStudentCommandValidator`/`UpdateStudentCommandValidator`) est également un champ texte
  libre unique, sans entité `Guardian` dédiée.
- Le seul précédent existant pour un champ bilingue est `Subject.NameAr` (`string?`,
  `HasMaxLength(80)`) : un **miroir arabe nullable** du nom français existant, saisi librement,
  sans traduction automatique, sans validation de contenu arabe. Déjà câblé de bout en bout côté
  Application (`CreateSubjectCommand`/`UpdateSubjectCommand`/`GetSubjectsQuery`) et déjà rendu en
  RTL dans `ReportCardDocument` via `Bilingual.ArabicBlock`/`PdfFonts.Arabic`, mais **sans aucun
  formulaire Razor pour le saisir** avant ce lot — l'unique UI de référence est
  `Views/Subjects/Index.cshtml` (modales de création/édition, `x-model="newSubject.nameAr"` /
  `editing.nameAr`, `dir="rtl"`, `maxlength="80"`, libellé « Nom en arabe (facultatif) »).

Décision actée avec l'utilisateur (29/09/2026) : reproduire strictement le patron `Subject.NameAr`
— un miroir arabe nullable, **sans éclatement de `FullName`**. Un éclatement structurel complet
toucherait potentiellement les 364 usages existants pour un bénéfice non demandé dans l'immédiat ;
il reste une option pour un lot ultérieur si le besoin se confirme (non couvert ici).

## 2. Décisions actées (brainstorming du 29/09/2026)

| # | Question | Décision |
|---|---|---|
| 1 | Ampleur du changement sur le nom élève | Miroir simple : ajouter `FullNameAr`, **aucun** éclatement `FirstName`/`LastName`. `FullName` (FR) reste inchangé partout — 0 des 364 usages existants modifiés. |
| 2 | Tuteur | Même traitement : ajouter `GuardianNameAr` dans le même lot (coût marginal faible, même entité, même migration). |
| 3 | Import CSV en masse | Inclus dans ce lot : `ImportStudentsCommand`/`StudentImportFileRow` gagnent deux colonnes optionnelles, même mécanique de `fieldErrors` par ligne que les colonnes existantes. |
| 4 | Recherche, PDF, i18n UI | Explicitement **hors périmètre** de ce lot — sous-projets séparés (voir §6). |

## 3. Modèle de données

### 3.1 `Student.FullNameAr` / `Student.GuardianNameAr` (nouveaux champs)

```
Student (ajouts)
  FullNameAr      string?   // miroir arabe de FullName, facultatif
  GuardianNameAr  string?   // miroir arabe de GuardianName, facultatif
```

Aucune traduction automatique : saisi librement par l'école, exactement le principe de
`Subject.NameAr` (voir sa doc XML, à reprendre à l'identique sur les deux nouvelles propriétés).
Null tant que personne ne l'a renseigné — un bulletin ou un export futur qui les consommerait
imprimerait alors la ligne sans second nom, jamais une valeur inventée.

### 3.2 `StudentConfiguration.cs`

```csharp
builder.Property(s => s.FullNameAr).HasMaxLength(200);
builder.Property(s => s.GuardianNameAr).HasMaxLength(200);
```

200 caractères choisi pour s'aligner sur `FullName` (200) et sur la borne déjà imposée à
`GuardianName` par son validator (`MaximumLength(200)`), plutôt que sur les 80 de
`Subject.NameAr` (un nom de matière est plus court qu'un nom de personne).

### 3.3 Migration

Une migration additive unique (nom proposé : `AddStudentArabicNames`) :

- `students.full_name_ar` (text, nullable).
- `students.guardian_name_ar` (text, nullable).
- Aucun changement RLS : `students` est déjà dans `TenantTables` depuis sa migration d'origine,
  et ce lot n'ajoute aucune table — seulement deux colonnes sur une table déjà protégée par sa
  policy RLS + le Global Query Filter EF existants (AGENTS.md règle #2).

## 4. Couche Application

### 4.1 Écriture

- `CreateStudentCommand` (`src/SamaEcole.Application/Students/Commands/CreateStudent/`) : ajout
  de `FullNameAr` (`string?`) et `GuardianNameAr` (`string?`).
  `CreateStudentCommandValidator` : `RuleFor(x => x.FullNameAr).MaximumLength(200).NoHtml();` et
  `RuleFor(x => x.GuardianNameAr).MaximumLength(200).NoHtml();` — **pas de `.NotEmpty()`**
  (facultatifs, contrairement à `FullName`), pas de validation de script arabe (même choix que
  `Subject.NameAr`, qui n'en impose pas).
  `CreateStudentCommandHandler` : affecte les deux champs à l'entité `Student` créée.
- `UpdateStudentCommand` (record positionnel) : ajout de `FullNameAr` et `GuardianNameAr` dans la
  même position logique que `FullName`/`GuardianName`. `UpdateStudentCommandValidator` : mêmes
  règles que côté création. `UpdateStudentCommandHandler` : écrit les deux champs (écrasement
  simple, pas de fusion — même comportement que le reste des champs texte de `UpdateStudentCommand`,
  aucun verrouillage optimiste requis ici, ces champs ne sont pas dans le périmètre `xmin` de
  AGENTS.md règle #5 qui vise Notes/Paiements/Frais).

### 4.2 Lecture

- `StudentIdentityDto` (`GetStudentDetailQuery.cs`) : ajout de `FullNameAr` et `GuardianNameAr`
  après leurs équivalents FR dans la déclaration du record, et dans la projection EF
  (`s.FullNameAr`, `s.GuardianNameAr`) ainsi que dans la construction manuelle plus bas dans le
  fichier (`student.FullNameAr`, `student.GuardianNameAr`) — le fichier construit le DTO à deux
  endroits (projection + reconstruction), les deux doivent être tenus à jour.
- `StudentListItem` (`GetStudentsQuery.cs`) : ajout de `FullNameAr` (utile pour un futur affichage
  liste/RTL, hors périmètre ici mais le DTO doit porter la donnée). `GuardianNameAr` **non ajouté**
  à `StudentListItem` : le tuteur n'apparaît pas dans la liste élèves aujourd'hui (seul `FullName`
  y figure), pas de besoin identifié d'y ajouter son miroir arabe dans ce lot.

### 4.3 Import CSV en masse

- `StudentImportFileRow.cs` : ajout de `FullNameAr`/`GuardianNameAr` (colonnes optionnelles du
  gabarit).
- `ImportStudentsCommand`/`ImportStudentsCommandHandler`/`ImportStudentsCommandValidator` : même
  traitement que les colonnes FR existantes, mêmes règles de validation qu'en §4.1 (facultatif,
  `MaximumLength(200)`, `.NoHtml()`), mêmes `fieldErrors` par ligne en cas de dépassement.

### 4.4 Explicitement non modifié dans ce lot

- `StudentsExportModel.cs` / `GetStudentsExportPdfQueryHandler.cs` (export PDF/liste élèves) : ne
  consomment pas les nouveaux champs — le rendu bilingue d'un document est du ressort du sous-projet
  « PDF bilingues » (§6), pas de celui-ci.
- Aucun autre des 364 usages de `FullName` n'est touché.

## 5. UI (Razor + Alpine.js — `Views/Students/Index.cshtml`)

Reprise stricte du patron déjà posé pour `Subject.NameAr` dans `Views/Subjects/Index.cshtml`
(`x-model`, `dir="rtl"`, libellé « (facultatif) »), avec `maxlength="200"` (au lieu de 80, voir
§3.2) :

- **Modale de création** : deux nouveaux champs sous `newStudent.fullName` et
  `newStudent.guardianName` respectivement — `newStudent.fullNameAr` / `newStudent.guardianNameAr`.
- **Modale d'édition** : `editingStudent.fullNameAr` / `editingStudent.guardianNameAr`, même
  emplacement relatif que leurs équivalents FR (lignes actuelles ~878 et ~929).
- **Fiche détail élève** : `detailStudent.fullNameAr` affiché sous le nom français
  (`x-show` conditionnel — rien si vide, jamais une case réservée) ; `detailStudent.guardianNameAr`
  affiché sous la ligne tuteur existante (ligne actuelle ~394).
- **Liste/carte élève, avatar, initiales** : **aucun changement**. Le nom arabe n'y apparaît pas —
  cohérent avec l'absence de bascule RTL de l'application dans ce lot (§6).
- **Écran d'import CSV** (lignes ~1215/1220 actuelles, `importCellClass`) : deux colonnes
  supplémentaires dans le tableau d'aperçu, même mécanique d'affichage d'erreur par cellule que les
  colonnes existantes.

## 6. Hors périmètre de ce lot (rappel de la décomposition en 4 sous-projets)

- **Recherche bilingue** (normalisation arabe — tashkeel/alifs — et recherche unifiée FR/AR dans la
  recherche globale de l'en-tête, qui n'existe pas encore) : sous-projet suivant, dépend des champs
  posés ici.
- **i18n UI complète** (`User.PreferredLanguage`, bascule LTR/RTL, traduction des menus) : sous-projet
  séparé, transverse à toute l'application, pas seulement au module Franco-Arabe.
- **PDF bilingues** (reçu, bulletin) : `ReportCardDocument` ne consomme pas `FullNameAr`/
  `GuardianNameAr` dans ce lot — sous-projet dédié, sur le modèle de ce qui existe déjà pour
  `Subject.NameAr`.
- Aucun éclatement de `FullName`/`GuardianName` en `FirstName`/`LastName` (décision #1, §2).
- Aucune entité `Guardian` dédiée.

## 7. Sécurité

- Aucune nouvelle table, aucune nouvelle route, aucune surface d'attaque nouvelle : deux colonnes
  nullable sur une table déjà protégée (RLS + Global Query Filter EF, AGENTS.md règle #2).
- `.NoHtml()` sur les deux nouveaux champs (§4.1) — même garde anti-injection que tous les champs
  texte libres existants de `Student`.

## 8. Tests

- **Unitaires EF** : configuration (`HasMaxLength(200)`, nullable, valeur par défaut `null` sur un
  élève neuf) — mirroir des tests existants de configuration `Subject.NameAr`.
- **Validators** : `CreateStudentCommandValidator`/`UpdateStudentCommandValidator` — champ absent
  accepté, présence acceptée, dépassement de 200 caractères refusé, contenu HTML refusé
  (`.NoHtml()`).
- **Handlers** : `CreateStudentCommandHandler`/`UpdateStudentCommandHandler` persistent bien les
  deux champs ; `GetStudentDetailQueryHandler`/`GetStudentsQueryHandler` les exposent dans leurs
  DTO respectifs (projection **et** reconstruction pour `GetStudentDetailQuery`, voir §4.2).
- **Import CSV** : ligne avec `FullNameAr`/`GuardianNameAr` valides importée correctement ; ligne
  avec dépassement de longueur ou HTML rejetée avec `fieldErrors` explicite.
- **JS** (`src/SamaEcole.Web/tests/js/`) : extension des tests existants de saisie/affichage élève
  s'ils couvrent déjà la modale création/édition/détail, pour y ajouter les deux nouveaux champs.
- Pas de nouveau test `Category=MultiTenant` requis (pas de nouvelle table, AGENTS.md règle #2).
- Suite complète (`dotnet test`) avant de clore le lot, changements strictement additifs, aucune
  régression attendue sur les 364 usages existants de `FullName` (non touchés).
