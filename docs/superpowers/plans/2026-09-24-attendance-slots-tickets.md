# Appel par créneau, absences partielles et billets d'entrée (Évolution N°5) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **STATUT : EXÉCUTÉ le 25/09/2026 (Tâches 1 à 8 et 10) — arbitrages B1 à B13 validés à 100 %.** La Tâche 9 (justification des séances manquées) est **hors périmètre**, non construite. Écarts constatés à l'exécution : voir la fin du document. **Complément N°5 bis (Tâches 11 à 15 : appel à trois statuts, billet par heure d'arrivée) : planifié le 25/09/2026, non exécuté — voir la fin du document.**

**Goal :** (1) Faire l'appel par créneau d'emploi du temps, pas seulement par demi-journée, et distinguer absences complètes, retards et absences partielles par matière. (2) Faire du billet d'entrée un vrai circuit : la Vie Scolaire l'émet pour un cours précis, le registre d'appel de ce cours se met à jour, et l'enseignant l'accepte en classe.

**Architecture :** L'appel garde son modèle (`AttendanceSheet` par classe, matière, date, créneau) et gagne un lien optionnel vers le `ScheduleSlot` ; le libellé du créneau (`Period`) en est **dérivé** côté serveur, ce qui laisse intacts l'index unique, les rapports et les notifications. « Complète / partielle » est une **classification calculée** sur les appels enregistrés (aucun nouveau statut persisté). Le billet d'entrée **n'est pas une nouvelle entité** : c'est le `LateArrival` existant, enrichi d'un cours visé et d'un statut (`Issued` → `Accepted`, ou `Cancelled`), dont l'émission met à jour la ligne d'appel du cours et dont l'acceptation par l'enseignant du créneau confirme le retard.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql (RLS + Global Query Filter), MediatR + FluentValidation, QuestPDF (billet), Alpine.js, xUnit + FluentAssertions, `node --test`.

**Spec :** pas de document séparé — la spécification est le message de l'utilisateur du 24/09/2026 (« Spécification Évolution N°5 ») :
1. saisie de la présence par créneau horaire : prise d'appel par cours d'emploi du temps et non plus seulement par demi-journée ; distinguer absences complètes, retards et absences partielles par matière ;
2. billets d'entrée et de sortie (Vie Scolaire) : la Vie Scolaire/Surveillance délivre un billet d'entrée en classe (justificatif de retard/absence) ; une fois émis pour un élève, son statut est mis à jour dans le registre d'appel du cours concerné et l'enseignant peut l'accepter en classe.

## Global Constraints

