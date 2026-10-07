# Internat — lot E : fiche pensionnaire et registre du pavillon en PDF — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Produire en PDF (QuestPDF) la **fiche individuelle d'un pensionnaire** et le **registre imprimable d'un pavillon**, avec un masquage de la fiche médicale **prouvé sur le contenu réel des PDF**.

**Architecture :** Même découpage que le bulletin coranique (`HizbReport*`) : requêtes MediatR qui construisent un DTO déjà masqué (Application), interfaces `I…PdfGenerator` (Application/Common/Interfaces), documents QuestPDF + générateurs protégés par `PdfRenderGuard` (Infrastructure/Documents), requêtes `…PdfQuery` auditables, deux routes minces dans `BoardingController`. **Aucune migration.**

**Tech Stack :** .NET 9, QuestPDF 2024.10.3 (licence Community, police arabe embarquée), MediatR, xUnit + FluentAssertions, **UglyToad.PdfPig (test uniquement — voir décision E1)**.

**Spec :** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` §5.2 (confidentialité), §7 (documents PDF), §9 (tests).

## Global Constraints

- Branche `feat/internat-pdf-reports`, **empilée sur `feat/internat-leaves-attendance` (lot D)**, elle-même sur le lot C (#63) puis le lot B (#62). Pas de push par moi : la PR sera préparée en brouillon.
- Routes (spec §7) : `GET /api/v1/boarding/boarders/{id}/pdf` et `GET /api/v1/boarding/dormitories/{id}/register-pdf?date=`. Rôles : `Directeur`, `Secretariat`, `Surveillant` (`ReadRoles`) ; `Enseignant`/`Finance` → 403 ; module Internat désactivé → 403 `MODULE_DISABLED`.
- **Fiche médicale** : visible dans la fiche individuelle pour `Directeur` et `Surveillant` seulement, via `BoarderDetailReader.CanReadMedical(role)` — **source unique** de la règle, déjà employée par `GET /boarders/{id}`. Le registre ne contient **jamais** de donnée médicale ni de numéro de téléphone (celui du surveillant du pavillon compris).
- Une impression est une action tracée : les deux requêtes PDF implémentent `IAuditableRequest`, et l'audit ne journalise jamais le contenu médical (l'exception du message ne reprend pas de valeur).
- Noms arabes : composant `Bilingual.ArabicBlock`, jamais de texte arabe brut dans un `Text()`.
- Aucun `DateTime.Now` : `TimeProvider` (même convention que les lots précédents).
- Génération déterministe et synchrone, fichier `inline` (`Content-Disposition: inline; filename=…`) avec un nom assaini, comme `GetHizbReportPdf`.
- `dotnet build` avant tout `dotnet test --no-build` ; suites jamais en parallèle ; journaux redirigés vers un fichier.

## Décisions à valider

| # | Décision | Pourquoi |
|---|---|---|
| **E1** | **Ajouter `UglyToad.PdfPig` (Apache-2.0) aux seuls projets de tests** pour extraire le texte des PDF générés. | Les tests PDF existants ne vérifient que l'en-tête `%PDF-` : le flux QuestPDF est compressé et les glyphes sont indexés, donc une recherche d'octets ne trouve jamais le texte. **Une assertion négative (« la fiche médicale est absente ») sur les octets bruts passerait toujours : elle ne prouverait rien.** C'est la seule dépendance nouvelle. Alternative sans dépendance : tester seulement le DTO masqué (la mise en page ne peut alors plus être contrôlée). |
| E2 | Le registre porte sur **une nuit** (`date`, aujourd'hui par défaut) et **réutilise `GetAttendanceSheetQuery`** : mêmes règles de date (≤ aujourd'hui, année active), mêmes pré-remplissages (absents en permission). | Une seule définition de « qui est pointable et qui est en sortie ». |
| E3 | La fiche imprime **toutes** les sorties de l'année scolaire active (pas seulement les 10 dernières de la fiche JSON), de la plus récente à la plus ancienne. | Spec §7 : « historique des sorties de l'année ». |
| E4 | Un séjour **clos** reste imprimable (fiche d'archive) ; le registre ne liste que les séjours actifs. | Cohérent avec `GET /boarders/{id}` (qui lit aussi les séjours clos). |
| E5 | Coordonnées de la fiche (contact d'urgence, personnes habilitées) **imprimées** ; réservées aux trois rôles de lecture, donc pas de masquage. | Spec §7 les liste dans la fiche ; seul le registre, affiché au portail du pavillon, les exclut. |

## Structure des fichiers

| Fichier | Rôle |
|---|---|
| `Application/Boarding/Documents/BoarderSheet/BoarderSheetDto.cs` (créer) | DTO d'impression de la fiche (déjà masqué) |
| `Application/Boarding/Documents/BoarderSheet/GetBoarderSheetQuery.cs` (créer) | Construit le DTO depuis `GetBoarderQuery` + élève + école + sorties de l'année |
| `Application/Boarding/Documents/BoarderSheet/GetBoarderSheetPdfQuery.cs` (créer) | Auditable ; rend le PDF |
| `Application/Boarding/Documents/DormitoryRegister/DormitoryRegisterDto.cs`, `GetDormitoryRegisterQuery.cs`, `GetDormitoryRegisterPdfQuery.cs` (créer) | Idem pour le registre |
| `Application/Common/Interfaces/IBoarderSheetPdfGenerator.cs`, `IDormitoryRegisterPdfGenerator.cs` (créer) | Contrats `byte[] Generate(dto, byte[]? logo)` |
| `Infrastructure/Documents/BoarderSheetDocument.cs`, `BoarderSheetPdfGenerator.cs`, `DormitoryRegisterDocument.cs`, `DormitoryRegisterPdfGenerator.cs` (créer) | Mise en page QuestPDF |
| `Infrastructure/DependencyInjection.cs` (modif) | Deux `AddSingleton<I…PdfGenerator, …>` |
| `Web/Controllers/BoardingController.cs` (modif) | Deux routes |
| `tests/SamaEcole.UnitTests/Boarding/PdfText.cs` (créer) | Aide `PdfText.Extract(byte[])` (PdfPig) |
| `tests/…/BoarderSheetDocumentTests.cs`, `DormitoryRegisterDocumentTests.cs`, `…/Boarding/BoardingDocumentsDtoTests.cs`, `FunctionalTests/Boarding/BoardingPdfEndpointsTests.cs` (créer) | Tests |
| `docs/Volume_4_API_Design.md`, `ACTIVE_CONTEXT.md` (modif) | Contrat et état |

---

### Task 1 : outil d'extraction de texte des PDF (si E1 est validée)

**Files:**
- Modify: `tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj`, `tests/SamaEcole.FunctionalTests/SamaEcole.FunctionalTests.csproj` (`<PackageReference Include="UglyToad.PdfPig" Version="…" />`, dernière version stable compatible net9.0 ; vérifier qu'elle se restaure)
- Create: `tests/SamaEcole.UnitTests/Boarding/PdfText.cs` (copie liée ou fichier identique dans les fonctionnels)
- Test: `tests/SamaEcole.UnitTests/Boarding/PdfTextTests.cs`

**Interfaces:**
- Produces : `internal static class PdfText { public static string Extract(byte[] pdf); }` — texte de toutes les pages, espaces normalisés.

- [ ] **Step 1 : test (échoue)** : générer avec QuestPDF un PDF d'une page contenant `"Allergie aux arachides"` et un nombre `"770000000"`, `PdfText.Extract(bytes)` doit contenir les deux ; et prouver l'inutilité de la recherche brute : `Encoding.Latin1.GetString(bytes)` ne contient **pas** `"Allergie"` (documente pourquoi PdfPig est nécessaire).
- [ ] **Step 2 : lancer** `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~PdfTextTests"` — échec de compilation.
- [ ] **Step 3 : implémenter**
```csharp
using UglyToad.PdfPig;

namespace SamaEcole.UnitTests.Boarding;

internal static class PdfText
{
    public static string Extract(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
```
- [ ] **Step 4 : relancer — PASS.** Si l'extraction de texte latin échoue avec la police embarquée, **s'arrêter et en rendre compte** (ne pas contourner par des assertions sur les octets).
- [ ] **Step 5 : commit** `test(internat): extraction de texte des PDF pour contrôler la confidentialité`

