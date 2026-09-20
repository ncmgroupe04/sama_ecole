# Module Franco-Arabe & Écoles Coraniques — CQRS et API (Phase 2)

**Date :** 20/09/2026
**Statut :** Validé en brainstorming, en attente de plan d'implémentation.
**Périmètre :** Rendre le module Coran/Franco-Arabe opérationnel côté API — CQRS complet
(Commands/Queries MediatR), validateurs FluentValidation, `QuranController`. S'appuie sur le socle
posé en Phase 1 (`docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md`) : entités
`QuranProgress`/`QuranEvaluation`, migration `AddQuranCoreModule`, `SchoolModule.Coran` +
`IsCoranModuleEnabled` déjà réservés. **Aucun écran, aucun JavaScript** dans ce lot — l'API seule,
comme demandé.

## 1. Contexte

Le module Coran/Franco-Arabe suit exactement la trajectoire déjà empruntée par l'Internat : un
réglage réservé (`IsInternatEnabled`/`IsCoranModuleEnabled`) posé bien avant tout écran ou route,
puis une spec dédiée qui construit le CQRS et le contrôleur, puis l'écran dans un lot séparé. Ce
document couvre l'étape CQRS/API.

Aucun sous-rôle « Enseignant Coran » n'existe dans `Role` (SuperAdmin, Directeur, Secretariat,
Finance, Enseignant, Surveillant). Le module suit donc le même patron d'autorisation que les Notes
classiques (`GradesController`) : n'importe quel Enseignant de l'école peut saisir, sans
vérification d'affectation à une matière précise (`TeacherAssignment`) — décision actée en
brainstorming, alignée sur `CreateGradeCommandHandler` qui ne fait aucune vérification de ce genre.

## 2. Décisions actées (brainstorming du 20/09/2026)

| # | Question | Décision |
|---|---|---|
| 1 | Rôles d'écriture (Create/Update) | Directeur + Enseignant, comme `GradingRoles` (`GradesController`). Pas de garde fine par affectation. |
| 2 | Rôles de lecture (Get/List) | Directeur + Enseignant + Secrétariat, comme `ViewGradesRoles`. |
| 3 | `UpdateQuranEvaluationCommand` | Construite (avec verrou `xmin`), malgré la formulation initiale « enregistrer et lire » — cohérent avec le verrou déjà posé en Phase 1 précisément pour ce cas. |
| 4 | Granularité de lecture | Par élève ET par classe (4 requêtes au total : 2 par entité). |
| 5 | Garde du module | `[RequireModule(SchoolModule.Coran)]` sur le contrôleur SUFFIT — contrairement à Internat, aucune donnée Coran ne vit sur une entité partagée (`Enrollment`) qui resterait accessible par un autre chemin ; aucun garde-fou Handler supplémentaire n'est nécessaire. |
| 6 | Journal d'audit | Les 4 commandes portent `IAuditableRequest`, comme la saisie/correction de notes (JGK-H01). |
| 7 | Plafond de `FinalScore` | Aucun — contrairement au barème des Notes (`GradingScaleGuard`), aucun barème n'a été spécifié pour l'examen oral coranique ; seule la borne `≥ 0` est imposée. À arbitrer si le besoin remonte. |
| 8 | Champs immuables en correction | `UpdateQuranProgressCommand` ne touche jamais Juz/Hizb/Sourate/Élève ; `UpdateQuranEvaluationCommand` ne touche jamais l'Élève — même philosophie qu'`UpdateGradeCommand` (seul `Value` change). |

## 3. Modèle CQRS

### 3.1 Commands — QuranProgress

**`CreateQuranProgressCommand`** (`SamaEcole.Application.Quran.Commands.CreateQuranProgress`)
```
CreateQuranProgressCommand(
    Guid StudentId, int JuzNumber, int HizbNumber, int SurahNumber,
    QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes)
    : IRequest<QuranProgressDto>, IAuditableRequest
```
Handler : vérifie que `StudentId` existe dans l'école courante (même contrôle que
`CreateGradeCommandHandler`, message dédié si absent). Aucune contrainte d'unicité (décision Phase 1
§3.3) — une création réussit toujours si l'élève existe.