- PostgreSQL uniquement ; migrations EF Core **nouvelles** (jamais modifier une migration appliquée). Aucune nouvelle table dans ce plan : uniquement des colonnes sur des tables déjà sous RLS + Global Query Filter (règle #2 inchangée).
- Aucune suppression physique ; un billet annulé passe en `Cancelled`, il n'est jamais effacé (règle #6). Le retard reste historisé.
- CQRS MediatR ; aucune logique métier dans un contrôleur ni une entité ; erreurs au format normalisé (`ValidationException` → 422, 403, 409). — règles #7 à #9.
- `schoolId` et **l'auteur** (`TakenByUserId`, `AcceptedByUserId`) viennent du JWT, jamais du corps de requête (règle #10).
- **Le taux de présence ne change pas** : « (Présents + Retards) ÷ lignes d'appel saisies » (invariant figé à l'Évolution N°3, `AttendanceRateWorkingDaysTests`). Un retard reste une présence tardive, pas une absence.
- **Évolution N°3 respectée** : l'appel et son ouverture restent refusés un jour de repos ; un billet d'entrée sans cours visé reste possible ce jour-là (arbitrage D4 de l'Évolution N°3).
- Aucun nouveau statut d'appel persisté : `AttendanceStatus` (`Present`, `JustifiedAbsence`, `UnjustifiedAbsence`, `Late`) est inchangé.
- Le PDF du billet n'est pas couvert par la règle #12 (reçu, bulletin, dashboard) : on peut y ajouter des lignes ; ces trois documents-là ne changent pas.
- Le propriétaire lance lui-même la suite complète `dotnet test` (consigne du 17/09/2026) ; ce plan n'exécute que des tests **ciblés**. `dotnet build` et `node --test` restent libres.
- Migration en local : si `SamaEcole.Web` tourne, générer avec `--configuration Release` **puis appliquer `dotnet ef database update` avant de relancer l'app** (sinon « Une erreur inattendue » partout — constat du 24/09/2026).
- Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Branche : `feature/attendance-slots`, **empilée sur `feature/working-days`** (mêmes handlers d'appel, migrations à la suite).

## Constat sur ce que le code fait aujourd'hui (à connaître avant de valider)

- **Un appel est déjà « par matière et par créneau »** : `AttendanceSheet` = (classe, matière, année, date, `Period`), où `Period` est un **texte libre** (« Matin », « 1re heure »…), avec un index unique sur (classe, matière, date, créneau). Mais la fiche **ne sait rien de l'emploi du temps** : rien ne relie un appel à un `ScheduleSlot`, rien ne vérifie que ce cours a lieu ce jour-là, et l'enseignant n'est contrôlé que sur ses **affectations** (classe/matière), pas sur ses créneaux.
- **Absence « partielle par matière » existe déjà à la saisie** : une absence à la fiche d'une matière n'est PAS une absence aux autres. Ce qui manque est la **lecture** : « absent toute la journée » ou « absent à un cours seulement » n'apparaît nulle part ; le rapport (`AttendanceReportAggregator`) ne compte que des lignes.
- **Le billet d'entrée est aujourd'hui l'impression d'un retard** (`LateArrival` : élève, date, minutes, motif, observations). `GetEntryTicketQuery` le documente en toutes lettres : « le billet n'est pas une entité à part ». **Il n'est relié ni à un cours, ni à l'appel** : émettre un billet ne change aucun statut, et l'enseignant ne le voit pas.
- **Notifications** : `SubmitAttendanceSheetCommandHandler` publie un `AttendanceRecordedEvent` pour chaque retard/absence, dans la transaction de l'appel ; `NotifyParentOnAttendanceEventHandler` envoie WhatsApp + SMS (Premium). Un élève marqué absent puis autorisé à entrer par un billet a donc **déjà** fait prévenir sa famille.
- **Rôles** : la saisie des retards (`AbsenceController`) est `SuperAdmin`/`Directeur`/`Surveillant` ; l'impression des billets (`BilletsController`) ajoute le Secrétariat ; l'appel (`AttendanceController`) est ouvert à l'Enseignant (ses classes), Directeur, Secrétariat et Surveillant.
- **Le fuseau** de l'école est UTC+0 (Dakar) : « le cours en cours » se déduit de l'heure UTC sans décalage (même hypothèse que l'Évolution N°3).

## Arbitrages à valider avant de commencer

| # | Question | Recommandation (appliquée par ce plan) |
|---|---|---|
| B1 | **Remplacer l'appel libre par l'appel par créneau, ou garder les deux ?** | **Garder les deux.** Mode « Par créneau » (défaut quand la classe a des cours ce jour-là) et mode « Libre » (demi-journée, texte libre) — les écoles sans emploi du temps saisi, ou l'appel du matin global, continuent de fonctionner sans changement. |
| B2 | **Lien fiche ↔ créneau** | `AttendanceSheet.ScheduleSlotId` **nullable**. Avec un créneau, le serveur **vérifie** (classe, matière, jour de la semaine = celui de la date) et **dérive** `Period` = « 08:00-10:00 » (le `Period` du client est ignoré). L'index unique existant suffit : deux créneaux distincts ont des horaires distincts. |
| B3 | **Qui peut faire l'appel d'un créneau ?** | Enseignant : **uniquement ses propres créneaux** (propriétaire du créneau) **et** ses affectations — comme le Cahier de texte. Directeur, Secrétariat, Surveillant : n'importe quel créneau (c'est le cas du **remplaçant**). |
| B4 | **Absence complète / partielle / retard : nouveaux statuts ou classification ?** | **Classification calculée**, aucun nouveau statut. Par (élève, date), sur les séances **appelées** ce jour-là : `FullAbsence` (absent à toutes), `PartialAbsence` (absent à au moins une et présent ou en retard à une autre), `Late` (aucune absence, au moins un retard), `Present`. La couverture (« 2 séances appelées sur 5 prévues ») est affichée, car une seule fiche saisie ne prouve pas une journée. Justifiée/injustifiée reste porté par le statut de chaque ligne. |
| B5 | **Billet : nouvelle entité ou extension de `LateArrival` ?** | **Extension de `LateArrival`** (numéro de billet, PDF, historique conservés). Nouveau : cours visé (`TargetScheduleSlotId`), statut nullable (`Issued`/`Accepted`/`Cancelled`), acceptation (qui, quand). **Statut `null` = billet sans cours visé** = comportement actuel, y compris pour tout l'historique existant : aucune migration de données. |
| B6 | **Quand le registre change-t-il ?** | **À l'émission**, comme la spécification le demande : si la fiche du cours existe, la ligne de l'élève passe à `Late` (minutes du billet) et est liée au billet ; le statut **précédent est conservé** sur le billet. Si la fiche n'existe pas encore, la feuille d'appel de l'enseignant **présélectionne « Retard — billet »** pour cet élève. **À l'acceptation** par l'enseignant, le retard est confirmé (et, si l'enseignant avait saisi « Absent » entre-temps, la ligne repasse à `Late`). **Annuler un billet non accepté restaure** le statut précédent. |
| B7 | **Le billet justifie-t-il aussi les séances manquées plus tôt ce jour-là ?** | **Non par défaut** (le billet vaut pour son cours visé). Option **Tâche 9, à activer seulement si vous la validez** : case « Justifier les séances manquées ce jour-là » qui fait passer `UnjustifiedAbsence` → `JustifiedAbsence` sur les fiches déjà saisies ce jour-là, jamais l'inverse. |
| B8 | **SMS/WhatsApp quand un billet corrige une absence déjà notifiée** | **Réutiliser `AttendanceRecordedEvent`** quand une ligne passe d'une absence à `Late` par un billet : la famille reçoit « en retard de X minutes » — c'est la rectification, sans nouveau modèle de message. Aucun événement quand la fiche n'existe pas encore (le retard sera notifié normalement à la soumission). L'envoi reste gouverné par `SmsDispatcher` (formule, activation, solde). |
| B9 | **Qui accepte un billet ?** | L'**enseignant titulaire du créneau visé** (via `Teacher.UserId`) et le **Directeur**. Pas le Surveillant ni le Secrétariat (spécification : « l'enseignant l'accepte en classe »). Un billet accepté ne s'annule plus : une erreur se corrige par un nouvel appel sur la ligne, pas en défaisant le billet. |
| B10 | **Périmètre billet de sortie / jours de repos** | **Billet de sortie hors périmètre** (la spécification ne demande que l'entrée ; il reste inchangé). **Un billet d'entrée peut être émis sans cours visé** (statut `null`), notamment un jour de repos ou quand la classe n'a pas d'emploi du temps. |
| B11 | **Combien de cours par billet ?** | **Un seul cours visé par billet.** Un élève en retard le matin reçoit un billet pour le cours qu'il rejoint ; les suivants n'ont besoin de rien. Au plus **un billet actif** (`Issued`/`Accepted`) par (élève, créneau, date) : index unique partiel. |
| B12 | **Qui émet ?** | Inchangé : `SuperAdmin`, `Directeur`, `Surveillant` (émission) ; le Secrétariat imprime. La Vie Scolaire voit, pour la classe de l'élève, les cours du jour et **celui en cours ou le prochain** est présélectionné. |
| B13 | **Un enseignant est-il averti ?** | Pas de notification poussée : le billet apparaît **dans sa feuille d'appel** (pastille « Billet d'entrée — en attente d'acceptation » + bouton « Accepter »). Cela suffit dès que l'appel se fait par créneau, sans nouveau canal. |

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Application/Attendance/SlotPeriod.cs` (créer) | Pur : libellé « HH:mm-HH:mm » et vérification créneau ↔ (classe, matière, date). |
| `src/SamaEcole.Application/Attendance/DayAttendanceClassifier.cs` (créer) | Pur : classification `FullAbsence`/`PartialAbsence`/`Late`/`Present`. |
| `src/SamaEcole.Domain/Enums/EntryTicketStatus.cs` (créer) | `Issued`, `Accepted`, `Cancelled`. |
| `src/SamaEcole.Domain/Entities/{AttendanceSheet,StudentAttendance,LateArrival}.cs` (modifier) | Nouvelles colonnes. |
| `src/SamaEcole.Persistence/Configurations/*`, `Migrations/*` (modifier/générer) | Colonnes, FK, index. |
| `Attendance/Queries/GetAttendanceSlots/*` (créer) | Cours du jour d'une classe (+ état de l'appel). |
| `Attendance/Commands/SubmitAttendanceSheet/*`, `Queries/InitializeAttendanceSheet/*` (modifier) | Créneau, dérivation de `Period`, billets. |
| `Attendance/EntryTickets/*` (créer) | Émission, acceptation, annulation, application au registre. |
| `Absences/Commands/CreateLateArrival/*` (modifier) | Cours visé optionnel. |
| `Reports/AttendanceReportAggregator.cs`, `Reports/Queries/GetAttendanceReport/*` (modifier) ; `GetAttendanceBySubjectReport/*` (créer) | Classification et vue par matière. |
| `Absences/Queries/GetEntryTicket/*`, `Infrastructure/Documents/EntryTicket*.cs` (modifier) | Cours visé et statut sur le billet. |
| `Web/Controllers/{AttendanceController,BilletsController,AbsenceController}.cs` (modifier) | Routes fines. |
| `wwwroot/js/{attendance,billets,attendance-report}.js`, `Views/{Attendance,Billets*,Reports*}` (modifier), `help.js` | Interface. |
| `tests/SamaEcole.UnitTests/Attendance/*`, `tests/SamaEcole.IntegrationTests/{Attendance,VieScolaire}/*`, `src/SamaEcole.Web/tests/js/attendance-slots.test.mjs` | Tests. |

---

### Task 1 : `SlotPeriod` et `DayAttendanceClassifier` (purs, sans base)

**Files:**
- Create: `src/SamaEcole.Application/Attendance/SlotPeriod.cs`, `src/SamaEcole.Application/Attendance/DayAttendanceClassifier.cs`
- Test: `tests/SamaEcole.UnitTests/Attendance/SlotPeriodTests.cs`, `…/DayAttendanceClassifierTests.cs`

**Interfaces:**
- Produces (namespace `SamaEcole.Application.Attendance`) :
  - `static class SlotPeriod` : `string Label(TimeOnly start, TimeOnly end)` → `"08:00-10:00"` ; `string? Mismatch(ScheduleSlot slot, Guid classroomId, Guid subjectId, DateOnly date)` → message français ou `null` si le créneau convient
  - `enum DayAttendanceKind { Present, Late, PartialAbsence, FullAbsence }`
  - `static class DayAttendanceClassifier` : `DayAttendanceKind Classify(IEnumerable<AttendanceStatus> sessionStatuses)` (liste vide → `Present` : aucune séance appelée ne fait pas une absence)

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
// tests/SamaEcole.UnitTests/Attendance/DayAttendanceClassifierTests.cs
using FluentAssertions;
using SamaEcole.Application.Attendance;
using SamaEcole.Domain.Enums;
using Xunit;
using S = SamaEcole.Domain.Enums.AttendanceStatus;

namespace SamaEcole.UnitTests.Attendance;

public class DayAttendanceClassifierTests
{
    [Fact]
    public void Absent_To_Every_Recorded_Session_Is_A_Full_Absence()
        => DayAttendanceClassifier.Classify([S.UnjustifiedAbsence, S.JustifiedAbsence, S.UnjustifiedAbsence])
            .Should().Be(DayAttendanceKind.FullAbsence);

    [Fact]
    public void Absent_To_One_Session_But_Present_To_Another_Is_A_Partial_Absence()
        => DayAttendanceClassifier.Classify([S.Present, S.UnjustifiedAbsence])
            .Should().Be(DayAttendanceKind.PartialAbsence);

    [Fact]
    public void Absent_Then_Late_Counts_As_Partial_Because_The_Student_Did_Come()
        => DayAttendanceClassifier.Classify([S.UnjustifiedAbsence, S.Late])
            .Should().Be(DayAttendanceKind.PartialAbsence);

    [Fact]
    public void A_Late_Without_Any_Absence_Stays_A_Late()
        => DayAttendanceClassifier.Classify([S.Present, S.Late]).Should().Be(DayAttendanceKind.Late);

    [Fact]
    public void All_Present_Is_Present()
        => DayAttendanceClassifier.Classify([S.Present, S.Present]).Should().Be(DayAttendanceKind.Present);

    [Fact]
    public void No_Recorded_Session_Is_Not_An_Absence()
        => DayAttendanceClassifier.Classify([]).Should().Be(DayAttendanceKind.Present);
}
```

```csharp
// tests/SamaEcole.UnitTests/Attendance/SlotPeriodTests.cs
using FluentAssertions;
using SamaEcole.Application.Attendance;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Attendance;

public class SlotPeriodTests
{
    private static readonly Guid Classe = Guid.NewGuid();
    private static readonly Guid Matiere = Guid.NewGuid();

    private static ScheduleSlot Slot(DayOfWeek day = DayOfWeek.Saturday) => new()
    {
        ClassroomId = Classe, SubjectId = Matiere, DayOfWeek = day,
        StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
    };

    [Fact]
    public void Label_Is_Start_Dash_End_In_24h()
        => SlotPeriod.Label(new TimeOnly(8, 0), new TimeOnly(10, 30)).Should().Be("08:00-10:30");

    [Fact]
    public void A_Matching_Slot_Has_No_Mismatch()
        => SlotPeriod.Mismatch(Slot(), Classe, Matiere, new DateOnly(2026, 9, 26)).Should().BeNull(); // samedi

    [Fact]
    public void A_Slot_Of_Another_Weekday_Is_Refused()
        => SlotPeriod.Mismatch(Slot(), Classe, Matiere, new DateOnly(2026, 9, 24)) // jeudi
            .Should().Contain("samedi");

    [Fact]
    public void A_Slot_Of_Another_Class_Or_Subject_Is_Refused()
    {
        SlotPeriod.Mismatch(Slot(), Guid.NewGuid(), Matiere, new DateOnly(2026, 9, 26)).Should().Contain("classe");
        SlotPeriod.Mismatch(Slot(), Classe, Guid.NewGuid(), new DateOnly(2026, 9, 26)).Should().Contain("matière");
    }
}
```

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~SlotPeriod|FullyQualifiedName~DayAttendanceClassifier"` → erreur de compilation.

- [ ] **Step 3 : implémenter**

```csharp
// src/SamaEcole.Application/Attendance/DayAttendanceClassifier.cs
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance;

public enum DayAttendanceKind { Present, Late, PartialAbsence, FullAbsence }

/// <summary>
/// Classe la JOURNÉE d'un élève d'après ses statuts de séance (Évolution N°5, arbitrage B4). Pur.
/// Calculée, jamais persistée : aucun nouveau statut d'appel. Elle ne porte que sur les séances
/// APPELÉES — l'appelant expose aussi la couverture (« 2 séances appelées sur 5 »), car une seule
/// fiche saisie ne prouve pas une journée.
/// </summary>
public static class DayAttendanceClassifier
{
    public static DayAttendanceKind Classify(IEnumerable<AttendanceStatus> sessionStatuses)
    {
        var statuses = sessionStatuses.ToList();
        var absences = statuses.Count(IsAbsence);

        if (statuses.Count == 0) return DayAttendanceKind.Present;
        if (absences == statuses.Count) return DayAttendanceKind.FullAbsence;
        if (absences > 0) return DayAttendanceKind.PartialAbsence;
        return statuses.Contains(AttendanceStatus.Late) ? DayAttendanceKind.Late : DayAttendanceKind.Present;
    }

    private static bool IsAbsence(AttendanceStatus s)
        => s is AttendanceStatus.JustifiedAbsence or AttendanceStatus.UnjustifiedAbsence;
}
```

```csharp
// src/SamaEcole.Application/Attendance/SlotPeriod.cs
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Attendance;

/// <summary>
/// Appel par créneau d'emploi du temps (Évolution N°5, arbitrage B2). Le libellé du créneau est DÉRIVÉ du
/// cours — jamais saisi — pour que l'index unique (classe, matière, date, créneau), les rapports et les
/// notifications, qui lisent tous <c>Period</c>, restent inchangés.
/// </summary>
public static class SlotPeriod
{
    public static string Label(TimeOnly start, TimeOnly end) => $"{start:HH\\:mm}-{end:HH\\:mm}";

    /// <summary>Message français si le créneau ne convient pas à (classe, matière, date), sinon null.</summary>
    public static string? Mismatch(ScheduleSlot slot, Guid classroomId, Guid subjectId, DateOnly date)
    {
        if (slot.ClassroomId != classroomId) return "Ce créneau n'appartient pas à la classe indiquée.";
        if (slot.SubjectId != subjectId) return "Ce créneau n'est pas celui de la matière indiquée.";
        if (slot.DayOfWeek != date.DayOfWeek)
        {
            return $"Ce créneau a lieu le {SchoolWeek.FrenchName(slot.DayOfWeek)}, pas le {SchoolWeek.FrenchName(date.DayOfWeek)} {date:dd/MM/yyyy}.";
        }

        return null;
    }
}
```

- [ ] **Step 4 : constater le succès** — même commande → PASS.
- [ ] **Step 5 : commit** — `feat(attendance): helpers purs du créneau d'appel et de la classification de journée`

---

### Task 2 : Modèle de données (créneau sur la fiche, circuit du billet)

**Files:**
- Create: `src/SamaEcole.Domain/Enums/EntryTicketStatus.cs`
- Modify: `AttendanceSheet.cs` (`Guid? ScheduleSlotId`), `StudentAttendance.cs` (`Guid? EntryTicketId`), `LateArrival.cs` (voir ci-dessous) ; leurs `*Configuration.cs`
- Create (générées) : migrations `AddAttendanceSheetScheduleSlot`, `AddEntryTicketWorkflow`
- Test: `tests/SamaEcole.IntegrationTests/VieScolaire/EntryTicketSchemaTests.cs`

**Interfaces:**
- Produces :
  - `enum EntryTicketStatus { Issued, Accepted, Cancelled }` (persisté en **texte**, comme le reste du projet)
  - `LateArrival` : `Guid? TargetScheduleSlotId` ; `EntryTicketStatus? Status` ; `Guid? AcceptedByUserId` ; `DateTimeOffset? AcceptedAt` ; `Guid? CancelledByUserId` ; `DateTimeOffset? CancelledAt` ; `AttendanceStatus? PreviousStatus` ; `int? PreviousLateMinutes` (valeur de la ligne d'appel avant que le billet ne la change, pour la restaurer à l'annulation)
  - Index unique partiel sur `late_arrivals` : `(StudentId, TargetScheduleSlotId, Date)` où `Status IN ('Issued','Accepted') AND NOT IsDeleted` (au plus un billet actif par élève, cours et jour — B11)
  - Index sur `attendance_sheets("ScheduleSlotId")` ; FK vers `schedule_slots` en `Restrict`

- [ ] **Step 1 : tests d'intégration qui échouent** (SQL brut sous le rôle applicatif, comme `AttendanceIsolationTests`) : les billets existants gardent `Status = NULL` après migration ; deux billets `Issued` pour le même (élève, cours, jour) → `UniqueViolation` ; un billet `Cancelled` laisse en émettre un autre ; deux billets sans cours visé le même jour restent permis ; **une école ne lit pas les billets d'une autre** (`[Trait("Category","MultiTenant")]`).
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** entités et configurations ; `Status` en `HasConversion<string>().HasMaxLength(20)` **sans valeur par défaut** (NULL = billet sans cours visé, arbitrage B5) ; migrations : un seul lot de `AddColumn`/`CreateIndex` par migration, diff de snapshot limité à ces colonnes.
- [ ] **Step 4 : constater le succès** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~EntryTicketSchemaTests"` ; **appliquer les migrations au dev**.
- [ ] **Step 5 : commit** — `feat(attendance): créneau sur la fiche d'appel et circuit du billet d'entrée (colonnes, index)`

---

### Task 3 : Appel par créneau (API)

**Files:**
- Create: `Attendance/Queries/GetAttendanceSlots/{GetAttendanceSlotsQuery,Handler,Dto}.cs`
- Modify: `SubmitAttendanceSheetCommand.cs` + `Handler` + validateur (`Guid? ScheduleSlotId`), `InitializeAttendanceSheetQuery.cs` + `Handler` (idem), `AttendanceController.cs`
- Test: `tests/SamaEcole.IntegrationTests/Attendance/AttendanceBySlotTests.cs`

**Interfaces:**
- Consumes : `SlotPeriod`, `AttendanceSheet.ScheduleSlotId` (Tâches 1-2).
- Produces :
  - `GET /api/v1/attendance/slots?classroomId={id}&date=YYYY-MM-DD` (mêmes rôles que la saisie) → `[{slotId, subjectId, subjectName, teacherId, teacherName, start, end, label, sheetId?, isTakenByMe?}]` ; jour de repos ou aucun cours → liste vide ; **un Enseignant ne reçoit que ses propres créneaux**
  - `SubmitAttendanceSheetCommand.ScheduleSlotId` et `InitializeAttendanceSheetQuery.ScheduleSlotId` (**dernier membre, optionnel**). Avec créneau : le serveur charge le créneau, refuse (422) si `SlotPeriod.Mismatch(...)` ≠ null, refuse (403) si l'utilisateur est Enseignant et n'est pas le titulaire du créneau, **écrit `Period = SlotPeriod.Label(...)` quelle que soit la valeur du client**, renseigne `AttendanceSheet.ScheduleSlotId`. Sans créneau : comportement d'avant, à l'identique (B1).

- [ ] **Step 1 : tests d'intégration qui échouent** (montage : école, année active, classe, deux matières, deux enseignants avec fiche et compte, deux créneaux le samedi, jour de repos jeudi) : (1) appel sur un créneau valide → fiche avec `ScheduleSlotId` et `Period = "08:00-10:00"`, même si le client envoie `Period = "n'importe quoi"` ; (2) créneau d'une autre classe / autre matière / autre jour → 422 lisible, rien d'écrit ; (3) Enseignant qui n'est pas titulaire du créneau → 403 ; Surveillant sur le même créneau → accepté (remplaçant) ; (4) même créneau, même date, deuxième soumission → 409 (index unique) ; (5) sans `ScheduleSlotId`, comportement libre inchangé (`Period = "Matin"`) ; (6) `GET slots` : un jeudi de repos → liste vide ; un Enseignant ne voit que les siens ; `sheetId` renseigné une fois l'appel fait ; (7) ouverture de feuille (`InitializeAttendanceSheet`) avec créneau vérifiée comme la soumission ; (8) `[Trait("Category","MultiTenant")]` : le créneau d'une autre école est introuvable (422), jamais accepté.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter.** Dans les deux handlers, après le contrôle de portée existant (`scopeAuthorizer`) et **avant** le `WorkingDayGuard` de l'Évolution N°3 :

```csharp
        string period = request.Period.Trim();
        Guid? slotId = null;
        if (request.ScheduleSlotId is { } requestedSlotId)
        {
            var slot = await dbContext.ScheduleSlots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == requestedSlotId, cancellationToken)
                ?? throw new ValidationException([new ValidationFailure(nameof(request.ScheduleSlotId),
                    "Ce créneau n'existe pas dans votre établissement.")]);

            var mismatch = SlotPeriod.Mismatch(slot, request.ClassroomId, request.SubjectId, request.Date);
            if (mismatch is not null)
                throw new ValidationException([new ValidationFailure(nameof(request.ScheduleSlotId), mismatch)]);

            await scopeAuthorizer.EnsureOwnsSlotAsync(slot, cancellationToken); // 403 pour un Enseignant non titulaire
            period = SlotPeriod.Label(slot.StartTime, slot.EndTime);
            slotId = slot.Id;
        }
```

  `AttendanceScopeAuthorizer.EnsureOwnsSlotAsync` : Directeur/Secrétariat/Surveillant → retour ; Enseignant → sa fiche (`Teacher.UserId`) doit égaler `slot.TeacherId`, sinon `UnauthorizedAccessException` (403, message qui indique l'action corrective : « Ce cours est assuré par un autre enseignant : demandez à la Surveillance de faire l'appel »). Le validateur de `Period` n'exige plus qu'il soit non vide **quand** un créneau est fourni.
- [ ] **Step 4 : constater le succès** — `--filter "FullyQualifiedName~AttendanceBySlotTests|FullyQualifiedName~WorkingDayLockTests|FullyQualifiedName~AttendanceIsolationTests"` (non-régression Évolution N°3 et isolation).
- [ ] **Step 5 : commit** — `feat(attendance): appel par créneau d'emploi du temps (lien, dérivation du créneau, contrôle du titulaire)`

---

### Task 4 : Rapports — absence complète, partielle, retard, par matière

**Files:**
- Modify: `Reports/AttendanceReportAggregator.cs` (+ `StudentAttendanceReportRow`), la requête et les exports du rapport d'assiduité (R02/R03)
- Create: `Reports/Queries/GetAttendanceBySubjectReport/*`
- Modify: `Web/Controllers/ReportsController.cs`
- Test: `tests/SamaEcole.IntegrationTests/Reports/AttendanceClassificationTests.cs`

**Interfaces:**
- Consumes : `DayAttendanceClassifier` (Tâche 1).
- Produces : `StudentAttendanceReportRow` gagne **en fin de liste** `int FullAbsenceDays`, `int PartialAbsenceDays`, `int LateOnlyDays`, `int SessionsRecorded` ; `GET /api/v1/reports/attendance/by-subject?classId=&startDate=&endDate=` → `[{subjectId, subjectName, sessions, presents, lates, justifiedAbsences, unjustifiedAbsences, rate}]`. **`AverageAttendanceRate` et le taux par élève sont inchangés** (aucune formule modifiée).

- [ ] **Step 1 : tests d'intégration qui échouent** : (1) un élève absent aux 3 séances d'un samedi → `FullAbsenceDays = 1`, `PartialAbsenceDays = 0` ; (2) absent à 1 séance sur 3 → `PartialAbsenceDays = 1` ; (3) absent puis retard → partielle ; (4) retard seul → `LateOnlyDays = 1` ; (5) **le taux moyen est identique** à celui d'avant sur le même jeu (invariant N°3) ; (6) `by-subject` compte par matière, le total des lignes égale celui du rapport élève ; (7) filtre de classe hors périmètre → 422 (garde existante de l'agrégateur) ; (8) isolation entre écoles.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** — regrouper les lignes par (élève, `sheet.Date`) et appeler `DayAttendanceClassifier` ; les exports PDF/CSV gagnent les colonnes « Jours d'absence complète », « Jours d'absence partielle » ; l'écran affiche « (2 séances appelées) » en info-bulle (couverture, B4).
- [ ] **Step 4 : constater le succès** — `--filter "FullyQualifiedName~AttendanceClassificationTests|FullyQualifiedName~AttendanceRateWorkingDaysTests|FullyQualifiedName~AttendanceReport"`.
- [ ] **Step 5 : commit** — `feat(attendance): absences complètes, partielles et vue par matière dans le rapport d'assiduité`

---

### Task 5 : Émettre un billet d'entrée pour un cours et mettre à jour le registre

**Files:**
- Modify: `Absences/Commands/CreateLateArrival/{Command,Validator,Handler}.cs` (`Guid? TargetScheduleSlotId`), `AbsenceController.cs`
- Create: `Attendance/EntryTickets/EntryTicketRegister.cs` (service scoped « application au registre »), `Attendance/Queries/GetTodaySlotsForStudent/*`
- Modify: `InitializeAttendanceSheetQueryHandler.cs` (présélection), `SubmitAttendanceSheetCommandHandler.cs` (fusion), `AttendanceRosterRow` (champs billet)
- Test: `tests/SamaEcole.IntegrationTests/VieScolaire/EntryTicketRegisterTests.cs`

**Interfaces:**
- Consumes : `LateArrival` enrichi (Tâche 2), `SlotPeriod`, `AttendanceRecordedEvent`.
- Produces :
  - `POST /api/v1/absences/late-arrivals` accepte `targetScheduleSlotId` (optionnel) → avec cours visé : statut `Issued`, contrôle (créneau de la **classe de l'élève**, `dayOfWeek` = celui de `date`), application au registre ; sans : statut `null`, comportement d'avant
  - `GET /api/v1/absences/today-slots?studentId={id}&date=` → cours du jour de la classe de l'élève, avec `isCurrent`/`isNext` (heure UTC, arbitrage B12)
  - `EntryTicketRegister.ApplyAsync(LateArrival ticket, CancellationToken ct)` : **si la fiche (créneau, date) existe** — mémorise `PreviousStatus`/`PreviousLateMinutes` sur le billet, met la ligne de l'élève à `Late` + `LateMinutes = ticket.Minutes` + `EntryTicketId`, **publie `AttendanceRecordedEvent` seulement si la ligne passait d'une absence à un retard** (B8) ; **sinon** ne fait rien (la feuille d'appel présélectionnera le retard)
  - Feuille d'appel (`InitializeAttendanceSheet`) : pour les élèves ayant un billet `Issued`/`Accepted` sur (créneau, date) → `status = "Late"`, `lateMinutes` du billet, champs `entryTicketId`, `entryTicketNumber`, `entryTicketStatus`. `SubmitAttendanceSheet`, **après** l'insertion des lignes et dans la même transaction, relit les billets actifs du (créneau, date) et **rattache** `EntryTicketId` aux lignes dont l'élève a un billet (sans écraser un statut que l'enseignant a explicitement mis à `Absent` : le billet reste `Issued`, non accepté).

- [ ] **Step 1 : tests d'intégration qui échouent** : (1) fiche déjà saisie, élève `UnjustifiedAbsence` → après émission la ligne est `Late` (minutes du billet), `PreviousStatus = UnjustifiedAbsence`, événement publié **une fois** ; (2) fiche déjà saisie, élève `Present` → la ligne passe `Late`, **aucun** événement d'absence ; (3) fiche absente → aucune ligne créée ; la feuille d'appel renvoie l'élève pré-marqué `Late` avec son billet ; à la soumission, la ligne porte `EntryTicketId` ; (4) l'enseignant soumet `Absent` pour un élève ayant un billet → la ligne reste `Absent`, le billet reste `Issued` (non accepté) et la feuille le signale ; (5) créneau d'une autre classe que celle de l'élève / autre jour → 422 ; (6) deuxième billet actif pour (élève, cours, jour) → 409 ; (7) billet **sans** cours visé → comportement d'avant (aucun changement de registre, statut `null`) ; (8) un billet un jour de repos **sans** cours visé passe (D4 de l'Évolution N°3) ; (9) `[Trait("Category","MultiTenant")]` : impossible de viser le créneau ou l'élève d'une autre école ; (10) le taux de présence de la période reflète le passage `Absent → Late` (Late compte comme présent) — assertion sur `AttendanceReportAggregator`.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** `EntryTicketRegister` et le branchement ; l'émission et l'application au registre se font **dans une seule transaction** (`ExecuteInTransactionAsync`) ; l'événement est publié après `SaveChanges`, comme dans `SubmitAttendanceSheetCommandHandler`.
- [ ] **Step 4 : constater le succès** — `--filter "FullyQualifiedName~EntryTicketRegisterTests|FullyQualifiedName~AttendanceBySlotTests|FullyQualifiedName~NotifyParent"`.
- [ ] **Step 5 : commit** — `feat(attendance): billet d'entrée visant un cours — registre d'appel mis à jour et présélection de la feuille`

---

### Task 6 : L'enseignant accepte le billet en classe (et annulation)

**Files:**
- Create: `Attendance/EntryTickets/{AcceptEntryTicketCommand,CancelEntryTicketCommand}/*` (commande, handler, validateur)
- Modify: `BilletsController.cs` (routes), `EntryTicketRegister.cs` (`RestoreAsync`)
- Test: `tests/SamaEcole.IntegrationTests/VieScolaire/EntryTicketAcceptanceTests.cs`

**Interfaces:**
- Produces (toutes `IAuditableRequest`) :
  - `POST /api/v1/billets/{id}/accept` — **Enseignant titulaire du créneau visé** ou Directeur (B9). `Issued` → `Accepted`, renseigne `AcceptedByUserId`/`AcceptedAt` (jamais lus du corps). Si la fiche existe et que la ligne n'est pas `Late`, elle repasse à `Late` (statut précédent conservé). **Idempotent** : accepter deux fois renvoie 200 sans rien changer. Billet sans cours visé ou `Cancelled` → 422.
  - `POST /api/v1/billets/{id}/cancel` — `Directeur`, `Surveillant` (émetteurs). Seulement si `Issued` : `Cancelled`, et **restaure** `PreviousStatus`/`PreviousLateMinutes` sur la ligne liée (sans événement). Billet `Accepted` → 422 « déjà accepté ».

- [ ] **Step 1 : tests d'intégration qui échouent** : (1) l'enseignant titulaire accepte → `Accepted`, ligne `Late` ; (2) un autre enseignant → 403 ; (3) Surveillant/Secrétariat → 403 ; Directeur → accepté ; (4) double acceptation → 200 idempotent, `AcceptedAt` inchangé ; (5) l'enseignant avait saisi `Absent` puis accepte → la ligne repasse `Late` et le statut précédent est conservé ; (6) annulation d'un `Issued` restaure le statut d'avant ; (7) annuler un `Accepted` → 422 ; (8) accepter un billet annulé → 422 ; (9) entrée d'audit créée pour chaque action ; (10) `[Trait("Category","MultiTenant")]` : un billet d'une autre école est introuvable (404).
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter.** La vérification « enseignant titulaire » rejoue la remontée compte → fiche → `slot.TeacherId` (même patron que `ScheduleOwnershipAuthorizer`), sans jamais accepter un identifiant d'enseignant fourni par le client.
- [ ] **Step 4 : constater le succès** — `--filter "FullyQualifiedName~EntryTicketAcceptanceTests|FullyQualifiedName~EntryTicketRegisterTests"`.
- [ ] **Step 5 : commit** — `feat(attendance): acceptation du billet d'entrée par l'enseignant du créneau, annulation par la Vie Scolaire`

---

### Task 7 : Le billet imprimé porte le cours visé et son statut

**Files:**
- Modify: `Absences/Queries/GetEntryTicket/GetEntryTicketQuery.cs` (`EntryTicketDto` : `TargetSubjectName?`, `TargetTimeRange?`, `TargetTeacherName?`, `Status?`), `GetEntryTicketPdfQuery`, le document QuestPDF du billet (`Infrastructure/Documents/EntryTicket*.cs`)
- Test: `tests/SamaEcole.UnitTests/Absences/EntryTicketPdfGeneratorTests.cs` (existant, étendu), `tests/SamaEcole.IntegrationTests/VieScolaire/EntryTicketDtoTests.cs`

**Interfaces:** nouveaux membres du DTO **en fin de liste, optionnels** (`= null`) ; un billet sans cours visé s'imprime exactement comme avant. Numéro de billet inchangé (`BILLET-XXXXXXXX`).

- [ ] **Step 1 : tests qui échouent** — le DTO d'un billet avec cours visé contient matière, plage horaire, enseignant et statut ; celui d'un billet sans cours visé a ces champs `null` et le PDF est généré sans ligne supplémentaire ; le PDF d'un billet `Accepted` mentionne « Accepté ».
- [ ] **Step 2 : constater l'échec.** **Step 3 : implémenter.** **Step 4 : constater le succès** — `--filter "FullyQualifiedName~EntryTicket"`.
- [ ] **Step 5 : commit** — `feat(attendance): cours visé et statut sur le billet d'entrée imprimé`

---

### Task 8 : Interface — appel par créneau, billets et rapports

**Files:**
- Modify: `wwwroot/js/attendance.js` et `Views/Attendance/Index.cshtml` (mode « Par créneau » / « Libre », pastilles de cours, pastille « Billet d'entrée — en attente d'acceptation » + bouton « Accepter » sur la ligne de l'élève), `wwwroot/js/billets.js` et sa vue (choix du cours visé, présélection du cours en cours ou du prochain, colonne « Statut » du billet, bouton « Annuler »), `wwwroot/js/attendance-report.js` et sa vue (colonnes complètes/partielles, onglet « Par matière »), `wwwroot/js/help.js`
- Test: `src/SamaEcole.Web/tests/js/attendance-slots.test.mjs`

**Interfaces:** aucune règle métier côté client : les cours, la classification et le statut viennent du serveur ; l'écran ne fait que présenter et appeler les routes des Tâches 3 à 6. Le confort d'affichage de l'Évolution N°3 (`isRestDay`) reste prioritaire : un jour de repos n'affiche aucun cours.

- [ ] **Step 1 : tests JS qui échouent** (harnais `harness.mjs`, comme `working-days-config.test.mjs`) : le mode « Par créneau » est celui par défaut quand `GET /attendance/slots` renvoie des cours, « Libre » sinon ; choisir un cours charge la feuille avec `scheduleSlotId` et **n'envoie pas** de `period` saisi ; une ligne portant un billet affiche la pastille et « Accepter » **seulement** pour l'Enseignant titulaire ; « Accepter » appelle `POST /billets/{id}/accept` puis recharge ; un jour de repos n'affiche aucun cours ; l'écran des billets présélectionne le cours marqué `isCurrent`, à défaut `isNext` ; annuler n'est proposé que pour un billet `Issued`.
- [ ] **Step 2 : constater l'échec.** **Step 3 : implémenter.**
- [ ] **Step 4 : constater le succès** — `node --test tests/js/*.test.mjs` ; `dotnet build src/SamaEcole.Web -c Release` ; `npm run build:css --prefix src/SamaEcole.Web`. Mettre à jour `help.test.mjs`/`help-render.test.mjs` si le texte d'aide est le contrat (fiches « Appel en classe », « Billets d'entrée », « Rapport d'assiduité »).
- [ ] **Step 5 : commit** — `feat(attendance): appel par créneau, billets d'entrée et rapports — interface`

---

### Task 9 (OPTIONNELLE — B7) : Le billet justifie les séances manquées plus tôt ce jour-là

**À n'exécuter que si vous validez B7 = oui.** Ajoute `LateArrival.JustifiesEarlierAbsences` (booléen, faux par défaut) ; à l'émission, les lignes `UnjustifiedAbsence` de l'élève sur les fiches déjà saisies ce jour-là **antérieures au cours visé** passent à `JustifiedAbsence` (jamais l'inverse, jamais une ligne `Present`/`Late`). Or un billet ne conserve qu'**un** statut précédent (celui de la ligne du cours visé) : pour pouvoir annuler proprement, plusieurs lignes doivent chacune garder le leur. Cette option exige donc une petite table de journal (`attendance_status_changes`) — nouvelle table tenant, donc RLS + Global Query Filter + reset + purge, comme à l'Évolution N°4. C'est la raison pour laquelle elle n'est **pas** dans le périmètre par défaut.

---

### Task 10 : Documentation et contexte actif

**Files:**
- Modify: `openapi.yaml` (`/attendance/slots`, `scheduleSlotId` sur roster/submit, `/absences/late-arrivals` : `targetScheduleSlotId`, `/absences/today-slots`, `/billets/{id}/accept|cancel`, `/reports/attendance/by-subject`, champs de rapport), `docs/Volume_4_API_Design.md`, `docs/Volume_1_Cahier_des_Charges.md` (Présences : appel par créneau ; Surveillance : circuit du billet), `docs/Volume_3_DDS.md` (colonnes `attendance_sheets.ScheduleSlotId`, `late_arrivals.*`, `student_attendances.EntryTicketId`), `docs/Volume_7_Security.md` (matrice : qui émet, qui accepte), `ACTIVE_CONTEXT.md` §2 (sous-section « Appel par créneau et billets d'entrée (Évolution N°5) », arbitrages B1-B13 **tels que validés**, invariant « le taux de présence n'a pas changé »).

- [ ] **Step 1 :** rédiger les mises à jour avec leur texte final. **Step 2 :** `python -c "import yaml; yaml.safe_load(open('openapi.yaml', encoding='utf-8'))"`, `dotnet build`, `node --test tests/js/*.test.mjs` → verts. **Step 3 : commit** — `docs(attendance): routes, modèle de données et contexte actif de l'évolution appel par créneau et billets`

---

## Vérification finale (à lancer par le propriétaire)

- `dotnet test` (suite complète) et `dotnet test --filter Category=MultiTenant` ; `node --test tests/js/*.test.mjs`.
- Recette manuelle (école en repos jeudi/vendredi, classe avec trois cours le samedi) : **Présences** → classe + samedi → trois pastilles de cours, choisir le premier → feuille chargée, aucun champ « Créneau » à saisir ; faire l'appel, marquer Awa « Absente » → 201 ; **Surveillance › Billets** → Awa, le cours visé est présélectionné (en cours ou prochain) → émettre : le registre de ce cours passe Awa à « Retard (billet) » et la famille reçoit « en retard de X minutes » ; **connecté en enseignant titulaire** → la feuille montre la pastille « Billet d'entrée — en attente d'acceptation » → « Accepter » → confirmé ; **autre enseignant** : pas de bouton ; **annuler** un billet non accepté → Awa retrouve « Absente » ; **Rapports › Assiduité** : Awa compte une journée d'**absence partielle** (et « 3 séances appelées »), pas complète ; un jeudi : aucun cours proposé, l'appel reste refusé, un billet sans cours visé reste possible ; le **taux de présence moyen** est celui d'avant pour les mêmes appels.

## Self-review (spec ↔ tâches)

| Exigence | Tâche |
|---|---|
| 1a. Appel par créneau horaire / cours d'emploi du temps (pas seulement par demi-journée) | 2 (lien), 3 (API : créneau, dérivation, titulaire), 8 (écran) — mode libre conservé (B1) |
| 1b. Distinguer absences complètes, retards, absences partielles par matière | 1 (classification pure), 4 (rapport : complètes/partielles/retard + vue par matière) — **aucun statut ajouté, taux inchangé** |
| 2a. La Vie Scolaire délivre un billet d'entrée en classe (justificatif de retard/absence) | 2 (circuit), 5 (émission visant un cours), 7 (billet imprimé), 8 (écran) |
| 2b. Statut mis à jour dans le registre d'appel du cours concerné | 5 (application au registre, présélection de la feuille, fusion à la soumission, SMS de rectification B8) |
| 2c. L'enseignant peut l'accepter en classe | 6 (acceptation par le titulaire, idempotente ; annulation avant acceptation), 8 (bouton dans la feuille) |
| Jours de repos (Évolution N°3) | 3 (ordre des gardes), 5 (billet sans cours visé possible), tests de non-régression `WorkingDayLockTests` |
| Isolation multi-tenant | 2, 3, 5, 6 (tests `Category=MultiTenant`) |

Points de vigilance connus, non traités par ce plan : (1) **billet de sortie** inchangé (hors périmètre, B10) ; (2) la classification « complète » se fonde sur les séances **appelées** — une école qui ne fait l'appel que sur un cours par jour verra surtout des absences « complètes » ; la couverture affichée est là pour le rappeler, et rendre l'appel obligatoire sur tous les cours serait une autre évolution ; (3) **concurrence** : un billet émis exactement pendant la soumission de la fiche peut ne pas être rattaché à sa ligne ; l'acceptation par l'enseignant est le point de réconciliation (elle applique le retard à la ligne existante), et la feuille rechargée le montre — pas de verrou `xmin` sur `StudentAttendance` dans ce plan ; (4) un billet **accepté** ne s'annule plus (B9) : une erreur se corrige par un nouvel appel sur la ligne ; (5) la Tâche 9 (justification des séances manquées) est la seule qui crée une table — d'où son statut optionnel.

---

## Écarts constatés à l'exécution

Ce que le code livré fait autrement que le plan ci-dessus, et pourquoi :

- **Pas de `TargetDate`.** `LateArrivals.Date` est déjà une colonne `date` : le billet vise (cours, `Date`), aucune colonne ajoutée.
- **Index de `StudentId` conservé explicitement.** EF supprimait `IX_LateArrivals_StudentId` comme redondant avec l'index unique partiel ; `HasIndex(e => e.StudentId)` est déclaré, la migration régénérée.
- **Refus.** Un enseignant non titulaire reçoit **403** (`ForbiddenException`), pas `UnauthorizedAccess` ; les refus métier (billet annulé, déjà accepté, sans cours visé) sont des `ValidationException` sur la clé « Ticket » → **422**.
- **Contrôleur d'acceptation séparé** (`EntryTicketActionsController`, même préfixe `api/v1/billets`) : un attribut de rôle sur une action s'ajoute à celui de la classe au lieu de l'élargir, et `BilletsController` exclut l'enseignant.
- **`EntryTicketRegister`.** `ApplyAsync` est idempotent (`PreviousStatus ??=`) et `RestoreAsync` ne restaure que si un statut d'avant existe ; le numéro de billet vient d'`EntryTicketNumber.For(id)`, seule source (le PDF et la feuille ne recalculent plus).
- **Formes de DTO.** `EntryTicketDto` gagne `TargetSubjectName`, `TargetTimeRange`, `TargetTeacherName`, `Status` en fin de record avec défauts ; `StudentAttendanceReportRow` gagne `DaysRecorded`, `FullAbsenceDays`, `PartialAbsenceDays` de la même façon — toute construction existante reste valide.
- **Rapport.** `AttendanceReportAggregator` calcule par jour en SQL (comptes par groupe) puis classe via `DayAttendanceClassifier` ; le CSV ajoute deux colonnes en fin de ligne, le PDF deux colonnes.
- **Tests d'infrastructure.** `AuthApiFactory` nettoie aussi `LateArrivals`, `ScheduleSlots` et `subject_coefficient_overrides` (FK) ; le garde de régression JS impose un `toast.error` dans les `catch` des chargeurs, y compris `loadSlots` et `loadTodaySlots`.

---

# Complément N°5 bis — « Retard » retiré de l'appel, billet par heure d'arrivée (Tâches 11 à 15)

> **STATUT : PLANIFIÉ le 25/09/2026, NON EXÉCUTÉ.** À exécuter **après** le plan ci-dessus (Tâches 1 à 8 et 10, livrées sur `feature/attendance-slots`), sur une branche dédiée `feature/attendance-arrival-time` **empilée sur `feature/attendance-slots`**, dans un worktree (`WORK_IN_PROGRESS.md`, règle 5) — jamais dans l'arbre partagé pendant qu'une autre session y travaille.

**Demande (25/09/2026) :**
1. Appel en classe : supprimer la notion de « Retard » et la saisie « RETARD (MIN) » ; l'enseignant ne choisit que **Présent**, **Absent (justifié)**, **Absent (non justifié)**.
2. Billet d'entrée : gérer (a) un **retard en minutes** sur un cours en cours (arrivé à 08h15 pour le cours de 08h00) et (b) une **absence sur une plage** (absent de 08h00 à 10h00, présent à partir de 10h00), avec calcul automatique de la durée.

## Arbitrages

| # | Question | Décision |
|---|---|---|
| **C1** | Deux types de billet, ou un seul champ ? | **VALIDÉ le 25/09/2026 : « heure d'arrivée » unique.** Le surveillant saisit l'heure réelle d'arrivée ; le serveur en déduit les cours **manqués** (terminés avant l'arrivée) et le cours **en cours** (retard en minutes). Les deux cas de la demande sont deux résultats d'un même calcul, sans double saisie. |
| **C2** | Coordination | **VALIDÉ : ajouter au plan, exécuter après.** Aucun fichier de l'appel ou des billets n'est modifié tant que ce complément n'est pas lancé. |
| C3 | Calcul (pur, testable) | `ArrivalCoverage.Compute(coursDeLaClasseCeJour, arrivée)` : **manqué** = cours dont `End <= arrivée` ; **en cours** = cours avec `Start < arrivée < End`, retard = `arrivée − Start` ; cours avec `Start >= arrivée` **ignorés**. Durée totale = Σ durées des cours manqués + minutes de retard. Arrivée **avant le début du premier cours** → 422 (« aucun cours n'a commencé »). Arrivée pile à l'heure de début → pas de retard sur ce cours (les précédents restent manqués). |
| C4 | Cours visé du billet (celui que l'enseignant accepte) | Le cours **en cours** ; à défaut (arrivée pendant une pause) le **prochain** ; à défaut (plus aucun cours) le **dernier cours manqué**, accepté alors par son titulaire ou le Directeur. Un jour de repos ou une classe sans emploi du temps : billet **sans cours visé**, minutes saisies à la main comme aujourd'hui (B10 inchangé). |
| C5 | Effet sur le registre des cours manqués | **À confirmer par le propriétaire (recommandé : oui).** Les lignes des cours manqués passent à `JustifiedAbsence` **seulement** depuis `UnjustifiedAbsence` ou en l'absence de ligne (présélection de la feuille) ; **jamais** depuis `Present`/`Late`, jamais l'inverse. Le cours en cours passe à `Late` (B6 inchangé). Cela **lève B7** : le billet justifie désormais les séances manquées. Si la réponse est « non », les cours manqués restent seulement **affichés** sur le billet (durée) sans toucher au registre. |
| C6 | Restauration à l'annulation, sans table de journal | Un billet peut maintenant toucher **plusieurs** lignes, ce qui était la raison d'être de la table `attendance_status_changes` (Tâche 9). Plus simple : `student_attendances` gagne `PreviousStatus`/`PreviousLateMinutes` (nullables), **une ligne ↔ un billet** (`EntryTicketId` existe déjà). **Aucune nouvelle table** : pas de RLS/Global Query Filter/reset/purge supplémentaires (règle #2). Les colonnes `late_arrivals.Previous*` restent lues en repli pour les billets déjà émis. |
| C7 | Notifications famille | `AttendanceRecordedEvent` **uniquement** pour le cours en cours (absence → retard, B8 inchangé). Le passage `UnjustifiedAbsence → JustifiedAbsence` d'un cours manqué **n'envoie rien** en V1 (pas de nouveau modèle de message). |
| C8 | Retard retiré de la grille : que devient un retard dans le registre ? | Un retard n'existe plus **que** par un billet. Les lignes `Late` **historiques** restent lues, comptées et rapportées telles quelles ; le **taux de présence est inchangé** (« Présents + Retards » ; invariant N°3). Le mode « Libre » (sans créneau) perd la saisie manuelle du retard : c'est **volontaire** — un retard sans billet n'a plus de source. |
| C9 | Contrat d'API | **Changement cassant assumé** pour `POST /attendance` : `status = "Late"` sans billet actif → **422** (« Le retard s'enregistre par un billet d'entrée »). `lateMinutes` reste dans le contrat (billets et historique). À reporter dans `openapi.yaml` et Volume 4. Un client d'appel externe qui envoyait `Late` doit passer par le billet. |

## File Structure (complément)

| Fichier | Rôle |
|---|---|
| `Application/Attendance/ArrivalCoverage.cs` (créer) | Pur : cours manqués / cours en cours / durée totale. |
| `Domain/Entities/LateArrival.cs` (modifier) | `TimeOnly? ArrivalTime`, `int? TotalMinutes` (instantané calculé à l'émission : un emploi du temps modifié plus tard ne réécrit pas un billet imprimé). |
| `Domain/Entities/StudentAttendance.cs` (modifier) | `AttendanceStatus? PreviousStatus`, `int? PreviousLateMinutes`. |
| `Persistence/Configurations/*`, `Migrations/*` | Colonnes nullables uniquement, migration **nouvelle**. |
| `Absences/Commands/CreateLateArrival/*` (modifier) | `TimeOnly? ArrivalTime` ; `Minutes` dérivé quand elle est fournie. |
| `Attendance/EntryTickets/EntryTicketRegister.cs` (modifier) | `ApplyAsync`/`RestoreAsync` sur **plusieurs** lignes. |
| `Absences/Queries/GetArrivalPreview/*` (créer) | Aperçu calculé **côté serveur** (aucune règle métier dans le client). |
| `Attendance/Commands/SubmitAttendanceSheet/*Validator.cs`, `*Handler.cs` (modifier) | Refus de `Late` sans billet actif (C9). |
| `wwwroot/js/attendance.js`, `Views/Attendance/Index.cshtml` | Grille à trois statuts. |
| `wwwroot/js/billets.js`, `Views/Absences/Billets.cshtml` | Champ « Heure d'arrivée » + aperçu. |
| `Infrastructure/Documents/EntryTicket*.cs`, `GetEntryTicket*` | Arrivée, cours manqués, durée. |
| `tests/SamaEcole.UnitTests/Attendance/ArrivalCoverageTests.cs`, `tests/SamaEcole.IntegrationTests/VieScolaire/ArrivalTimeTicketTests.cs`, `src/SamaEcole.Web/tests/js/attendance-no-late.test.mjs`, `billets-arrival.test.mjs` | Tests. |

---

### Task 11 : Retirer « Retard » de la grille d'appel (écran + garde serveur)

**Files:** Modify `wwwroot/js/attendance.js` (`STATUSES`, `lateMinutes`, compteur d'en-tête, `statusBadge`), `Views/Attendance/Index.cshtml` (colonne « Retard (min) », pastille « Retards »), `SubmitAttendanceSheetCommandValidator.cs` + `SubmitAttendanceSheetCommandHandler.cs`, `help.js` (fiche « Appel en classe »). Test : `attendance-no-late.test.mjs`, `AttendanceBySlotTests.cs` (étendu).

**Interfaces:** aucune nouvelle route. Écran : la liste ne propose que `Present`, `JustifiedAbsence`, `UnjustifiedAbsence` ; plus de colonne « RETARD (MIN) » ; le compteur d'en-tête ne montre plus « Retards ». Une ligne portant un `entryTicketId` (billet) n'a **pas** de liste : pastille en lecture seule « Retard — billet » (ou « Absence justifiée — billet »), minutes affichées, bouton « Accepter » inchangé pour l'enseignant titulaire ; elle est renvoyée telle quelle à la soumission. Serveur (C9) : ligne `Late` sans billet actif sur (élève, créneau, date) → 422.

- [ ] **Step 1 : tests qui échouent.** JS : les options sont exactement Présent / Absent (justifié) / Absent (non justifié) ; aucune saisie de minutes ; une ligne avec billet est verrouillée et renvoyée avec son statut ; le compteur n'affiche plus de retards. C# : (1) `Late` sans billet → 422, rien d'écrit ; (2) `Late` **avec** billet actif → accepté (feuille pré-marquée renvoyée par le client) ; (3) fiche sans aucune ligne `Late` → comportement d'avant ; (4) les lignes `Late` historiques sont toujours lues et **le taux de présence est identique** (`AttendanceRateWorkingDaysTests` vert, non modifié) ; (5) `[Trait("Category","MultiTenant")]` : un billet d'une autre école ne « couvre » jamais une ligne.
- [ ] **Step 2 : constater l'échec.** **Step 3 : implémenter.** **Step 4 : constater le succès** — `node --test tests/js/*.test.mjs` ; `--filter "FullyQualifiedName~AttendanceBySlotTests|FullyQualifiedName~AttendanceRateWorkingDaysTests|FullyQualifiedName~EntryTicketRegisterTests"` ; mettre à jour les tests d'aide si le texte est le contrat.
- [ ] **Step 5 : commit** — `feat(attendance): appel à trois statuts — le retard ne se saisit plus qu'avec un billet d'entrée`

---

### Task 12 : `ArrivalCoverage` (pur, sans base)

**Files:** Create `Application/Attendance/ArrivalCoverage.cs` ; Test `tests/SamaEcole.UnitTests/Attendance/ArrivalCoverageTests.cs`.

**Interfaces:** `ArrivalCoverage.Compute(IReadOnlyList<(Guid SlotId, TimeOnly Start, TimeOnly End)> slots, TimeOnly arrival)` → `record ArrivalCoverageResult(IReadOnlyList<Guid> MissedSlotIds, Guid? InProgressSlotId, int LateMinutes, int MissedMinutes, int TotalMinutes, Guid? TargetSlotId)` ; `TargetSlotId` applique C4. Lève une erreur de domaine lisible (arrivée avant le premier cours, rien à régulariser) — le Handler la traduit en 422.

- [ ] **Step 1 : tests qui échouent** (cas de la demande) : trois cours 08:00-10:00, 10:00-12:00, 14:00-16:00 — (1) arrivée 08:15 → aucun manqué, en cours = cours 1, retard 15, total 15 ; (2) arrivée 10:00 → manqué = cours 1, retard 0, total 120, cible = cours 2 ; (3) arrivée 10:20 → manqué = cours 1, retard 20, total 140 ; (4) arrivée 12:30 (pause) → manqués 1 et 2, cible = cours 3, retard 0, total 240 ; (5) arrivée 17:00 → tout manqué, cible = **dernier** cours (C4) ; (6) arrivée 07:30 → refus ; (7) arrivée = 08:00 pile → aucun manqué, aucun retard → refus (« rien à régulariser ») ; (8) liste vide → refus ; (9) créneaux non triés en entrée → même résultat.
- [ ] **Step 2 : constater l'échec.** **Step 3 : implémenter.** **Step 4 : constater le succès** — `--filter "FullyQualifiedName~ArrivalCoverageTests"`.
- [ ] **Step 5 : commit** — `feat(attendance): calcul pur des cours manqués et du retard à partir de l'heure d'arrivée`

---

### Task 13 : Émission par heure d'arrivée, registre multi-lignes, annulation

**Files:** Modify `LateArrival.cs`, `StudentAttendance.cs`, leurs `*Configuration.cs`, `Migrations/*` (nouvelle), `CreateLateArrival{Command,Validator,Handler}.cs`, `EntryTicketRegister.cs`, `AbsenceController.cs`, `InitializeAttendanceSheetQueryHandler.cs` (présélection des cours manqués). Create `Absences/Queries/GetArrivalPreview/*`. Test `ArrivalTimeTicketTests.cs`.

**Interfaces:**
- `POST /api/v1/absences/late-arrivals` accepte `arrivalTime` (`HH:mm`, optionnel). Avec `arrivalTime` : le serveur charge les cours de la classe de l'élève pour `date`, calcule `ArrivalCoverage`, **dérive** `Minutes`, `TargetScheduleSlotId`, `TotalMinutes` (les `minutes`/`targetScheduleSlotId` du client sont **ignorés** dans ce mode), refuse un jour de repos / classe sans cours (le billet reste alors possible **sans** `arrivalTime`, comportement d'avant). Sans `arrivalTime` : **tout comme aujourd'hui** — un client qui n'envoie pas le champ ne voit aucun changement. Le validateur actuel (`Minutes > 0`) est relâché **seulement** quand `arrivalTime` est fourni (un billet d'arrivée pile à la fin d'un cours a 0 minute de retard et une durée manquée > 0).
- `GET /api/v1/absences/arrival-preview?studentId=&date=&arrivalTime=` → `{missedSlots:[{slotId,label,subjectName,minutes}], inProgress?:{slotId,label,subjectName,lateMinutes}, targetSlotId?, totalMinutes}` — l'écran n'affiche que ce que le serveur calcule.
- `EntryTicketRegister.ApplyAsync` : pour chaque cours **manqué** ayant une fiche : la ligne de l'élève en `UnjustifiedAbsence` → `JustifiedAbsence` (C5), `PreviousStatus`/`PreviousLateMinutes` mémorisés **sur la ligne**, `EntryTicketId` posé ; le cours en cours → `Late` comme en B6 ; sans fiche → présélection à l'ouverture (`InitializeAttendanceSheet` : statut du billet + `entryTicketId`). Événement famille : C7. `RestoreAsync` restaure **chaque** ligne liée depuis **son** `PreviousStatus` (repli sur `late_arrivals.Previous*` pour l'historique). Au plus un billet actif par (élève, cours, jour) (index existant) : un cours **déjà couvert** par un billet actif est ignoré au calcul.

- [ ] **Step 1 : tests d'intégration qui échouent** (montage de la Tâche 5 + trois cours) : (1) arrivée 10:20, fiche du cours 1 saisie en `UnjustifiedAbsence` → cours 1 `JustifiedAbsence`, cours 2 `Late` 20 min, `TotalMinutes = 140`, billet `Issued` visant le cours 2 ; (2) même arrivée, fiche du cours 1 en `Present` → **inchangée** ; (3) fiche du cours 1 absente → aucune ligne créée, la feuille l'affiche pré-marquée `JustifiedAbsence` avec le billet ; (4) **annulation** avant acceptation → cours 1 retrouve `UnjustifiedAbsence`, cours 2 retrouve son statut d'avant ; (5) annuler un billet accepté → 422 (B9) ; (6) `arrivalTime` fourni + `minutes` incohérentes → `minutes` du client ignorées ; (7) arrivée avant le premier cours → 422 ; (8) jour de repos avec `arrivalTime` → 422 lisible, sans `arrivalTime` → billet libre accepté ; (9) un second billet pour le même élève le même jour ne re-justifie pas un cours déjà couvert ; (10) SMS/WhatsApp : **un seul** événement (cours en cours), aucun pour le cours manqué (C7) ; (11) **taux de présence** : le cours manqué justifié reste une absence pour le taux, le cours en retard compte comme présent — assertion sur `AttendanceReportAggregator` (aucune formule modifiée) ; (12) `[Trait("Category","MultiTenant")]` : l'aperçu et l'émission ne voient ni les cours ni l'élève d'une autre école ; (13) entrée d'audit.
- [ ] **Step 2 : constater l'échec.** **Step 3 : implémenter** — colonnes nullables, migration **nouvelle** (générer avec `--configuration Release`, **puis** `dotnet ef database update` avant de relancer l'app, constat du 24/09/2026) ; émission et application au registre dans **une seule transaction**. **Step 4 : constater le succès** — `--filter "FullyQualifiedName~ArrivalTimeTicketTests|FullyQualifiedName~EntryTicket|FullyQualifiedName~AttendanceBySlotTests"`.
- [ ] **Step 5 : commit** — `feat(attendance): billet d'entrée par heure d'arrivée — cours manqués justifiés, retard en minutes, restauration par ligne`

---

### Task 14 : Écran Billets d'entrée et billet imprimé

**Files:** Modify `wwwroot/js/billets.js`, `Views/Absences/Billets.cshtml`, `GetEntryTicketQuery.cs` (+ Pdf), `Infrastructure/Documents/EntryTicket*.cs`, `help.js`. Test `billets-arrival.test.mjs`, `EntryTicketPdfGeneratorTests.cs` (étendu).

**Interfaces:** le formulaire propose **« Heure d'arrivée »** (champ heure) à la place de « Minutes de retard » quand l'élève a des cours ce jour-là ; l'**aperçu** (appel de `GET /absences/arrival-preview` à chaque changement, avec `debounce`) affiche « Cours manqué : Mathématiques 08:00-10:00 (120 min) · En retard de 20 min à Anglais 10:00-12:00 · Durée totale : 2 h 20 ». Sans cours ce jour-là (repos, pas d'emploi du temps) : l'ancien champ « Minutes » est conservé. `EntryTicketDto` gagne **en fin de liste, optionnels** : `ArrivalTime`, `MissedSlots`, `TotalMinutes` ; un billet ancien s'imprime **exactement comme avant**.

- [ ] **Step 1 : tests JS et PDF qui échouent** : le champ heure apparaît quand `today-slots` renvoie des cours, sinon le champ minutes ; aucune durée n'est calculée côté client (l'aperçu vient du serveur) ; le bouton « Émettre » est désactivé tant que l'aperçu est en erreur ; le PDF d'un billet avec arrivée mentionne l'heure, les cours manqués et la durée, celui d'un ancien billet est inchangé. **Step 2 : échec. Step 3 : implémenter. Step 4 : succès** — `node --test tests/js/*.test.mjs`, `--filter "FullyQualifiedName~EntryTicket"`, `npm run build:css --prefix src/SamaEcole.Web`.
- [ ] **Step 5 : commit** — `feat(attendance): écran des billets par heure d'arrivée, aperçu serveur et billet imprimé`

---

### Task 15 : Documentation et contexte actif

**Files:** `openapi.yaml` (`arrivalTime` sur `POST /absences/late-arrivals`, `GET /absences/arrival-preview`, refus 422 de `Late` sur `POST /attendance`), `docs/Volume_4_API_Design.md`, `Volume_1_Cahier_des_Charges.md` (Présences : trois statuts ; Surveillance : billet par heure d'arrivée), `Volume_3_DDS.md` (`late_arrivals.ArrivalTime/TotalMinutes`, `student_attendances.PreviousStatus/PreviousLateMinutes`), `Volume_7_Security.md` (inchangé : mêmes rôles), `ACTIVE_CONTEXT.md` §2 (arbitrages C1-C9 **tels que validés**, dont **B7 levé** et le changement cassant C9), `docs/GUIDE_FONCTIONNEL_MODULES.md`.

- [ ] **Step 1 :** rédiger. **Step 2 :** `python -c "import yaml; yaml.safe_load(open('openapi.yaml', encoding='utf-8'))"`, `dotnet build`, `node --test tests/js/*.test.mjs`. **Step 3 : commit** — `docs(attendance): appel à trois statuts et billet par heure d'arrivée`

---

## Vérification finale du complément (à lancer par le propriétaire)

- `dotnet test` (suite complète, dont `--filter Category=MultiTenant`) ; `node --test tests/js/*.test.mjs`.
- Recette manuelle (classe avec cours 08:00-10:00, 10:00-12:00, 14:00-16:00 le samedi) : **Présences** → la liste ne propose que Présent / Absent (justifié) / Absent (non justifié), aucune colonne de minutes ; marquer Awa « Absente (non justifiée) » au cours 1 ; **Surveillance › Billets** → Awa, arrivée **10:20** → l'aperçu annonce « Cours manqué : 08:00-10:00 (120 min) · retard de 20 min au cours 2 · total 2 h 20 » → émettre ; **Présences** : cours 1 devient « Absent (justifié) — billet », cours 2 « Retard — billet » (verrouillé), l'enseignant du cours 2 voit « Accepter » ; **annuler** le billet avant acceptation → Awa retrouve « Absent (non justifié) » au cours 1 ; arrivée **08:15** → simple retard de 15 min ; un jeudi de repos : champ « Minutes » ancien, billet libre possible ; **Rapports › Assiduité** : taux moyen identique à celui d'avant pour les mêmes appels.

## Self-review du complément (demande ↔ tâches)

| Exigence | Tâche |
|---|---|
| Supprimer « Retard » et « RETARD (MIN) » de la grille ; ne garder que Présent / Absent justifié / Absent non justifié | 11 |
| Billet : retard en minutes sur un cours en cours | 12 (calcul), 13 (registre, cours en retard), 14 (écran) |
| Billet : absence sur une plage / un créneau entier, présent ensuite | 12 (cours manqués), 13 (justification des lignes), 14 (aperçu) |
| Calcul automatique de la durée | 12 (`TotalMinutes`), 13 (instantané sur le billet), 14 (aperçu et PDF) |
| Aucune règle métier côté client | 14 (aperçu serveur) |
| Taux de présence, jours de repos, isolation multi-tenant, historique inchangés | 11, 13 (tests de non-régression et `MultiTenant`) |

Points de vigilance : (1) le passage `UnjustifiedAbsence → JustifiedAbsence` n'avertit pas la famille (C7) ; (2) **C5 attend la confirmation du propriétaire** — c'est la seule décision qui change ce que le registre affirme ; (3) C9 est un changement d'API assumé ; (4) une arrivée pendant une pause vise le cours suivant : si l'école veut plutôt viser le dernier cours manqué, seul `ArrivalCoverage` change ; (5) le mode « Libre » n'a plus de retard manuel (C8).