---

### Task 2 : DTO de la fiche et du registre, déjà masqués

**Files:**
- Create: `Application/Boarding/Documents/BoarderSheet/{BoarderSheetDto,GetBoarderSheetQuery}.cs`, `Application/Boarding/Documents/DormitoryRegister/{DormitoryRegisterDto,GetDormitoryRegisterQuery}.cs`
- Test: `tests/SamaEcole.IntegrationTests/Boarding/BoardingDocumentsDtoTests.cs` (étend `LeaveTestBase`, « aujourd'hui » = 12/10), `tests/SamaEcole.UnitTests/Boarding/DormitoryRegisterDtoShapeTests.cs`

**Interfaces:**
- Consumes : `GetBoarderQuery` (masquage par rôle), `GetAttendanceSheetQuery` (lot D), `ICurrentUserService`, `BoardingLeaves`.
- Produces :
```csharp
public record BoarderSheetDto(
    string SchoolName, string? SchoolAddress, string? SchoolLogoUrl, string? SchoolYearLabel,
    BoarderListItemDto Boarder, string? FullNameAr, DateOnly BirthDate, string BirthPlace,
    string? MedicalNotes,                       // null sauf Directeur / Surveillant
    string? EmergencyContactName, string? EmergencyContactPhone,
    IReadOnlyList<AllowedExitPersonDto> AllowedExitPersons,
    IReadOnlyList<BoardingLeaveDto> Leaves,     // toutes les sorties de l'année active (E3)
    DateTimeOffset GeneratedAt);
public record GetBoarderSheetQuery(Guid BoarderId) : IRequest<BoarderSheetDto>;

public record DormitoryRegisterRowDto(
    string RoomName, int BedNumber, string StudentName, string? StudentNameAr, string Matricule, string ClassroomName,
    bool OnLeave, bool Recorded, bool? IsPresent, string? Note);
public record DormitoryRegisterDto(
    string SchoolName, string? SchoolAddress, string? SchoolLogoUrl, string? SchoolYearLabel,
    string DormitoryName, string? SupervisorName,     // jamais SupervisorPhone
    DateOnly Date, IReadOnlyList<DormitoryRegisterRowDto> Rows, DateTimeOffset GeneratedAt);
public record GetDormitoryRegisterQuery(Guid DormitoryId, DateOnly? Date) : IRequest<DormitoryRegisterDto>;
```

- [ ] **Step 1 : tests (échouent)** :
  1. **Masquage** : `GetBoarderSheetQuery` pour un séjour dont la fiche médicale est renseignée → `MedicalNotes` renseigné avec `Directeur` et `Surveillant`, **`null` avec `Secretariat`** (handler construit avec `TestCurrentUser(role)`), et le reste du DTO identique entre rôles.
  2. **Sorties de l'année** : 3 sorties dont une d'une année précédente → seules les 2 de l'année active sont dans `Leaves`, la plus récente d'abord.
  3. **Séjour clos** imprimable (E4) ; séjour d'une autre école / inconnu → `KeyNotFoundException`.
  4. **Registre** : lignes des internes du pavillon triées chambre puis lit ; absent en permission pré-rempli ; ligne déjà pointée reprise ; date par défaut = aujourd'hui ; date future → `ValidationException` ; pavillon d'une autre école → `KeyNotFoundException` ; demi-pensionnaire et interne sans lit absents.
  5. **Forme du registre (unitaire, par réflexion)** : aucun membre de `DormitoryRegisterDto` ni de `DormitoryRegisterRowDto` dont le nom contient `Medical`, `Phone`, `Contact` ou `AllowedExit` — verrou contre l'ajout futur d'un champ sensible.
- [ ] **Step 2 : lancer — échec de compilation.**
- [ ] **Step 3 : implémenter** `GetBoarderSheetQueryHandler(ISender mediator, IApplicationDbContext db, TimeProvider clock)` : `var detail = await mediator.Send(new GetBoarderQuery(id))` (le masquage vient d'ici, **ne jamais relire `MedicalNotes` en base**) ; élève (`FullNameAr`, naissance) et école par jointure ; `Leaves` = `LeaveReader.Project` filtré sur l'année active (`LeaveDate` ou `ExpectedReturnDate` dans [début, fin] de l'année), sans `Take`. `GetDormitoryRegisterQueryHandler` : `date = request.Date ?? today`, `sheet = await mediator.Send(new GetAttendanceSheetQuery(id, date))`, puis ajoute classe, nom arabe, école, surveillant (nom seulement).
- [ ] **Step 4 : relancer — PASS.**
- [ ] **Step 5 : commit** `feat(internat): DTO d'impression de la fiche pensionnaire et du registre du pavillon (masquage par rôle)`

---

### Task 3 : document PDF de la fiche pensionnaire

**Files:**
- Create: `Application/Common/Interfaces/IBoarderSheetPdfGenerator.cs`, `Infrastructure/Documents/BoarderSheetDocument.cs`, `BoarderSheetPdfGenerator.cs`
- Modify: `Infrastructure/DependencyInjection.cs`
- Test: `tests/SamaEcole.UnitTests/Boarding/BoarderSheetDocumentTests.cs`

**Interfaces:**
- Consumes : `BoarderSheetDto`, `Bilingual.ArabicBlock`, `OfficialHeaderComponent.ComposeEstablishmentBlock`, `PdfRenderGuard.Render`.
- Produces : `interface IBoarderSheetPdfGenerator { byte[] Generate(BoarderSheetDto sheet, byte[]? logo); }`.

Mise en page A4 portrait : en-tête d'établissement (logo si présent) ; titre « Fiche de pensionnaire » ; bloc identité (nom, **nom arabe via `Bilingual.ArabicBlock` quand renseigné**, matricule, naissance, classe, année) ; bloc hébergement (pavillon / chambre / lit, régime, dates du séjour, « Séjour clos le … » si clos) ; bloc urgence (contact, téléphone) ; tableau des personnes habilitées ; **bloc « Fiche médicale » uniquement si `MedicalNotes` non vide** (sinon aucune trace du titre du bloc) ; tableau des sorties de l'année (dates, motif, accompagnateur avec pastille « non habilité » si `IsCompanionUnlisted`, statut) ; pied « Document confidentiel — édité le … ». Le générateur suit `HizbReportPdfGenerator` (`LicenseType.Community`, `PdfRenderGuard`, repli sans logo).

- [ ] **Step 1 : tests (échouent)** avec `PdfText.Extract` :
  1. Avec `MedicalNotes = "Allergie sévère aux arachides"` : le texte contient la fiche médicale, le nom, le matricule, le pavillon, le contact d'urgence.
  2. Avec `MedicalNotes = null` (rôle Secrétariat) : le texte ne contient **ni la valeur, ni le titre « Fiche médicale »**. *Test jumeau du 1 sur le même DTO sauf ce champ, pour que l'absence prouve bien le masquage et non un PDF vide* : vérifier d'abord que le texte du cas 2 contient bien le nom et le matricule.
  3. Séjour clos → « Séjour clos » présent ; sortie non habilitée → pastille « non habilité » présente ; liste de sorties vide → document généré sans exception.
  4. Nom arabe renseigné → génération sans exception et PDF plus volumineux que sans nom arabe (la police arabe est embarquée) ; sans nom arabe → aucun bloc arabe.
  5. Logo `null` et logo invalide → repli sans échec (comme les autres générateurs) ; en-tête `%PDF-`.
- [ ] **Step 2 : lancer — échec de compilation.**
- [ ] **Step 3 : implémenter** document + générateur + enregistrement DI (`services.AddSingleton<IBoarderSheetPdfGenerator, BoarderSheetPdfGenerator>();`).
- [ ] **Step 4 : relancer — PASS.**
- [ ] **Step 5 : commit** `feat(internat): fiche individuelle de pensionnaire en PDF`

---

### Task 4 : document PDF du registre du pavillon

**Files:**
- Create: `Application/Common/Interfaces/IDormitoryRegisterPdfGenerator.cs`, `Infrastructure/Documents/DormitoryRegisterDocument.cs`, `DormitoryRegisterPdfGenerator.cs`
- Modify: `Infrastructure/DependencyInjection.cs`
- Test: `tests/SamaEcole.UnitTests/Boarding/DormitoryRegisterDocumentTests.cs`

**Interfaces:** `interface IDormitoryRegisterPdfGenerator { byte[] Generate(DormitoryRegisterDto register, byte[]? logo); }`.

Mise en page A4 **paysage**, imprimable et annotable à la main : en-tête d'établissement ; titre « Registre du pavillon — {pavillon} » ; ligne « Nuit du {date} · Surveillant : {nom} » ; tableau `Chambre | Lit | Nom (+ arabe) | Matricule | Classe | Présence | Observations` ; la colonne **Présence** porte une case vide (`☐ Présent  ☐ Absent`) quand rien n'est pointé, « Présent » / « Absent » quand le pointage existe ; une ligne **en permission** est signalée (« En permission » en observations) ; pied « Document interne — ne pas afficher » et pagination ; en-têtes de tableau répétés sur chaque page ; **aucune** colonne de téléphone ni de santé.

- [ ] **Step 1 : tests (échouent)** avec `PdfText.Extract` :
  1. Texte : nom du pavillon, date, nom du surveillant, noms des élèves, matricules, chambres/lits.
  2. **Confidentialité** : sur un DTO dont les noms et notes (`Note`) sont fixés, vérifier que le texte ne contient ni `"Médical"` ni `"Téléphone"` ni un numéro de téléphone — et, test jumeau, que le texte contient bien les noms (preuve que le PDF n'est pas vide).
  3. Ligne pointée « Présent » / « Absent », ligne non pointée avec case vide, ligne en permission signalée.
  4. 120 lignes → plusieurs pages, document généré, en-tête de tableau répété (le libellé « Matricule » apparaît à chaque page).
  5. Registre vide (pavillon sans pensionnaire) → généré avec un message « Aucun pensionnaire », sans exception.
- [ ] **Step 2 : lancer — échec.** **Step 3 : implémenter** + DI. **Step 4 : relancer — PASS.**
- [ ] **Step 5 : commit** `feat(internat): registre imprimable du pavillon en PDF`

---

### Task 5 : requêtes PDF auditables et routes HTTP

**Files:**
- Create: `Application/Boarding/Documents/BoarderSheet/GetBoarderSheetPdfQuery.cs`, `Application/Boarding/Documents/DormitoryRegister/GetDormitoryRegisterPdfQuery.cs`
- Modify: `Web/Controllers/BoardingController.cs`
- Test: `tests/SamaEcole.FunctionalTests/Boarding/BoardingPdfEndpointsTests.cs`

**Interfaces:**
- Produces :
```csharp
public record GetBoarderSheetPdfQuery(Guid BoarderId) : IRequest<BoardingPdfResult>, IAuditableRequest;
public record GetDormitoryRegisterPdfQuery(Guid DormitoryId, DateOnly? Date) : IRequest<BoardingPdfResult>, IAuditableRequest;
public record BoardingPdfResult(byte[] Content, string FileStem);   // matricule / nom du pavillon assaini
```
Routes : `[HttpGet("boarders/{id:guid}/pdf")]`, `[HttpGet("dormitories/{id:guid}/register-pdf")]` avec `[FromQuery] DateOnly? date`, `[Produces("application/pdf")]`, 200/403/404/422, `Content-Disposition: inline; filename="Fiche-Pensionnaire-{matricule}.pdf"` / `"Registre-{pavillon}-{date}.pdf"` (noms assainis : lettres, chiffres, `-`, `_`).

- [ ] **Step 1 : tests fonctionnels (échouent)**, amorçage comme `LeavesAndAttendanceEndpointsTests` :
  1. **Fiche — Directeur et Surveillant** : le texte du PDF contient la fiche médicale renseignée ; **Secrétariat : 200 mais le texte ne contient ni la valeur ni « Fiche médicale »** (texte extrait avec `PdfText`, test jumeau vérifiant la présence du nom de l'élève dans le même PDF).
  2. **Registre — tous rôles de lecture** : le texte ne contient ni la fiche médicale ni les numéros de téléphone du contact d'urgence ni celui du surveillant du pavillon, alors que ces valeurs sont renseignées en base.
  3. En-têtes : `Content-Type: application/pdf`, `Content-Disposition` inline avec nom assaini ; octets débutant par `%PDF-`.
  4. **Statuts** : séjour / pavillon inconnu → 404 ; registre d'une date future → 422 ; `Enseignant`/`Finance` → 403 sur les deux routes ; anonyme → 401 ; module désactivé → 403 `MODULE_DISABLED`.
  5. **Audit** : après l'impression de la fiche, une ligne du journal d'audit existe pour l'action, et **ne contient pas** la fiche médicale (chercher la valeur secrète dans la ligne d'audit).
- [ ] **Step 2 : lancer — 404 (routes absentes).**
- [ ] **Step 3 : implémenter** les deux handlers sur le modèle de `GetStudentHizbReportPdfQueryHandler` (`ISender` → DTO, `ISchoolLogoProvider.TryFetchAsync`, générateur) et les deux actions minces.
- [ ] **Step 4 : relancer** ces tests + `BoardersEndpointsTests` + `LeavesAndAttendanceEndpointsTests` (non-régression) — PASS.
- [ ] **Step 5 : commit** `feat(internat): routes PDF de la fiche pensionnaire et du registre du pavillon`

---

### Task 6 : documentation, suite complète, PR

- [ ] **Step 1 : docs.** `docs/Volume_4_API_Design.md` : §31.4 « Documents PDF (lot E) » (deux routes, rôles, règle de masquage, nom de fichier, audit, date du registre) ; `ACTIVE_CONTEXT.md` : entrée « lot E », mise à jour du « Reste » (restent G puis F). Dire explicitement : **aucune migration, aucun script post-déploiement**.
- [ ] **Step 2 : suite complète SÉQUENTIELLE**, journaux dans un fichier (build, unitaires, `npm test`, fonctionnels, intégration, ~75 min). Consigner les chiffres exacts ; tout échec est rejoué seul avant d'être qualifié de transitoire ; lancer **aussi** les classes entières du lot précédent touchées (une régression de test du lot D n'a été vue que par la suite complète).
- [ ] **Step 3 : commit docs** `docs(internat): API des PDF de pensionnaires et de pavillons et état du lot E`.
- [ ] **Step 4 : PR** en brouillon, base `feat/internat-leaves-attendance`, description avec décisions E1-E5, résultats réels, « aucune migration ». Push à la charge de l'équipe.

---

## Auto-revue

- **Couverture de la spec §5.2/§7/§9** : fiche (identité bilingue, classe, pavillon/chambre/lit, dates, contact, personnes habilitées, fiche médicale selon le rôle, sorties de l'année) → T2/T3 ; registre (chambre/lit/nom/classe, colonne de pointage vide ou remplie, sorties en cours signalées, sans santé ni téléphone) → T2/T4 ; masquage prouvé sur le texte du PDF, avec test jumeau anti-faux-négatif → T3/T4/T5 ; rôles, garde de module, audit → T5. Hors lot : IHM (G), suppression des colonnes (F).
- **Risque principal** : l'extraction de texte (E1). Si PdfPig ne lit pas la police embarquée, le plan s'arrête à la tâche 1 plutôt que d'affaiblir les tests.
- **Cohérence des noms** : `BoarderSheetDto`, `GetBoarderSheetQuery`, `GetBoarderSheetPdfQuery`, `DormitoryRegisterDto`, `DormitoryRegisterRowDto`, `GetDormitoryRegisterQuery`, `GetDormitoryRegisterPdfQuery`, `BoardingPdfResult`, `IBoarderSheetPdfGenerator`, `IDormitoryRegisterPdfGenerator`, `PdfText.Extract` — identiques dans toutes les tâches.
- **Limite connue** : la date du registre est une nuit unique ; un registre de période n'est pas prévu.