**`UpdateQuranProgressCommand`**
```
UpdateQuranProgressCommand(
    Guid Id, QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes, uint RowVersion)
    : IRequest<QuranProgressDto>, IAuditableRequest
```
Handler : charge la ligne (404 si absente ou d'une autre école — Global Query Filter), pose le jeton
de concurrence attendu (`SetOriginalConcurrencyToken`), écrit les trois champs, `SaveChangesAsync` →
409 (`ConcurrencyConflictException`) si `RowVersion` périmé.

### 3.2 Commands — QuranEvaluation

**`CreateQuranEvaluationCommand`**
```
CreateQuranEvaluationCommand(
    Guid StudentId, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes,
    int Hesitations, decimal FinalScore)
    : IRequest<QuranEvaluationDto>, IAuditableRequest
```
Handler : même contrôle d'existence de l'élève que ci-dessus.

**`UpdateQuranEvaluationCommand`**
```
UpdateQuranEvaluationCommand(
    Guid Id, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes,
    int Hesitations, decimal FinalScore, uint RowVersion)
    : IRequest<QuranEvaluationDto>, IAuditableRequest
```
Même patron 404/409 que `UpdateQuranProgressCommand`.

### 3.3 Queries

```
GetStudentQuranProgressQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranProgressDto>>
GetClassQuranProgressQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranProgressRowDto>>
GetStudentQuranEvaluationsQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranEvaluationDto>>
GetClassQuranEvaluationsQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranEvaluationRowDto>>
```
Les deux requêtes « par classe » retournent une ligne par élève actuellement inscrit dans la classe
(comme `StudentGradeRowDto`), portant la LISTE de ses observations/évaluations — pas de forme fixe
(Devoir1/2/Composition) puisque le nombre d'observations par élève est libre (décision Phase 1).

### 3.4 DTOs (`SamaEcole.Application.Quran`)

```
QuranProgressDto(
    Guid Id, Guid StudentId, int JuzNumber, int HizbNumber, int SurahNumber,
    QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes, uint RowVersion)

ClassQuranProgressRowDto(
    Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranProgressDto> Entries)

QuranEvaluationDto(
    Guid Id, Guid StudentId, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes,
    int Hesitations, decimal FinalScore, uint RowVersion)

ClassQuranEvaluationRowDto(
    Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranEvaluationDto> Entries)
```
`QuranProgressDto`/`QuranEvaluationDto` servent À LA FOIS de résultat de Create/Update et d'élément
de liste — pas de type `Result` séparé (contrairement à `GradeResult`, qui existe seulement parce
que `GradeCellDto` ne porte pas `StudentId` ; ici le DTO complet suffit dans les deux cas, YAGNI).

### 3.5 Validation (FluentValidation)

- `CreateQuranProgressCommandValidator` : `StudentId` non vide ; `JuzNumber` entre 1 et 30 ;
  `HizbNumber` entre 1 et 60 ; `SurahNumber` entre 1 et 114 ; `Status` valide (`IsInEnum`) ; `Notes`
  ≤ 2000 caractères (aligné sur `HasMaxLength(2000)` de la configuration EF).
- `UpdateQuranProgressCommandValidator` : `Id` non vide ; `Status` valide ; `Notes` ≤ 2000.
- `CreateQuranEvaluationCommandValidator` : `StudentId` non vide ; `EvaluationDate` non vide et non
  future (`≤ DateOnly.FromDateTime(DateTime.UtcNow)`) ; `MemoryMistakes`/`TajwidMistakes`/
  `Hesitations` ≥ 0 ; `FinalScore` ≥ 0 (décision #7 : pas de plafond).
- `UpdateQuranEvaluationCommandValidator` : `Id` non vide ; mêmes bornes que ci-dessus sur les
  quatre champs numériques et la date.

### 3.6 Ce qui n'est PAS construit dans ce lot

- Aucun écran, aucun fichier JavaScript, aucune entrée de navigation.
- Aucune vérification d'affectation enseignant↔matière/classe (décision #1).
- Aucun plafond métier sur `FinalScore` (décision #7).
- Aucune modification d'`openapi.yaml` au-delà de l'ajout des 8 nouvelles routes (section dédiée
  « Module Coran/Franco-Arabe », sur le modèle des sections Internat/Cahier de texte).

## 4. Sécurité

- `QuranController` : `[Authorize]` + `[RequireModule(SchoolModule.Coran)]` — 403 `MODULE_DISABLED`
  si l'école n'a pas activé le module (`IsCoranModuleEnabled = false`, valeur par défaut).
- Écriture (`POST`/`PUT` sur `/quran/progress` et `/quran/evaluations`) : `Authorize(Roles =
  "Directeur,Enseignant")`.
- Lecture (`GET`) : `Authorize(Roles = "Directeur,Enseignant,Secretariat")`.
- Isolation multi-tenant déjà garantie par le socle Phase 1 (Global Query Filter + RLS) : aucun
  contrôle supplémentaire nécessaire dans les Handlers au-delà de la vérification d'existence de
  l'élève/la classe dans l'école courante (même idiome que `CreateGradeCommandHandler`).
- `SchoolId` jamais lu depuis un paramètre client (règle #10) — vient de `ITenantProvider`.

## 5. Tests

- **Unitaires** : les 4 validateurs (bornes Juz/Hizb/Sourate, enums, dates futures refusées, nombres
  négatifs refusés).
- **Intégration** (catégorie `MultiTenant` incluse pour les cas d'isolation) :
  - Création réussie de `QuranProgress`/`QuranEvaluation` pour un élève de l'école courante.
  - Rejet (422 `ValidationException`) si l'élève visé appartient à une AUTRE école (même contrôle
    que Grade, prouve que le Global Query Filter fait déjà le travail d'isolement).
  - Rejet 409 sur une correction concurrente (deux `UpdateQuranProgressCommand`/
    `UpdateQuranEvaluationCommand` avec le même jeton `RowVersion` périmé).
  - `GetClassQuranProgressQuery`/`GetClassQuranEvaluationsQuery` : ne renvoient jamais un élève d'une
    autre classe ni d'une autre école.
  - Garde de module : `IsCoranModuleEnabled = false` → 403 sur toute route de `QuranController`.
  - Garde de rôle : un Secrétariat ne peut pas écrire (403), un Enseignant ne peut pas lire une école
    dont il n'est pas membre (déjà couvert par le JWT/tenant, pas un test spécifique supplémentaire).

## 6. Hors périmètre (rappel)

- Tout écran, composant Razor/Alpine ou fichier JavaScript.
- Toute vérification d'affectation enseignant↔matière (`TeacherAssignment`) pour l'écriture.
- Tout plafond métier sur la note finale d'examen oral.
- Toute évolution du réglage `IsCoranModuleEnabled`/`SchoolType` (Phase 1, inchangés).
