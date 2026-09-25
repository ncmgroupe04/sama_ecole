# ACTIVE_CONTEXT — Périmètre V1 de Sama Ecole

État de référence du périmètre livré, tenu à jour à chaque clôture de sprint. Ce fichier ne porte
**aucune règle** : les règles non négociables vivent dans `AGENTS.md`, la spécification fonctionnelle
dans `docs/Volume_1_Cahier_des_Charges.md`. Il répond à une seule question — *qu'est-ce qui est dans
la V1, et qu'est-ce qui n'y est pas ?*

**Dernière mise à jour : 25/09/2026** (Conformité pédagogique et institutionnelle — Évolution N°7 : PV du conseil,
cartographie IEF, programmes et volumes horaires, voir §2 ; Séries du Baccalauréat, matières par classe et options — Évolution N°6,
voir §2 ; Appel par cours de l'emploi du temps et billets d'entrée visant
un cours — Évolution N°5, voir §2 ; Notes — fenêtre de correction de l'Enseignant, saisie par le
Secrétariat et fiche de saisie papier PDF, voir §2 ; Module Cahier de texte / Journal de classe (JGK-P04) —
**livré : entité + migration RLS, `ClassJournalScopeAuthorizer` (TeacherAssignment + ScheduleSlot),
règle des 15 jours, CQRS complet, contrôleur, écran `/cahier-de-texte`, tests unitaires et
d'intégration**, voir §2 ; Module Internat — **livré : back-end, persistance, migration RLS, écran
`/internat` avec modale d'affectation, badge fiche élève, formulaire d'inscription**, voir §2 ;
Module M « Intégration étatique / Passerelle SIMEN » — **livré : back-end, persistance, migrations
RLS, écrans et documentation**, voir §2 ; JGK-F09 — contrôle du comptage physique et écarts de caisse
à la clôture le 29/08/2026, voir §5 ; câblage de la session de caisse sur `/caisse` le 27/08/2026 —
JGK-F02 était inutilisable en production faute d'écran d'ouverture/clôture ; écran `/examens` livré
le 26/08/2026 — backend Module J déjà complet ; module Inventaire — API, migration RLS, PDF, tests et
écran `/inventaire` livrés).

---

## 1. Hors périmètre V1

### Portail Parents & Élèves + Messagerie (`docs/Volume_1_Cahier_des_Charges.md` §13) — **reporté en V3**

Sont concernés, et **uniquement** eux :

| §  | Sujet | Statut |
|---|---|---|
| 13.1 | Rattachement Parent ↔ Élève | Reporté V3 |
| 13.2 | Compte Élève — restriction de cycle (Collège/Lycée) | Reporté V3 |
| 13.3 | Consultation Parent (lecture seule) | Reporté V3 |
| 13.4 | Consultation Élève (lecture seule) | Reporté V3 |
| 13.5 | Messagerie et annonces | Reporté V3 |
| 13.6 | Isolation et sécurité des portails | Reporté V3 |

Conséquences concrètes pour toute personne — ou tout agent — qui travaille sur ce dépôt :

- Les rôles `Parent` et `Eleve` **ne doivent pas** être ajoutés à l'énumération `Role`, ni apparaître
  dans un `[Authorize]`, tant que la V3 n'est pas ouverte.
- Aucun endpoint public de consultation par un tiers non-personnel de l'établissement.
- La communication avec les parents en V1 passe **exclusivement** par les canaux sortants déjà
  livrés : notification SMS (`SmsDispatcher`, formule Premium), WhatsApp et e-mail transactionnel.
  Ces envois passent désormais par une **file d'attente** (statuts `Pending` → `Sent` → `Delivered`,
  accusés de réception signés) — voir §4 ci-dessous.
- Les convocations de parent/tuteur (`/convocations`) ne sont **pas** un portail : c'est un registre
  interne du module Vie Scolaire, imprimé et remis en main propre.

Ce report ne remet en cause ni le modèle de données ni la RLS : le jour où le portail sera ouvert,
il devra respecter à la lettre la règle #2 d'`AGENTS.md` (Global Query Filter **et** policy RLS).

---

## 2. Modules livrés et intégrés

Livrés, câblés à l'IHM, et couverts par la suite de tests :

| Module | Écran | Backend |
|---|---|---|
| **Paie** | `/paie` | Fiches de paie, contrats employés, heures enseignants |
| **Trésorerie** | `/tresorerie` | `GetTreasuryDashboardQuery` — encaissements + décaissements consolidés |
| **Caisse** | `/caisse` | Encaissements de guichet, reçus |
| **TVA / Fiscalité** | `/fiscalite` | VRS / IPRES, `GenerateTaxDeclarationCommand` |
| **Infrastructures** | `/infrastructures` | Bâtiments & salles |
| **Documents** | Module Documents | Génération et archivage documentaire |
| **Rapports financiers** | `/rapports/financiers` | `GetRevenueConsolidationQuery` + export `.xlsx` |
| **Inventaire** | `/inventaire` | API `/api/v1/inventory` — catalogue, journal de stock, prêts, 2 PDF |
| **Examens officiels** | `/examens` | API `/api/v1/exams` — sessions, dossiers CFEE/BFEM/BAC, audit, attribution centre/table, transmission, résultats, statistiques, export ministériel, convocations |
| **Intégration étatique** | `/integration-etatique` + fiche élève + `/verifier/mutation/{token}` | API `/api/v1/state-integration` — IEN, export Planète (CSV/JSON), rapport STATEDUC (PDF + Excel), certificat de mutation avec QR + registre + révocation, livret de compétences, vérification publique du certificat |
| **Internat** | `/internat` + fiche élève + formulaire d'inscription | API `/api/v1/internat` — tableau de bord par chambre, recherche d'élève, affectation/transfert/libération ; module désactivé par défaut |
| **Cahier de texte** | `/cahier-de-texte` | API `/api/v1/class-journal` — journal de classe paginé/filtrable, écriture Enseignant sur son propre créneau (`ClassJournalScopeAuthorizer`), correction 15 jours puis Directeur/Secrétariat |

Le socle V1 (Élèves, Inscriptions, Classes, Matières, Enseignants, Notes & Bulletins, Frais,
Présences, Surveillance générale, Abonnements & Facturation, Console Super Admin) est livré depuis
les sprints précédents.

### Notes — fenêtre de correction, Secrétariat et fiche papier (24/09/2026) — livré (Évolution N°1)

Trois changements sur `/grades` et l'écran `/notes`. Spécification : `docs/Volume_7_Security.md` « Notes »,
`docs/Volume_4_API_Design.md` §8, `openapi.yaml`.

- **Fenêtre de correction de l'Enseignant.** `SchoolSettings.GradeEditWindowDays` (7 par défaut, 1–365,
  migration `AddGradeEditWindowDays`, réglée par le Directeur dans Paramètres › Notation & mentions).
  `PUT /grades/{id}` s'ouvre à l'Enseignant, mais `GradeEditPolicy` le borne : délai depuis la saisie ≤ fenêtre
  (borne incluse) **et** auteur de la note **ou** affecté à la classe/matière (`TeacherAssignment`, année du
  trimestre) — 403 sinon, jamais 409. Directeur et Secrétariat corrigent sans limite. Cela **remplace** le
  modèle « Photoshop » (correction réservée au Directeur et au Secrétariat).
- **Secrétariat.** Il saisit désormais (`POST /grades`, import Excel), pour toutes les classes. `DELETE` reste
  Directeur/Secrétariat ; `GET /grades/calculate` reste Directeur/Enseignant (`SummaryRoles`) : l'extension de
  la saisie ne lui ouvre pas la consultation des moyennes.
- **Fiche de saisie papier.** `GET /grades/sheet/print` : PDF vierge (QuestPDF, `GradeSheetDocument`) — élèves
  par ordre alphabétique **français** (tri en mémoire, pas la collation de la base), cases Note et
  Appréciation vides, barème de la matière ou du cycle. Bouton « Fiche papier » sur l'écran de saisie.

**Trois arbitrages actés, à ne pas rouvrir sans raison :**

1. **La règle vit à un seul endroit.** `GradeEditPolicy` (pure) + `GradeCorrectionAuthorizer` servent la
   correction unitaire, l'**import Excel** et le champ `canEdit` de chaque cellule de la grille. L'import
   modifie aussi des notes existantes : sans ce contrôle, un Enseignant hors fenêtre corrigeait par fichier ce
   que l'API lui refuse (contournement qui existait déjà sous l'ancien modèle).
2. **`Grade.CreatedBy` est désormais renseigné.** Aucun code ne le posait avant cette évolution
   (`SaveChangesAsync` ne stampe que `CreatedAt`/`UpdatedAt`), donc le critère « auteur » n'avait aucune donnée
   à lire. Il est posé explicitement à la création (saisie unitaire et import) — pas globalement dans le
   contexte, pour ne pas changer le comportement de toutes les entités. Les notes **antérieures** n'ont pas
   d'auteur connu : l'Enseignant ne les corrige que par l'affectation.
3. **`CanEdit` est un confort d'affichage, jamais une garde.** La cellule grisée suit le serveur ; la vraie
   garde reste `PUT /grades/{id}` (403).

**Écart corrigé (24/09/2026) :** `settings.js` (Paramètres › Configuration) envoyait un
`PUT /schools/current/settings` construit champ par champ, qui **omettait** les alertes SMS et
`debtorReminderThresholdDays` — chaque enregistrement depuis cet écran les ramenait à leurs valeurs par défaut.
`saveConfig()` relit désormais `GET /schools/current/settings` juste avant le PUT et étale cet état sous les
champs qu'il pilote (`{ ...current, …champs de l'écran }`), comme `sms-settings.js`. Deux effets : aucune
omission possible, y compris pour un champ ajouté plus tard ; et aucune valeur **périmée** (un enregistrement de
l'écran SMS survenu depuis le chargement de la page n'est plus écrasé). Le PUT reste un remplacement complet
côté serveur : **tout nouvel écran qui écrit ces réglages doit relire avant d'écrire.** Verrouillé par
`tests/js/settings-save-preserves-fields.test.mjs`.

### Module Coran / Franco-Arabe — Phases 1 et 2 (20/09 → 24/09/2026) — API livrée, aucun écran

Branche `feature/franco-arabic-core`. Spécifications : `docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md`
(Phase 1) et `…-franco-arabic-cqrs-api-design.md` (Phase 2) ; plans jumeaux dans `docs/superpowers/plans/`.
Routes : `openapi.yaml` (préfixe `/api/v1/quran`). **Aucun écran ni JavaScript à ce stade** — l'API seule.

- **Phase 1 — socle de données.** Enums `SectionType` (Français / Arabe / Études islamiques), `SchoolType`
  (Standard / FrancoArabic / Daara) et `QuranMemorizationStatus` (InProcess / Memorized / Revised) ;
  `Subject.SectionType` (défaut French) et `SchoolSettings.SchoolType` (défaut Standard) ; entités `QuranProgress`
  (Juz, Hizb, Sourate, statut) et `QuranEvaluation` (erreurs mémoire/tajwid, hésitations, note finale), toutes deux
  sous verrou `xmin` ; migrations `AddQuranCoreModule` (RLS incluse) et `AddQuranModuleToResetSchoolData`
  (`quran_progress`/`quran_evaluations` ajoutées à `reset_school_data`). Tests : valeurs par défaut, verrou
  optimiste, isolation RLS.
- **Phase 2 — CQRS et API.** 4 commandes (création/correction de progression et d'évaluation, avec verrou
  `RowVersion` → 409, journal d'audit `IAuditableRequest`), 4 requêtes (par élève et par classe, pour chaque
  entité), validateurs FluentValidation, `QuranController` (`[RequireModule(SchoolModule.Coran)]`).
  Écriture : Directeur + Enseignant ; lecture : + Secrétariat. Tests unitaires (validateurs, enums, entités) et
  d'intégration (une classe par commande/requête + isolation multi-tenant).

**Arbitrages actés (spec Phase 2 §2), à ne pas rouvrir sans raison :** aucune garde fine par affectation (comme
`CreateGradeCommandHandler`) ; `FinalScore` sans plafond (seule la borne `≥ 0` — aucun barème spécifié pour
l'oral coranique) ; `Juz`/`Hizb`/`Sourate`/`Élève` immuables en correction ; **`SchoolType` est purement
informatif** — `IsCoranModuleEnabled` reste le seul interrupteur lu par `ModuleAuthorizationHandler`.

**Reste à faire :** écran(s) Coran (lot séparé, comme l'Internat) et intégration de `SectionType` au bulletin
(aucun effet sur `Coefficient` ni sur le calcul des moyennes tant que ce lot n'existe pas).

### Périodes d'évaluation dynamiques (24/09/2026) — livré (Évolution N°2)

Le découpage de l'année n'est plus « 3 trimestres » en dur. Branche `feature/evaluation-periods`. Plan :
`docs/superpowers/plans/2026-09-24-evaluation-periods.md`.

- **Réglage.** `SchoolSettings.EvaluationPeriodType` (`Trimester` défaut / `Semester` / `Custom`) et
  `CustomPeriodCount` (2 à 6, lu seulement pour `Custom`), migration `AddEvaluationPeriodType`, réglés par le
  Directeur dans Paramètres › Pédagogie. `PeriodSchedule` (Application) remplace `TermSchedule` : nombre,
  libellés (« 1er trimestre », « 1er semestre », « 1re période ») et dates à parts égales, la dernière période
  absorbant le reste — l'ancien découpage trimestriel est reproduit à l'identique (test figé).
- **Génération.** `CreateSchoolYearCommandHandler` lit le réglage (une école sans ligne garde les trimestres).
  Les sélecteurs de notes, moyennes et bulletins listaient déjà `GET /school-years/{id}/terms` : ils suivent sans
  autre code. Reste du vocabulaire « Trimestre » remplacé par « Période » dans l'interface et l'aide.
- **Documents.** Titre du bulletin dérivé du libellé (`BulletinTitle` : « BULLETIN DU 1ER SEMESTRE », « … DE LA 1RE
  PÉRIODE »), repli « BULLETIN DE NOTES » sans libellé ; ligne « Période » de la fiche de saisie papier.
  `docs/design-references/README.md` §2 mis à jour (règle #12).

**Quatre arbitrages actés (24/09/2026), à ne pas rouvrir sans raison :**

1. **« Personnalisé » = un nombre de périodes de 2 à 6**, à parts égales. Libellés et dates éditables période par
   période : hors périmètre.
2. **Le titre du bulletin s'adapte dynamiquement** — dérogation assumée au titre fixe de la référence visuelle,
   consignée dans `design-references/README.md`.
3. **Le réglage n'agit que sur les années créées ensuite.** Une année déjà créée **garde son nombre de périodes**
   quand ses dates changent (`UpdateSchoolYearCommandHandler` recale les `Term` existants, jamais le réglage).
   `POST /school-years/{id}/apply-evaluation-periods` rejoue le découpage sur une année existante, en **blocage
   strict** : refusé (422) dès qu'UNE note ou UNE appréciation de bulletin existe sur l'année, et sur une année
   terminée. Les anciennes périodes sont archivées (suppression logique), pas effacées.
4. **Libellés complets à l'écran** (« 1er semestre »), aucun code court « S1/T1 ».

**Piège de la migration en local :** un serveur de dev lancé verrouille les DLL du dossier `Debug` ; pour générer
une migration sans l'arrêter, `dotnet ef migrations add … --configuration Release`.

### Jours ouvrés et week-ends configurables (24/09/2026) — livré (Évolution N°3)

Chaque école définit ses jours ouvrés (ex. repos jeudi et vendredi pour une école franco-arabe ou un daara).
Branche `feature/working-days`, empilée sur `feature/evaluation-periods` (à fusionner après elle). Plan :
`docs/superpowers/plans/2026-09-24-working-days.md`.

- **Réglage.** `SchoolSettings.WorkingDays` (texte « Monday,Tuesday,… », défaut base lundi → samedi), migration
  `AddSchoolWorkingDays`, réglé par le Directeur dans Paramètres › Notation & mentions (préréglages « Lundi →
  Vendredi », « Lundi → Samedi », « Samedi → Mercredi »). `PUT` : `workingDays` absent ou `null` = **inchangé**
  (ce réglage verrouille des écritures, un ancien client ne doit pas le réinitialiser).
- **Logique.** `SchoolWeek` (Application, pur, testé sans base) : analyse, forme canonique, ordre d'affichage,
  noms français. `WorkingDayGuard` lit le réglage de l'école COURANTE (RLS) et lève un 422 lisible.
- **Verrous.** Appel des élèves (soumission **et** ouverture de la feuille), pointage des enseignants, création et
  déplacement de créneaux. Côté écran : la grille suit la semaine de l'école, le sélecteur « Jour » n'offre que
  les jours ouvrés, l'écran d'appel affiche un bandeau et n'envoie aucune requête un jour de repos.

**Six arbitrages actés (24/09/2026), validés sans modification :**

1. **D1 — défaut lundi → samedi** (la grille historique) : aucune école existante ne perd de jour ; seul le
   dimanche devient jour de repos par défaut.
2. **D2 — appels déjà saisis un jour devenu repos : conservés dans les taux.** Le verrou ne joue que sur les
   saisies nouvelles ; rien n'est réécrit rétroactivement.
3. **D3 — créneaux hérités : approche souple.** Le réglage se change librement ; la grille garde la colonne
   marquée « repos » ; on peut y lire et supprimer, pas créer ni modifier (même pour changer seulement la salle).
4. **D4 — périmètre du verrou.** Verrouillés : appel élèves, pointage enseignants, créneaux. NON verrouillés, à
   dessein : billets d'entrée/sortie et retards, justificatifs, journal de classe, heures de paie.
5. **D5 — ordre d'affichage.** La semaine commence le lendemain du bloc de repos (repos jeudi/vendredi → samedi,
   dimanche, lundi, mardi, mercredi) ; repos non contigus : lundi → dimanche. Calculé une fois, côté serveur.
6. **D6 — aucun calendrier de jours fériés ni de vacances.**

**Invariant : aucun calcul de taux n'a changé.** Ils portent sur les appels réellement saisis (jamais des jours
calendaires), donc un jour de repos sans appel n'entre dans aucun dénominateur ; des tests
(`AttendanceRateWorkingDaysTests`) figent ce comportement contre une future refonte du dénominateur.

### Coefficients par série et surcharge du Directeur (24/09/2026) — livré (Évolution N°4)

Un lycée règle le coefficient d'une matière par série (L1, L2, S1, S2, TECH) ou par classe. Branche
`feature/series-coefficients`, empilée sur `feature/working-days`. Plan :
`docs/superpowers/plans/2026-09-24-series-coefficients.md`. Spécification fonctionnelle :
`docs/Volume_1_Cahier_des_Charges.md` §8.7.

- **Données.** `Classroom.Series` (nullable, Lycée seulement) et table `subject_coefficient_overrides`
  (portée série ou classe, par année scolaire) : `SchoolId`, Global Query Filter **et** policy RLS, `xmin`
  (409), suppression logique, ajoutée à `reset_school_data` et aux purges d'année.
- **Calcul.** `SubjectCoefficients.Resolve` (pur) : **classe › série › matière**, appelé par les deux seuls
  lecteurs de coefficient (`GetGradeSummaryQueryHandler`, `GetStudentDetailQueryHandler`) — bulletins PDF,
  bulletins de classe, délibération et fiche élève suivent. Primaire/Maternelle : 1, inchangé.
- **API.** `/api/v1/coefficients` : catalogue, grille, `PUT` (upsert), `DELETE` (« Rétablir »), `carry-over`,
  `apply-template`. Lecture Directeur + Secrétariat, écriture **Directeur seul**, module `Pedagogy` requis.
- **Modèles nationaux.** `SeriesCoefficientTemplates` (une seule table de données) : L1, L2, S1, S2 validés
  par la direction le 24/09/2026 ; **TECH sans modèle** (aucune valeur fournie, `apply-template` répond 422).
  Matérialisés en surcharges de série par « Appliquer le modèle », jamais utilisés comme repli au calcul.
- **Écrans.** Classes : champ « Série » (lycée) + pastille. Matières › onglet **Coefficients** : grille
  Base · Surcharge · Effectif · Origine, « Rétablir », « Appliquer le modèle », « Reprendre l'année
  précédente », avertissement rétroactif. Fiche d'aide « Coefficients par série » (`help.js`).

**Invariant : sans surcharge, le calcul est strictement celui d'avant.** Un lycée qui n'a rien paramétré (ou
qui contourne encore par « une matière par niveau texte ») voit ses bulletins inchangés le jour du déploiement.

**Points de vigilance connus :**
1. Les modèles ne distinguent pas Première et Terminale : la table valait pour la série ; un bulletin réel de
   1re S2 (Maths 5, Sc. physiques 6, SVT 6…) peut différer du modèle S2 — à confronter au texte officiel.
2. Effacer la série d'une classe change des coefficients : l'écran Classes renvoie toujours la série au `PUT`,
   mais un ancien client qui l'omet l'effacerait.
3. Le cycle de notation (barème /10 ou /20) d'un élève reste celui de sa classe actuelle, y compris pour les
   années passées ; seuls les coefficients suivent l'inscription de l'année.
4. Le niveau d'une matière est un texte libre : la grille liste « toutes les matières hors primaire/maternelle ».

### Appel par cours et billets d'entrée (25/09/2026) — livré (Évolution N°5)

L'appel se fait par cours de l'emploi du temps, et un billet d'entrée vise un cours que l'enseignant accepte en
classe. Branche `feature/attendance-slots`, empilée sur `feature/series-coefficients`. Plan et arbitrages :
`docs/superpowers/plans/2026-09-24-attendance-slots-tickets.md`. Spécification fonctionnelle :
`docs/Volume_1_Cahier_des_Charges.md` §21.4.

**Arbitrages B1 à B13 — validés à 100 % sans modification :**

| # | Décision retenue |
|---|---|
| B1 | **Les deux modes coexistent** : « Par créneau » (défaut si la classe a des cours ce jour-là) et « Libre » (demi-journée) inchangé. |
| B2 | `AttendanceSheet.ScheduleSlotId` nullable ; le serveur vérifie (classe, matière, jour) et **dérive** `Period` — celle du client est ignorée. L'index unique existant suffit. |
| B3 | Enseignant : **ses** cours (titulaire + affectations). Directeur, Secrétariat, Surveillant : tout cours (le remplaçant). |
| B4 | Absence complète / partielle / retard = **classification calculée** (`DayAttendanceClassifier`) sur les séances appelées ; aucun nouveau statut. |
| B5 | Le billet **étend `LateArrival`** ; statut `null` = billet sans cours visé = comportement d'avant, historique compris. |
| B6 | Le registre change **à l'émission** (ligne → `Late`, statut d'avant conservé ; sinon présélection à l'ouverture de la feuille). L'annulation restaure. |
| B7 | Le billet ne justifie **pas** les séances manquées plus tôt. La Tâche 9 (« justifier les séances manquées ») est **hors périmètre** et n'est pas construite. |
| B8 | `AttendanceRecordedEvent` réutilisé quand une absence devient un retard par un billet (SMS/WhatsApp de rectification, via `SmsDispatcher`). |
| B9 | Acceptation : enseignant **titulaire** du cours ou Directeur. Un billet accepté ne s'annule plus. |
| B10 | **Billet de sortie hors périmètre** (inchangé). Billet d'entrée émissible sans cours visé. |
| B11 | Un cours par billet ; **un billet actif** par (élève, cours, jour) — index unique partiel. |
| B12 | Émission : SuperAdmin, Directeur, Surveillant ; le Secrétariat imprime. Le cours en cours (à défaut le suivant) est présélectionné. |
| B13 | Pas de notification poussée : le billet apparaît sur la feuille de l'enseignant (pastille + « Accepter »). |

- **Données.** Colonnes seulement (`attendance_sheets.ScheduleSlotId`, `student_attendances.EntryTicketId`,
  `LateArrivals.*`) et index partiel `UX_LateArrivals_ActiveTicket` — voir `docs/Volume_3_DDS.md` §4.4.
  Migration `AddAttendanceSlotAndEntryTicketWorkflow`. Aucune migration de données.
- **API.** `GET /attendance/slots`, `scheduleSlotId` sur roster/soumission, `GET /absences/today-slots`,
  `targetScheduleSlotId` sur `POST /absences/late-arrivals`, `POST /billets/{id}/accept|cancel`
  (`EntryTicketActionsController`, séparé de `BilletsController` à dessein), `GET /reports/attendance/by-subject`.
  Routes : `docs/Volume_4_API_Design.md` §10 ; schémas : `openapi.yaml` ; rôles : `docs/Volume_7_Security.md` §15.
- **Écrans.** Présences : pastilles « Cours de la journée », bascule appel libre, billet et « Accepter » sur la ligne
  de l'élève. Billets : sélecteur du cours visé, colonne Statut, « Annuler » avec confirmation. Rapport d'assiduité :
  colonnes jours d'absence complète/partielle (infobulle « N séances appelées »), onglet « Par matière ». Le billet
  PDF porte le cours et son statut ; un billet annulé se lit « BILLET ANNULÉ ». Fiches d'aide `appel-classe`,
  `billets-entree-sortie`, `rapport-assiduite-detaille` mises à jour.

**Invariant : le taux de présence n'a pas changé** — `(Présents + Retards) / lignes d'appel`, au rapport comme au
tableau de bord. Sans `scheduleSlotId` ni cours visé, tout se comporte exactement comme avant.

**Points de vigilance connus :**
1. Annuler un billet dont la ligne n'avait **aucun statut d'avant** (la feuille n'existait pas à l'émission : le
   retard a été présélectionné puis soumis par l'enseignant) laisse la ligne **telle que l'enseignant l'a saisie**
   — typiquement `Late` — simplement détachée du billet : il n'y a rien à restaurer, et un statut d'avant inventé
   serait faux. La Vie Scolaire corrige alors la ligne à la main.
2. **Concurrence** : un billet émis exactement pendant la soumission de la fiche peut ne pas être rattaché à sa
   ligne. L'acceptation par l'enseignant est le point de réconciliation (elle applique le retard à la ligne
   existante) et la feuille rechargée le montre ; il n'y a pas de verrou `xmin` sur `StudentAttendance`. Deux
   émissions simultanées pour le même (élève, cours, jour) sont, elles, arbitrées par l'index unique partiel (`409`).
3. La classification « complète » se fonde sur les séances **appelées** : une seule fiche saisie ne prouve pas une
   journée entière — d'où l'infobulle « N séances appelées » qui donne le dénominateur.
4. `isCurrent` / `isNext` se calculent sur l'heure **UTC** du serveur (`TimeProvider`) : exact au Sénégal (UTC+0),
   à revoir pour un déploiement dans un autre fuseau.
5. Le billet de sortie reste un registre à part, sans lien avec le cours. (La justification des séances manquées,
   « Tâche 9 », est **remplacée** par le Complément N°5 bis ci-dessous, sans table de journal.)

#### Complément N°5 bis (25/09/2026) — appel à trois statuts et billet par heure d'arrivée

Plan et arbitrages C1 à C9 : `docs/superpowers/plans/2026-09-24-attendance-slots-tickets.md` (fin du document).
Branche `feature/attendance-arrival-time`, empilée sur `feature/attendance-slots`.

- **Appel.** La grille ne propose que **Présent / Absent (justifié) / Absent (non justifié)** ; plus de colonne
  « Retard (min) ». Une ligne issue d'un billet, et tout retard historique, est en **lecture seule**. **Changement
  d'API assumé (C9)** : `POST /attendance` refuse (422) une ligne `Late` sans billet actif sur (élève, cours,
  date), y compris en appel libre. Lignes `Late` existantes et **taux de présence inchangés**.
- **Billet.** Le surveillant saisit l'**heure d'arrivée** (C1, validé) ; `ArrivalCoverage` (pur) et `ArrivalPlanner`
  en déduisent cours manqués, retard, cours visé (en cours, sinon prochain, sinon dernier manqué) et durée totale.
  `GET /absences/arrival-preview` = le même calcul, pour l'écran. Minutes et cours visé du client sont **ignorés**
  dans ce mode ; sans cours ce jour-là, saisie des minutes comme avant.
- **Registre (C5, validé).** Les cours manqués dont la fiche existe passent `UnjustifiedAbsence` → `JustifiedAbsence`
  — jamais depuis `Present`/`Late`, jamais l'inverse, aucune ligne créée. **B7 est levé.** Statut d'avant gardé
  **sur la ligne** (`student_attendances.PreviousStatus`) : annulation ligne par ligne, **aucune table de journal**
  (C6). Le cours en cours passe en `Late` (à 0 minute : ligne seulement rattachée). Un seul message famille (C7).
- **Données.** Migration `AddArrivalTimeToEntryTickets`, colonnes nullables seulement : `LateArrivals.ArrivalTime`,
  `TotalMinutes`, `MissedScheduleSlotIds` (`uuid[]`), `student_attendances.PreviousStatus`/`PreviousLateMinutes`.
  À appliquer avec `dotnet ef database update` avant de relancer l'app en local (sinon « Une erreur inattendue »).
- **Billet imprimé.** Heure d'arrivée, durée, cours manqué (nommé s'il est seul, résumé sinon) — toujours une page A5.
  La création d'un billet devient une requête **auditée** (`IAuditableRequest`).

**Points de vigilance du complément :** (1) le passage « non justifié → justifié » n'avertit pas la famille ;
(2) une arrivée pendant une pause vise le cours **suivant** — seul `ArrivalCoverage` change si l'école préfère le
dernier manqué ; (3) le mode « Libre » n'a plus de retard manuel ; (4) un billet ne peut plus être émis « sans cours
précis » depuis l'écran quand la classe a des cours ce jour-là (l'API, elle, l'accepte encore) ; (5) un billet émis
pendant la soumission d'une fiche peut ne pas être rattaché : l'acceptation reste le point de réconciliation.

### Conformité pédagogique et institutionnelle (25/09/2026) — livré (Évolution N°7)

Quatre modules, un commit chacun, branche `claude/senegal-series-subjects-coefficients-ndqwkq`. Spécification :
`docs/Volume_1_Cahier_des_Charges.md` §8.9, §23.7 à §23.9 ; routes : `docs/Volume_4_API_Design.md` §9, §25 à §27.

- **PV du conseil de classe et décisions.** Seuils réglables par le Directeur (`school_settings`, Paramètres ›
  Notation & mentions) : Félicitations ≥ 14, Tableau d'honneur ≥ 12 **sans note éliminatoire** (< 5, jugée sur le
  barème de chaque matière), Encouragements ≥ 12, passage ≥ 10, redoublement ≥ 8,5 (sinon exclusion). PV de période et
  **PV annuel** (en-tête officiel, statistiques Filles / Garçons / Total, taux de réussite). « Appliquer les décisions
  proposées » ne remplace jamais une décision déjà prise ; une proposition s'imprime « Proposé : … », en italique.
- **Cartographie IEF.** Normes d'âge par niveau en code (âge normal − 1 / + 2, au 31 décembre), réglables par l'école
  (`grade_age_norms`) ; avertissement d'âge à l'inscription (jamais un blocage) ; statut Nouveau / Redoublant /
  Transféré (`enrollments.IsTransferredIn`, `PreviousSchoolName`). Écran `/rapports/institutionnels` : effectifs
  classe × âge × sexe, redoublement par niveau, corps professoral (discipline, diplôme, heures) ; export PDF et Excel.
- **Programmes et cahier de texte.** `syllabus_units` (chapitres d'une matière pour un niveau) et
  `class_journal_entry_units` (chapitres cochés dans une séance). Trame nationale codée pour les Mathématiques de 3e
  seulement ; ailleurs, un chapitre par ligne. Écran `/programmes` : avancement par classe × matière, par matière et
  par enseignant (année active), référentiel, volumes horaires.
- **Volumes horaires et conformité des emplois du temps.** Grilles en code (collège ; Seconde S/L ; Première et
  Terminale S1, S2, L1a, L1b, L2), réglables par niveau ou niveau × série (`weekly_hour_norms`). Contrôle par classe
  dans l'emploi du temps « Par classe » (conforme / sous / au-dessus / sans référence, options comptées une fois) et
  chevauchements enseignant / salle / classe de tout l'établissement. La **salle** est désormais refusée à l'écriture
  d'un créneau qui la double.
- **Migrations** `AddCouncilRules`, `AddIefMapping`, `AddSyllabusTracking`, `AddWeeklyHourNorms` : RLS + Global Query
  Filter, `GRANT` sans DELETE, `reset_school_data` corrigée pour les tables qui pendent à `subjects` ou
  `class_journal_entries`. Scripts idempotents et de retour arrière dans `docs/migrations/` (vérifiés sur
  PostgreSQL 16 : double application, retour arrière, réapplication).

**Points de vigilance connus :**
1. Les grilles horaires et la trame de programme sont des **valeurs par défaut indicatives** : à confronter à l'arrêté
   et aux programmes DEMSGS / INEADE en vigueur — chaque école les ajuste sans toucher au code.
2. Le niveau d'une classe se lit sur son **nom** (« 6e B », « Tle S2 A ») : une classe au nom hors nomenclature n'est
   ni contrôlée en âge, ni rattachée à un programme, ni confrontée à une grille horaire.
3. Le contrôle de conformité suppose des créneaux hebdomadaires fixes (l'emploi du temps n'a pas de semaines A/B).
4. Deux créneaux d'une même classe à la même heure (groupes de LV2 en parallèle) restent refusés par la garde
   historique ; le contrôle les signale comme chevauchement de classe s'ils existent déjà.

### Séries du Baccalauréat, matières par classe et options (25/09/2026) — livré (Évolution N°6)

Référentiel des séries de l'Office du Baccalauréat, programme de chaque classe et matières au choix de chaque
élève. Branche `claude/senegal-series-subjects-coefficients-ndqwkq`. Spécification fonctionnelle :
`docs/Volume_1_Cahier_des_Charges.md` §8.8.

- **Référentiel.** `LyceeSeries` étendu : L1a, L1b, L'1, L2, S1 à S5, STEG, T1, T2, STIDD, LA, S1A, S2A (+ anciens
  codes L1, TECH, toujours valides mais plus proposés). `SeriesCoefficientTemplates` porte leurs modèles, avec les
  **groupes d'options** (LV2, Langue ancienne, Option scientifique, Philosophie ou Théologie). **L2, S1 et S2 prennent
  les valeurs du référentiel** (elles remplacent la table du 24/09) ; le modèle L1 est inchangé. Données en code
  (arbitrage A10 de l'Évolution N°4), pas en table : pas de table globale hors tenant à maintenir.
- **Données.** `class_subjects` (programme d'une classe : matière, groupe d'options, `IsCustom`, `IsActive`) et
  `student_subject_enrollments` (option d'un élève pour une année). RLS + Global Query Filter, `xmin`, suppression
  logique, purges. Migration `AddClassSubjectsAndOptions`. **Aucun coefficient dans ces tables** : le coefficient
  d'une classe reste la surcharge de classe de l'Évolution N°4 (par année), une seule vérité.
- **Création de classe.** Une série avec modèle recopie son programme dans la classe (`ClassSubjectTemplateInjector`),
  crée les matières absentes, et pose le coefficient officiel en surcharge de classe là où l'hérité diffère — sauf si
  le Directeur a déjà réglé la série. Ne modifie ni `Subject.Coefficient`, ni une surcharge de série, ni une autre classe.
- **Options.** Inscription (`subjectOptionIds`, même transaction) et fiche élève ; option la plus fréquente de
  l'établissement par défaut. `SubjectFollowScope` est le seul point d'entrée : grille de saisie, fiche papier, modèle
  Excel, import, garde de `CreateGrade`, résumé de notes (bulletins, délibération) et fiche élève.
- **API.** `/api/v1/class-subjects` (programme, ajout, activation/groupe, `reset`, `assign-default-options`, `options`,
  options d'un élève). Programme : lecture Directeur + Secrétariat, écriture Directeur ; options : Directeur +
  Secrétariat. Module `Pedagogy` requis.
- **Écrans.** Classes : « Série / Filière » (« Général / Collège » = aucune série). Matières › onglet **Matières par
  classe** (badge « Option obligatoire », colonne « Officiel », « 🔄 Réinitialiser aux coefficients officiels du
  Sénégal », « Affecter l'option par défaut »). Inscriptions et fiche élève : section « Matières optionnelles ».
  Fiche d'aide `matieres-par-classe-options`.

**Invariant : une classe sans programme garde exactement le calcul d'avant** — toute matière notée figure au
bulletin. Une matière notée hors programme reste suivie ; seules une matière désactivée et une option non choisie
sortent de la grille et du bulletin.

**Points de vigilance connus :**
1. Le référentiel reçu est parfois ambigu ; lectures retenues : Maths (5-6) → 5 en T1/T2/STIDD ; « Matières
   scientifiques (7-8) » → Mathématiques 8 en S1A, SVT 7 en S2A ; « Latin/Grec » et « Théologie/Philo » → groupes
   d'options ; « Construction/Dessin » et « Biologie/Agro » → une matière. À confronter au texte officiel.
2. Changer L2/S1/S2 ne touche pas les surcharges de série déjà posées avec l'ancienne table : « Appliquer le modèle »
   avec remplacement les aligne, et « Réinitialiser » aligne une classe.
3. Un élève **sans choix** dans un groupe n'apparaît dans aucune grille du groupe et une note antérieure sur une option
   non choisie sort du bulletin (elle reste en base). L'écran signale les élèves sans option ; « Affecter l'option par
   défaut » les complète.
4. Le programme d'une classe n'est pas rattaché à l'année (les choix d'options et les coefficients le sont) : désactiver
   une matière en cours d'année la retire aussi des bulletins déjà calculés de l'année.
5. L'écran de saisie des notes propose toujours toutes les matières de l'école ; seule la liste des élèves suit les
   options.

### Dispense d'une matière obligatoire (25/09/2026) — livré

Un élève peut être dispensé d'une matière **obligatoire** de sa classe pour l'année active, avec un **motif**
obligatoire. Branche `feature/optional-subjects` (nom conservé), construite sur `main` après l'Évolution N°6 ;
spécification `docs/superpowers/specs/2026-09-25-subject-exemptions-design.md` (v2) et plan
`docs/superpowers/plans/2026-09-25-subject-exemptions.md`. La v1 (options + dispenses par inscription, redondante avec
l'Évolution N°6) reste consultable sur `backup/optional-subjects-v1`. Spécification fonctionnelle :
`docs/Volume_1_Cahier_des_Charges.md` §8.10.

- **Données.** `student_subject_exemptions` (élève, matière, année, `Reason` non nul ≤ 200) : RLS + Global Query Filter,
  index unique partiel, pas de `xmin`, purges (`reset_school_data`, `delete_school_year`). Migrations
  `AddStudentSubjectExemptions` et `AddStudentSubjectExemptionsToPurges`, scripts `docs/migrations/`.
- **Calcul.** `SamaEcole.Application.Exemptions` (`ExemptionRules`, `ExemptionQueries`, statiques). `SubjectFollowScope`
  réunit les dispenses aux exclusions du programme : résumé de notes, bulletins, délibération, fiche élève, grille,
  import, feuilles PDF/Excel et garde de `CreateGrade` en héritent sans nouveau paramètre de constructeur.
- **API.** `GET`/`PUT /api/v1/class-subjects/students/{studentId}/exemptions` (`StaffRoles`, module `Pedagogy`).
- **Écran.** Fiche élève › section « Dispenses » (`subject-exemptions.js`, `students.js`). Fiche d'aide `dispenses-matieres`.
- **Bulletin.** « Dispensé(e) » (secondaire, primaire, grille APC) : **écart validé à la règle #12**, consigné dans
  `docs/design-references/README.md`. Aucun autre changement de mise en page.

**Invariant : sans dispense, résumé, bulletin, grilles et imports sont strictement ceux de `main`.**

**Points de vigilance connus :**
1. « Dispensé(e) » est le seul écart au bulletin de référence ; tout autre ajout est à arbitrer séparément.
2. Le motif est une donnée sensible : jamais imprimé ni journalisé, lu et écrit par le Directeur et le Secrétariat
   seulement.
3. `SubjectFollowScope` a désormais **deux sources d'exclusion** (programme/options et dispenses) : tout nouveau lecteur
   doit passer par lui, jamais recalculer.
4. Une dispense devient **inerte** (sans être supprimée) si le programme change — matière désactivée ou devenue
   option — ou si l'élève change de classe : elle exclut toujours la matière tant qu'elle existe ; à nettoyer depuis la fiche.
5. Le **livret de compétences** ignore le marquage « Dispensé(e) ».
6. `PUT`/`DELETE` d'une note existante sur une matière dispensée ne sont pas bloqués (invisible dans les moyennes).
7. Le parcours navigateur de la section « Dispenses » et le rendu du bulletin bilingue arabe n'ont pas été vérifiés à l'œil.

### Inventaire (26/08/2026) — API et écran livrés

Suivi du patrimoine, commun aux écoles publiques (tables-bancs, manuels d'État, consommables) et
privées (parc informatique, tenues, matériel de laboratoire). Quatre tables tenant
(`inventory_categories`, `inventory_items`, `stock_movements`, `item_assignments`), migration
`AddInventoryModule`, 17 endpoints sous `/api/v1/inventory`, deux documents QuestPDF.

**Ce qui est livré :** entités, migration + policies RLS, handlers CQRS, contrôleur, fiche
d'inventaire global (A4 paysage) et fiche de décharge (A5 paysage), tests unitaires et d'intégration,
`openapi.yaml`, Volume 3 §5.9, Volume 4 §21, Volume 7 §15, et l'écran `/inventaire` (Razor + Tailwind +
Alpine, `Views/Inventory/Index.cshtml` + `wwwroot/js/inventory.js`) : 4 onglets (Catalogue, Catégories,
Mouvements de stock, Prêts & Décharges), navigation ajoutée sous « Gestion Scolaire ». Compilation
Razor vérifiée (0 erreur) ; non cliqué dans un navigateur réel faute de base PostgreSQL disponible
dans cet environnement — à valider en local avant mise en production.

**Quatre arbitrages actés, à ne pas rouvrir sans raison :**

1. **Un bien est un LOT, pas une unité.** Suivi à l'unité = lot de quantité 1. Une table
   `inventory_item_units` reste la porte de sortie si un état par unité devient nécessaire ; elle
   n'est pas construite.
2. **`Condition` est l'état dominant du lot.** Le détail par état s'obtient en scindant en deux lots.
3. **Module gratuit** — aucun membre ajouté à `Feature`, accessible à toutes les formules.
4. **`Code` en saisie libre et facultative**, jamais généré : les écoles publiques réutilisent le
   numéro d'immatriculation posé sur le bien par la mairie ou l'État.

Export `.xlsx` de l'inventaire : **écarté pour ce lot** (PDF seul), à arbitrer si le besoin remonte.

### Module Cahier de texte / Journal de classe (19/09/2026) — livré

Ticket JGK-P04 (Module P). Une entrée de journal par séance réellement tenue (classe, matière, date,
sujet, contenu, devoirs éventuels), saisie par l'Enseignant qui l'a effectivement assurée —
traçabilité pédagogique pour le contrôle administratif et la continuité en cas de remplacement.
Table `class_journal_entries` (migration `AddClassJournal`), `ClassJournalController`
(`/api/v1/class-journal` — liste paginée/filtrable, création, correction, archivage), écran
`/cahier-de-texte` (filtres classe/matière/période, modales création/détail/édition/suppression).
Rôles : écriture Enseignant seul (sa propre séance) ; lecture Directeur, Secrétariat, Surveillant,
Enseignant — document pédagogique partagé, pas un carnet privé.

**Deux arbitrages actés, à ne pas rouvrir sans raison :**

1. **La garde d'écriture combine DEUX signaux, jamais un seul.** `ScheduleSlot` (créneau
   hebdomadaire récurrent : jour de la semaine + horaire) ne porte aucune `SchoolYearId` — un
   contrôle isolé matcherait donc aussi un créneau d'une année scolaire révolue. `ClassJournalScopeAuthorizer`
   vérifie donc `TeacherAssignment` (l'enseignant enseigne bien cette classe/matière CETTE année)
   **et** `ScheduleSlot` (un créneau existe bien ce jour-là), refus 409 (`SCHEDULE_SLOT_NOT_PLANNED`)
   si l'un des deux manque — jamais 403, qui est réservé au refus de rôle/propriété.
2. **La règle des 15 jours (`ClassJournalEditWindow`) est symétrique Update/Delete**, avec une
   version pure (`CanCorrect`, sans exception) réutilisée par `GetClassJournalQueryHandler` pour
   poser `canEdit` sur chaque ligne de la liste (confort d'affichage côté écran — masquer
   Modifier/Supprimer — la vraie garde restant les Handlers d'écriture, 403 sinon). Passé le délai,
   seuls Directeur et Secrétariat corrigent, et `UpdateClassJournalEntryCommand` porte
   `IAuditableRequest` : la correction est toujours historisée, qu'elle vienne de l'auteur ou d'un
   rôle élevé (même esprit que la règle #4 Finance).

**Vérifié** : `dotnet build` 0 erreur ; 18 tests unitaires (validateurs + `ClassJournalEditWindow`,
logique pure) ; 14 tests d'intégration sur PostgreSQL réel (Testcontainers) — portée d'écriture
(succès, 409 sans affectation, 409 sans créneau ce jour-là, 403 rôle non concerné, 403 fiche
enseignant manquante), fenêtre des 15 jours (auteur dans le délai, auteur hors délai, Directeur hors
délai, collègue jamais), et isolation RLS dédiée (`class_journal_entries` ajoutée à `TenantTables`,
lecture/écriture croisée refusées, `DELETE` refusé par privilège — append via soft delete
uniquement, règle #6).

### Module Internat (18/09/2026) — livré

Régime d'hébergement (Externe / Demi-pensionnaire / Interne) et affectation de chambre, module
désactivable par école (`SchoolSettings.IsInternatEnabled`, **désactivé par défaut** — à l'inverse de
Pédagogie/Finance). Migration `AddInternatBoarding` (`Enrollment.BoardingStatus`/`RoomId`,
`RoomType.Dortoir`, `FeeCategory.IsBoardingFee`), `InternatController` (`/api/v1/internat` — tableau
de bord, recherche d'élève, affectation), écran `/internat` (tableau de bord par chambre + modale
d'affectation rapide : recherche, transfert, libération), badge d'hébergement sur la fiche élève,
section « Régime & Hébergement » sur le formulaire d'inscription. Rôles : Directeur, Secrétariat,
Surveillant (même trio que `ParentSummonsController`).

**Cinq arbitrages actés, à ne pas rouvrir sans raison :**

1. **Aucune entité `Bed` distincte.** L'hébergement réutilise `Building`/`Room` (module
   Infrastructures) avec `RoomType.Dortoir` ; la capacité se compte au niveau de la chambre
   (`Room.Capacity` vs occupants actifs), jamais lit par lit.
2. **`BoardingStatus` est porté par `Enrollment`, pas par `Student`** — portée ANNUELLE, comme
   `IsRepeating` : une réinscription reconfirme ou change le régime, jamais un report automatique
   d'une année sur l'autre. `RoomId` n'est significatif que si `BoardingStatus != Externe`.
3. **Comptage de capacité simple** : occupants actifs (inscription non annulée, année active) vs
   `Room.Capacity`. Pas de distinction de lit ni de réservation anticipée.
4. **La pension (`FeeCategory.IsBoardingFee`) n'est jamais retirée à la libération.** Sortir un élève
   de l'Internat (retour à Externe, ou changement de chambre) ne retranche pas la ligne de pension
   déjà posée sur son compte financier — une correction suit la règle Finance habituelle
   (AGENTS.md règle #4 : jamais un retrait silencieux, toujours une correction tracée par
   Secrétariat/Admin si le montant facturé doit changer).
5. **Confort d'affichage, jamais une mesure de sécurité** : le masquage du menu/des sections quand le
   module est désactivé (`internatEnabled` côté client) n'est qu'ergonomique — la garde réelle est
   `[RequireModule(SchoolModule.Internat)]` côté serveur (403 `MODULE_DISABLED`), et
   `CreateEnrollmentCommandHandler`/`ChangeBoardingAssignmentCommandHandler` revérifient
   indépendamment (422 si le module n'est pas activé).

**Vérifié de bout en bout (18/09/2026, `dotnet build` 0 erreur + parcours réel sur PostgreSQL, skill
`run`)** : activation du module → création d'un pavillon + deux dortoirs → inscription d'un élève en
régime Interne avec pension (reçu : ligne « Pension » correcte) → transfert de chambre via la modale
d'affectation (occupation reflétée sur le tableau de bord, xmin renouvelé) → libération (retour à
Externe) → **ligne de pension toujours présente** sur le compte financier après libération (vérifié en
base) → refus 422 explicite d'une écriture Interne/chambre quand le module est désactivé pour l'école.
Données de vérification créées sur une école jetable dédiée, entièrement nettoyées après coup (base de
développement partagée).

### Intégration étatique / Passerelle SIMEN (30/08/2026) — livré (back-end + écrans)

Module M (`docs/Volume_1_Cahier_des_Charges.md` §23, `docs/Volume_3_DDS.md` §5.11,
`docs/Volume_4_API_Design.md` §23, tickets `JGK-M01`..`M07`). Le socle producteur des **pièces et
fichiers réglementaires** dus au ministère de l'Éducation. Migrations `AddStateIntegrationModule` +
`FixMutationCertificateReasonDefault` (colonnes sur `students`/`schools`/`teachers`/`exam_dossiers` +
table `student_mutation_certificates` + policy RLS + fonction
`SECURITY DEFINER verify_mutation_certificate`).

**Cadre non négociable, rappelé dans l'aide en ligne, le contrôleur ET l'écran :** le SIMEN n'expose
**aucune API publique** à ce jour. Le module produit des fichiers que l'école transmet par la voie
habituelle. `ISimenBridgeService` est un contrat seul ; l'implémentation livrée
(`UnavailableSimenBridgeService`) refuse explicitement chaque appel — aucune réussite simulée, jamais
d'état « Transmis » posé par une route client (règle #11). L'écran `/integration-etatique` affiche un
bandeau **« Statut du relais SIMEN »** qui montre l'indisponibilité plutôt qu'une action de
transmission.

**Ce qui est livré :** entités, migrations + RLS, handlers CQRS, `StateIntegrationController`
(11 routes), `PlaneteExportSerializer` (CSV/JSON, une seule définition de colonnes), documents QuestPDF
(`StateducReportDocument` A4 paysage, `StudentMutationCertificateDocument` avec QR,
`SkillsBookletDocument` multi-pages), `StateducReportExcelGenerator`, `openapi.yaml`, Volumes 1/3/4,
Centre d'aide §11.

**Écrans (Razor + Tailwind + Alpine) :**
- `/integration-etatique` (`Views/StateIntegration/Index.cshtml` + `wwwroot/js/state-integration.js`) —
  3 onglets : **Export Planète** (année + périmètre + format CSV/JSON → téléchargement, message
  actionnable si le code établissement manque), **Rapport STATEDUC** (année + date d'observation →
  agrégats à l'écran avec encadré « Données incomplètes » + téléchargement PDF/Excel), **Certificats
  de mutation** (registre paginé + révocation avec motif obligatoire). Réservé Directeur ; le
  Secrétariat n'a que l'onglet Certificats. Entrée de menu sous « Gestion Scolaire ».
- **Fiche élève** (`Views/Students/Index.cshtml` + `students.js`) — ligne « IEN » dans le cartouche
  d'identité (numéro provisoire distingué visuellement) + modale « Gérer l'IEN » : saisie du numéro
  officiel, ou bouton « Générer un numéro provisoire ».
- **Page publique** `/verifier/mutation/{token}` (`Views/StateIntegration/VerifyMutation.cshtml` +
  `verify-mutation.js`, layout `_LayoutPublic`) — scan du QR par l'école d'accueil : statut
  (authentique / révoqué / introuvable) + numéro, date, établissement émetteur. **Aucune donnée
  d'élève.** `unknown` renvoyé en HTTP 200, jamais 404.

**Vérifié au navigateur / `curl` sur PostgreSQL réel (skill `run`, 30/08/2026)** : login Directeur →
IEN officiel + provisoire (`P` + code + millésime + séquence + clé Luhn) → export Planète CSV (BOM,
`;`, cellules vides pour l'absent) → STATEDUC JSON/PDF/Excel → délivrance d'un certificat
(`MUT-2025-0001`, PDF + QR) → révocation (motif obligatoire, 2ᵉ tentative en 409) → vérification
publique anonyme du certificat révoqué. `dotnet build` **0 erreur** ; `SimenComplianceTests`
**26/26** ; `RlsCoverageTests` **vert** ; suite unitaire **1004/1004** ; intégration ciblée
(Students, ReportCards, RLS) **vert**.

**Écart trouvé et corrigé pendant cette vérification :** `StudentMutationCertificate.Reason` portait
un `HasDefaultValue(Autre)`. La valeur CLR par défaut de l'enum (`Demenagement` = 0) étant aussi celle
qu'EF Core lit comme « propriété non affectée », une mutation réellement pour « Déménagement » aurait
été enregistrée « Autre » **en silence**. Défaut base retiré (`FixMutationCertificateReasonDefault`,
additive et réversible) ; le Handler fournit toujours `Reason`. Par ailleurs, la génération d'un IEN
provisoire sans code établissement renvoyait un 500 (`InvalidOperationException` de
`NationalIenGenerator`) — désormais un 409 actionnable, même message que le refus de l'export Planète.

**JGK-M05 câblé (30/08/2026)** : *Paramètres → Établissement* porte un bloc « Intégration étatique
(SIMEN) » (code établissement national, n° d'autorisation, code de circonscription, coordonnées GPS —
les deux ensemble ou aucune) ; la *fiche enseignant* (création + modification) porte un bloc replié
« Informations pour le rapport STATEDUC » (genre, diplômes académique et professionnel, statut
administratif, matricule de solde, date de première prise de service). `UpdateCurrentSchoolCommand` /
`SchoolProfileDto` / `CreateTeacherCommand` / `UpdateTeacherCommand` / `TeacherProfileDto` étendus.
Vérifié : `GET /state-integration/planete/export` passe de 409 à 200 dès que le code établissement est
posé.

**Écrans complétés (30/08/2026)** : l'écran `/integration-etatique` ne se contentait que de lister et
révoquer les certificats — aucun bouton ne permettait de délivrer un certificat (`POST
.../mutation-certificate`, livré côté API depuis JGK-M06 mais orphelin côté écran) ni de télécharger le
livret de compétences (`GET .../skills-booklet`, JGK-M07, jusque-là inaccessible en dehors de l'aide en
ligne). L'onglet Certificats porte maintenant deux modales : « Délivrer un certificat » (recherche
élève par nom/matricule — même widget que `/caisse` — puis motif, destination, téléchargement immédiat
du PDF, et proposition du livret pour le même élève/année une fois délivré) et « Livret de compétences »
(téléchargement autonome, hors mutation). Recherche élève NON rattachée à la fiche élève : ce choix
évite de dépendre d'un écran distinct pour ce dernier kilomètre. `state-integration.js` / `Views/State
Integration/Index.cshtml` étendus, aucun autre écran touché.

**Tests d'intégration ajoutés (30/08/2026)** — comblent le manque ci-dessus :
`tests/SamaEcole.IntegrationTests/StateIntegration/` (`IenProvisionalSequenceTests`,
`StateIntegrationRefusalTests`, `GetStateducReportQueryHandlerTests`), 13 tests, vérifiés contre
PostgreSQL réel (Testcontainers, rôle applicatif) : unicité de la séquence IEN sous 20 générations
concurrentes, rembobinage sur rollback, séquence par école, refus 409 (export Planète et IEN
provisoire) sans code établissement, et agrégats STATEDUC (périmètre = inscriptions non annulées, âge
à la date d'observation, enseignant archivé/sans genre non imputé).

**Carte scolaire & état civil du dossier d'examen — livré (30/08/2026, commits `86e4398` +
`729ce9d`).** Les trois champs « carte scolaire & état civil » du dossier d'examen
(`ExamDossier.ExamCenterCode`, `TableNumber`, `CivilRegistryDocumentStatus` — Volume 1 §23.4), jusque-là
présents en base et dans la configuration EF sans aucun câblage applicatif, sont maintenant saisissables
de bout en bout. `AssignExamCenterCommand` porte le code du centre et le numéro de table
(préserve-si-vide, jamais générés ni effacés par un envoi à blanc — même contrat qu'`ExamCenterName`) ;
`UpdateExamDossierCommand` porte `CivilRegistryDocumentStatus` en NULLABLE (`null` = préserve la valeur
en base, ne la ramène jamais à `NonFourni` en silence) — seul point d'écriture d'`EnRegularisation`,
désormais réellement sélectionnable. Le couple booléen existant (`BirthCertificatePresent` /
`CivilStatusConforming`) reste la source du statut Incomplet/Complet du dossier ; l'enum ne fait que le
COMPLÉTER. Lecture (`GetExamDossierDetail`, `GetExamDossiers`), `ExamsController`, `openapi.yaml` et
tests (Exams unitaires 34/34, intégration 33/33) à jour. Écrans : modale « Attribuer un centre » (code +
numéro de table), modale « Modifier le dossier » (select à 5 états), fiche détaillée (affichage des 3
champs + ligne « régularisation en cours » dans l'encadré pièces manquantes).

**JGK-T01 (30/08/2026) — corrigé** : retirer une matière à un enseignant échouait en 500
(`42501: permission denied for table teacher_subjects`). `UpdateTeacherCommandHandler` fait un
`DELETE` physique ; `AddTeachers` n'accordait que `SELECT, INSERT, UPDATE`. Micro-migration
`GrantDeleteOnTeacherSubjects` (Option A du ticket) : `GRANT DELETE` sur cette table de LIAISON —
« qualifié pour cette matière » n'a aucune valeur d'audit, et le `DELETE` est déjà accordé à des
tables de même nature (`refresh_tokens`, `classrooms`, paie, décaissements). Les tables à donnée
métier historisée restent sans `DELETE`. Test `TeacherSubjectUnassignmentTests` (rôle bridé + contrôle
SQL brut de la disparition physique).

**Trois arbitrages actés, validés par le client :**

1. **`Teacher.Gender` ajouté (nullable).** Le formulaire STATEDUC ventile tout le personnel en
   Hommes/Femmes ; les fiches enseignant existantes ne portent pas le genre. Une **troisième colonne
   « genre non saisi »** (absente du formulaire officiel) est ajoutée plutôt que d'imputer d'office à
   l'une des deux colonnes (faux et indétectable) ou de déduire le genre du prénom (faux en silence).
2. **Coordonnées GPS en deux `numeric(9,6)`** (latitude, longitude), jamais une chaîne « lat,lon ».
   `School.GpsCoordinates` est une propriété **calculée, non mappée** (`builder.Ignore`).
3. **IEN provisoire de secours préfixé `P`.** `NationalIenGenerator` produit des numéros
   `P` + code établissement + millésime + séquence + clé Luhn, marqués `Student.IsIenProvisional` et
   signalés partout. Le format national réel n'est **pas** imité — deux numéros indiscernables
   seraient le pire résultat. L'arrivée d'un IEN officiel écrase le provisoire ; l'inverse est refusé.

### Examens officiels (26/08/2026) — écran livré, backend déjà complet

Écran `/examens` (`Views/Exams/Index.cshtml` + `wwwroot/js/exams.js`) : 4 onglets (Sessions, Dossiers,
Audit, Statistiques). Le backend (routes, migration RLS, PDF, export) couvrait déjà l'intégralité du
backlog Module J (JGK-J01 à J08) ; seul l'écran manquait. Un `Enseignant` charge l'onglet Dossiers en
lecture seule (liste + fiche détaillée), borné par le serveur à ses classes assignées (JGK-J08) —
tous les autres onglets et toutes les actions d'écriture restent Directeur/Secrétariat.

### Classes passerelles / accélérées (04/08/2026) — option désactivée par défaut

Une classe peut déclarer qu'elle valide **deux niveaux** en une année scolaire (« CI-CP », « 6e-5e »),
pour les parcours d'intégration des élèves venus des écoles coraniques. `Classroom.IsAccelerated`
(faux par défaut) + `Classroom.TargetLevel` (migration `AddAcceleratedClassrooms`, strictement additive).

- **Pas de table de niveaux, et c'est délibéré.** `Classroom.Level` reste le CYCLE en texte libre ; le
  niveau réel vit dans le nom de la classe. `TargetLevel` est donc un **libellé** (« CP », « Cinquième »),
  pas un identifiant — introduire une énumération de niveaux est explicitement interdit (AGENTS.md,
  `Classroom.Level`). La nomenclature par cycle et la reconnaissance du niveau depuis le nom vivent dans
  `ClassroomGradeLevels`, seule source pour la liste déroulante de l'écran **et** pour la délibération.
- **Délibération** : `ClassroomPromotion.ValidatedLevels` — un élève **admis** en classe passerelle valide
  le niveau courant ET le niveau cible ; une classe ordinaire valide un seul niveau, comme avant. Aucune
  décision (ou un redoublement/exclusion) ne valide rien.
- **Documents** : mention « Cursus Accéléré Passerelle » sur le reçu d'inscription, le reçu de caisse, le
  bulletin et le PV de délibération. Les documents d'une classe ordinaire sont inchangés au caractère près.

### Structure d'évaluation modulable / grilles APC (22/08/2026) — option désactivée par défaut

Le Directeur configure lui-même la grille d'évaluation d'un niveau, et le bulletin PDF s'y adapte : plus
aucun tableau de matières codé en dur. Migration `AddHierarchicalEvaluationStructure`, strictement additive
(cinq colonnes nullables ou à défaut neutre sur `subjects`).

- **Deux niveaux, pas plus.** `Subject.ParentSubjectId` : un **domaine** (« Lang & Com. », « Français »)
  porte des **activités** (« P. Alphabétique », « Ressources », « Compétences »). Le bulletin n'imprime
  qu'une colonne de regroupement, dont le `RowSpan` se calcule sur le nombre d'activités — une troisième
  profondeur n'aurait aucune colonne où s'afficher, `SubjectHierarchyGuard` la refuse en 422.
- **`Subject.MaxScore` NULL veut dire « suis le barème du cycle »**, pas « pas de barème ». C'est ce repli
  qui garantit qu'aucune donnée existante ne change : une valeur par défaut fixe à 20 aurait discrètement
  relevé le plafond de tout le primaire, resté à /10. Résolution unique : `GradeCalculator.EffectiveMaxScore`,
  utilisée par la saisie, la correction, l'import Excel, la feuille de notes et le bulletin.
- **Les lignes de barèmes différents sont ramenées au barème du bulletin avant d'être moyennées**
  (`GradeCalculator.Rebase`) : déclarer « Compétences /60 » n'est pas déclarer un poids. Le poids reste le
  coefficient. L'appréciation d'une ligne se décide, elle, sur son **pourcentage** de réussite — une seule
  échelle de mentions pour tous les « Sur ».
- **Le bulletin imprime la grille ENTIÈRE**, cases vides comprises (`EvaluationStructureBuilder`), comme les
  modèles officiels — là où le tableau du secondaire ne liste que les matières notées. Le niveau qui ne
  déclare aucune hiérarchie retombe sur les tableaux d'origine, inchangés.
- **Écran** : `/matieres`, onglet « Structure d'évaluation » — domaines, activités, barème par ligne,
  réorganisation par flèches, entêtes des deux premières colonnes du bulletin.
- **Index unique** : passé à (SchoolId, Level, **ParentSubjectId**, Name, IsDeleted) avec `NULLS NOT DISTINCT`
  (PostgreSQL 15+). Sans cette clause, ajouter une colonne nullable à la clé aurait fait cesser à l'index
  d'interdire deux matières de même nom au même niveau. Les deux moitiés de la règle sont testées contre un
  vrai PostgreSQL (`SubjectStructureEndpointsTests`).

### Présences & Discipline — bilan actionnable et suite des convocations (02/09/2026)

Les deux modules étaient livrés depuis des sprints, **et sans aucun lien entre eux**. Le bilan
d'assiduité comptait les retards et les absences ; convoquer un parent obligeait à ouvrir un autre
écran et à ressaisir ces chiffres de mémoire. Symétriquement, une convocation n'avait ni statut ni
compte rendu : le registre listait des rendez-vous, jamais des entretiens tenus.

- **Convoquer depuis le bilan.** `/reports/attendance` porte une colonne « Action » : sur toute ligne
  présentant au moins un retard ou une absence, « Convoquer » ouvre une convocation dont le **motif
  est pré-rempli** avec les chiffres réellement comptés sur la période affichée, et reste modifiable.
  L'avis PDF s'imprime dans la foulée, sans passer par `/convocations`.
- **Aucun seuil automatique.** Le bilan propose, le Directeur décide (Volume 1 §18.2). L'option d'un
  seuil paramétrable avec génération par lot — sur le modèle de `GenerateDebtorReminderBatches` — a
  été écartée à l'arbitrage du 02/09/2026 ; la porte reste ouverte, rien n'a été construit dans ce sens.
- **Colonne masquée au Secrétariat.** Le rapport lui est ouvert, `ParentSummonsController` ne l'est pas
  (SuperAdmin, Directeur, Surveillant) : l'action n'apparaît qu'à l'intersection des deux rôles.
- **Suite de l'entretien** (Volume 1 §18.3, migration `AddParentSummonsOutcome`) : `Status`,
  `OutcomeNotes`, `ClosedAt`, `ClosedByUserId` sur `ParentSummons`, plus
  `PATCH /parent-summons/{id}/outcome`. Trois invariants, figés par `ParentSummonsIsolationTests` :
  la suite se pose **une seule fois** et depuis `Scheduled` seulement (sinon 422, jamais d'écrasement) ;
  un **compte rendu est exigé** pour « non honorée » et « reportée », facultatif pour « honorée » ;
  les convocations **sans suite passent en tête** du registre, quelle que soit leur date.
- **L'avis PDF n'a pas changé** — et ne doit pas changer : c'est la pièce remise **avant** l'entretien,
  elle ne peut pas porter une suite qui n'existe pas encore au moment de son impression.
- **Migration : `defaultValue` repris à la main.** L'échafaudage EF proposait la chaîne vide pour
  `Status`, qui n'est pas un `ParentSummonsStatus` : toute convocation antérieure se serait relue en
  `""` et aurait fait lever la conversion enum ↔ texte à la première lecture du registre. Corrigé en
  `'Scheduled'` avant application.

### Zone de danger — réinitialisation des données d'essai (22/08/2026)

Le Directeur teste l'application avec des données fictives, puis remet son établissement à neuf depuis
l'onglet **Paramètres → Configuration**, en bas d'écran. `POST /schools/current/reset-data`, migration
`AddSchoolDataReset`.

- **Seule exception du produit à la règle #6** (aucune suppression physique). Elle est bornée : Directeur
  uniquement, sur SON école, après saisie de `PURGER` ou du nom de l'établissement, et journalisée —
  `ResetSchoolDataCommand` est `IAuditableRequest`, et `audit_logs` n'est pas purgé.
- **Pourquoi une fonction PostgreSQL et non un `ExecuteDelete` EF Core.** Le rôle applicatif n'a
  volontairement aucun droit de `DELETE` sur les tables métier — chaque migration lui accorde
  « SELECT, INSERT, UPDATE, jamais DELETE », de sorte que la règle #6 est tenue par la base et pas
  seulement par le code. Lui accorder le `DELETE` aurait affaibli cette garantie pour tout le produit,
  définitivement, au bénéfice d'un seul écran. `reset_school_data` est donc `SECURITY DEFINER` : elle
  ne donne pas un droit, elle donne UNE opération, dont la liste des tables et l'ordre des clés
  étrangères vivent dans la base.
- **Un `SECURITY DEFINER` est exempté de RLS** (droits du propriétaire). La fonction reconstitue donc
  elle-même la barrière : elle refuse d'agir si `p_school_id` diffère de `app.current_school_id`, ou si
  la session n'a aucun tenant. Les deux refus sont testés (`ResetSchoolDataTests`, catégorie
  `MultiTenant`) — c'est la seule opération du produit où l'isolation ne repose pas sur les policies.
- **Périmètre élargi le 15/09/2026** (migration `ExtendResetSchoolDataToConfiguration`) : la purge
  emporte désormais la CONFIGURATION métier, pas seulement la saisie.
  **Conservé** : fiche et réglages de l'école (formats, signatures, SMS), **comptes Directeur**,
  abonnement, années scolaires et trimestres, mentions, bâtiments et salles, journal d'audit.
  **Effacé** : élèves et tout ce qui pend à eux (inscriptions, échéanciers, notes, appréciations,
  appels, discipline, convocations, SMS), paiements, caisse, décaissements, engagements, examens,
  certificats de mutation — plus les **compteurs de matricules** (sans quoi le premier élève recréé
  porterait `ELEV-2026-0043`) — et, depuis le 15/09 : **classes** (le niveau n'est qu'une colonne),
  **matières** (y compris les sous-matières : `ParentSubjectId` est mis à NULL avant la boucle, sa FK
  auto-référencée étant `RESTRICT`, non différable), **enseignants**, affectations, matières
  enseignées, **emploi du temps**, pointages, **contrats et fiches de paie**, **déclarations fiscales
  (TVA)**, **barème des frais** (catégories, frais par classe, historique) et **inventaire**
  (catégories, biens, `stock_movements` — append-only pour le rôle applicatif : la fonction
  propriétaire est la seule voie qui l'efface).
- **Comptes du personnel : supprimés physiquement** (arbitrage du 15/09/2026), avec trois réserves
  qui sont autant d'invariants testés :
  1. le **journal d'audit n'est pas purgé** — ses entrées sont **détachées** (`audit_logs.UserId` →
     NULL, colonne rendue nullable par la même migration ; `get_global_audit_logs` passe en LEFT JOIN,
     sinon ces entrées disparaîtraient du journal Super Admin). L'écran affiche « Compte supprimé » ;
  2. un compte rattaché **aussi à un autre établissement** (`user_schools`, groupe scolaire) n'est
     **jamais** supprimé : purger une école ne doit rien retirer à une autre, qui peut être en mode
     réel. Seul son rattachement à l'école purgée part ;
  3. le Directeur (et tout Super Admin) est épargné — il doit pouvoir se reconnecter.
- **Les fichiers déjà téléversés ne sont pas supprimés** (photos d'élèves sous `wwwroot/uploads/`) : la
  purge est transactionnelle en base, un effacement disque ne l'est pas et laisserait, en cas d'échec,
  une incohérence pire que quelques fichiers orphelins devenus inatteignables.
- **Le bouton « Repasser en mode test » reste AFFICHÉ en mode réel** (15/09/2026), et non plus masqué
  hors des environnements jetables : un bouton absent laisse croire que la fonction n'existe pas, là où
  la vraie raison est l'environnement. Il est **désactivé** quand `revertToTestAvailable` est faux, avec
  la phrase qui l'explique. Rien n'a changé côté serveur : la route `/dev/revert-to-test` n'est toujours
  montée que sur les environnements jetables, et le garde de démarrage refuse toujours
  `SAMA_RETOUR_MODE_TEST_AUTORISE=true` en Production. La pastille de la barre supérieure, elle, affiche
  désormais les **deux** régimes (« Mode test » / « Mode réel ») et mène, pour le Directeur, à
  Paramètres › Sécurité.
- **Refonte du verrou (19/09/2026) : mode et verrouillage définitif sont désormais deux décisions
  INDÉPENDANTES du Directeur** (migration `DecoupleProductionLockFromLiveMode`), sur arbitrage produit
  explicite — abandon de l'ancien comportement ci-dessus.
  - Le passage test ↔ réel (`GoLiveCommand` / `RevertToTestCommand`) est désormais **pleinement
    réversible** et **ne pose plus aucun verrou par effet de bord**. En mode test, la purge reste
    disponible **sans limite de rejeu**, y compris après un ou plusieurs allers-retours en mode réel.
  - `School.HasEverGoneLive` est **renommée** `IsProductionLocked` (+ `ProductionLockedAt`, horodatage) :
    même colonne, mêmes invariants de garde (Handler C# **et** fonction PostgreSQL
    `reset_school_data`, défense en profondeur inchangée), mais posée **UNIQUEMENT** par une nouvelle
    action manuelle et explicite : `POST /schools/current/lock-production` (« Verrouiller
    définitivement l'établissement », confirmation `VERROUILLER` ou nom de l'école, Directeur seul, non
    rejouable — 409 `ALREADY_LOCKED` au second appel). Aucune commande ne défait ce verrou une fois posé.
  - `reset-data` distingue maintenant deux refus à codes différents : 409
    `RESET_UNAVAILABLE_LIVE_MODE` (mode réel COURANT — réversible, un retour en mode test rouvre la
    purge) et 409 `RESET_UNAVAILABLE_PRODUCTION_LOCKED` (verrou définitif — irréversible, quel que soit
    le mode ultérieur).
  - **Backfill de migration, arbitrage EXPLICITE et délibéré** : `IsProductionLocked` est forcé à
    `false` pour **toutes** les écoles existantes, y compris celles déjà passées en mode réel avant
    cette migration (donc déjà verrouillées sous l'ancien régime automatique). Elles retrouvent la
    « Zone de danger » tant qu'un Directeur ne clique pas explicitement sur le nouveau bouton — décision
    du produit pour rendre la main aux écoles déjà en exploitation plutôt que de reconduire un verrou
    qu'elles n'ont jamais posé elles-mêmes. Toute école qui a besoin de la protection doit désormais
    verrouiller manuellement.

### Suppression d'une année scolaire (15/09/2026)

`DELETE /api/v1/school-years/{id}`, Directeur seul, confirmé par la **recopie du libellé exact** de
l'année (« 2025-2026 ») — et non un mot-clé générique, qui se taperait de mémoire sur la mauvaise ligne
du tableau. Écran : Paramètres › Années scolaires.

- **Deux régimes, et c'est le cœur de l'arbitrage.** En **mode test**, l'année et tout ce qu'elle porte
  sont effacés par la fonction `delete_school_year` (migration `AddSchoolYearDeletion`, mêmes gardes que
  `reset_school_data` : tenant, appartenance de l'année, mode test). En **mode réel**, la suppression
  physique est refusée : le Handler exige une année **VIDE** (aucune inscription, note, appréciation,
  fiche d'appel, session d'examen ni certificat de mutation — lignes en suppression logique comprises)
  et se contente alors d'une **suppression logique** de l'année, de ses trimestres et de ses
  affectations d'enseignants. Une année qui a servi est refusée en **409 `SCHOOL_YEAR_HAS_DATA`**, avec
  le détail de ce qui bloque : ses inscriptions et paiements sont la contrepartie de reçus déjà remis
  aux familles, que la règle #6 déclare inaltérables. L'export ZIP reste la voie d'archivage.
- **Les élèves survivent** à la suppression d'une année : ils appartiennent à l'établissement, pas à un
  exercice — seule leur inscription à cette année-là disparaît. Les sessions de caisse non plus ne sont
  pas touchées : elles couvrent une journée, pas une année.
- **Rebascule de l'année active.** Si l'année supprimée était l'active, l'établissement repart sur
  l'année **précédente** encore ouverte, à défaut la suivante. Jamais sur une année **terminée** :
  `ActivateSchoolYearCommandHandler` l'interdit déjà (les écritures du jour s'imputeraient sur un
  exercice clos), et cette suppression ne crée pas d'exception à cette règle. Quand il ne reste que des
  années terminées, la réponse porte `requiresActiveYearSelection` et l'écran demande d'en activer ou
  d'en créer une — c'est la seconde branche demandée à l'arbitrage.

---

## 3. Points de conformité traités (sprint du 27/07/2026)

1. **`Subscription` implémente `ITenantEntity`.** La table `subscriptions` figurait dans les
   `TenantTables` de la migration `EnableRowLevelSecurity` — la policy RLS était donc en place, mais
   le Global Query Filter EF Core manquait. Les **deux** protections exigées par la règle #2 sont
   désormais actives. Aucune migration : le changement est purement applicatif.
2. **Export financier `.xlsx` câblé à l'IHM.** L'endpoint `GET /finance/reports/revenue/excel`
   existait sans consommateur ; il est servi par le nouvel écran `/rapports/financiers`.
3. **Créneaux d'emploi du temps — création, modification ET suppression.** Un Enseignant ne peut plus
   agir sur le créneau d'un collègue. Le contrôle est porté par `ScheduleOwnershipAuthorizer` (même
   idiome qu'`AttendanceScopeAuthorizer`), partagé par les trois handlers : le `TeacherId` reçu est
   rapproché de sa propre fiche via `Teacher.UserId` (règle #10).
   La modification contrôle **deux** choses — le propriétaire actuel du créneau *et* le propriétaire
   demandé — sinon le verrou de la création se contournait en deux appels (créer pour soi, puis
   réattribuer à un collègue). La suppression est couverte pour la même raison : interdire la
   modification en laissant supprimer ne protégeait rien.
4. **Vue orpheline supprimée.** `Views/Absences/BilletPrint.cshtml` (« Page en construction ») et sa
   route `/billet-print` : les billets d'entrée et de sortie sont des PDF A5 générés côté serveur.

---

## 4. Notifications sortantes — file d'attente et accusés de réception (27/07/2026)

Le canal SMS existait mais envoyait **en ligne**, dans la requête ou l'événement métier. Il passe
désormais par une file. Spécification : `docs/Volume_4_API_Design.md` §20.

- **Mise en file plutôt qu'envoi direct.** `SmsDispatcher` conserve ses quatre gardes (formule,
  commutateur d'école, débit atomique du solde, historisation) mais inscrit le message en `Pending`
  au lieu d'appeler l'agrégateur. `SmsQueueHostedService` le remet hors requête, avec report
  exponentiel plafonné et abandon après `MaxAttempts` — l'abandon **recrédite** le solde.
- **Statuts complets.** `Pending` → `Sent` → `Delivered` ou `Failed`. « Envoyé » (accepté par
  l'agrégateur) et « Livré » (confirmé par accusé de réception) sont volontairement distincts : seul
  le second atteste qu'un parent a été prévenu.
- **Webhook DLR signé** — `POST /api/v1/webhooks/sms/{provider}`, public, HMAC-SHA256 du corps brut.
  Fermé par défaut : sans `Sms__WebhookSecret`, tout est rejeté en 401.
- **Le worker n'a aucun tenant** et ne peut donc rien voir sous RLS. Il passe par trois fonctions
  `SECURITY DEFINER` au périmètre étroit (migration `AddSmsQueue`), **jamais** par le rôle
  propriétaire — même idiome que `SchoolProvisioningStore` (règle #2). `SmsQueueTests` fige cette
  contrainte : un `DbContext` sans tenant voit zéro ligne, la fonction voit la file.
- **Nouveau déclencheur** : mise à disposition d'un bulletin (`SmsTrigger.ReportCard`) — un SMS
  d'avis en plus du PDF envoyé par WhatsApp/e-mail, un SMS ne transportant pas de pièce jointe.
- **Fournisseur WhatsApp réel** (`HttpWhatsAppSender`, API Cloud de Meta) en remplacement du repli
  qui se contentait de journaliser en production. Configuration absente ⇒ pas d'échec au démarrage,
  mais un avertissement explicite — `SmsServiceGuard`, jusqu'ici jamais appelé, l'est désormais.

---

## 5. Session de caisse câblée à l'écran (27/08/2026) — `/caisse` était inutilisable

`RecordPaymentCommandHandler` refuse (422) tout encaissement hors d'une session de caisse ouverte pour
l'utilisateur courant depuis toujours (Volume 1 §15.1) — mais **aucun fichier JS du dépôt n'appelait
jamais** `POST /finance/sessions/open` ni `POST /finance/sessions/{id}/close`, pourtant présents côté
API. Conséquence concrète : personne n'a jamais pu enregistrer un encaissement via l'écran `/caisse`,
pour aucun établissement, depuis la livraison de JGK-F02. Trouvé en poussant la vérification manuelle
de JGK-L03 (résilience réseau) jusqu'à un vrai paiement plutôt que de s'arrêter à l'affichage de l'écran.

- **Nouvelle requête** `GET /finance/sessions/current` (`GetCurrentCashierSessionQuery`) : la session
  ouverte de l'utilisateur courant (fonds initial, total encaissé, nombre de versements, exclut les
  paiements annulés — même agrégat que la clôture), ou `null` s'il n'en a aucune. C'est ce que l'écran
  interroge à l'ouverture pour savoir s'il doit bloquer l'encaissement ou afficher le statut.
- **Écran `/caisse`** : bandeau d'ouverture (déclaration du fonds initial) tant qu'aucune session n'est
  ouverte — la recherche d'élève et le formulaire d'encaissement restent masqués jusque-là ; bandeau de
  statut en direct une fois ouverte ; modale de clôture avec récapitulatif (fonds initial, encaissé,
  solde théorique) puis accès direct au rapport PDF.
- **Second bug corrigé au passage** : `DailyClosingReportPdfGenerator` ne posait pas
  `QuestPDF.Settings.License = LicenseType.Community` dans son constructeur statique, contrairement aux
  25 autres générateurs du projet — le rapport de clôture renvoyait donc systématiquement une erreur
  500 depuis sa livraison, jamais couvert par un test.
- **Vérifié de bout en bout** en conditions réelles (navigateur + PostgreSQL, skill `run`) : ouverture
  → encaissement avec coupure réseau simulée (JGK-L03) → clôture → téléchargement du rapport PDF.
  4 nouveaux tests d'intégration (`GetCurrentCashierSessionQueryTests.cs`).

### Contrôle du comptage physique et écarts de caisse (29/08/2026) — JGK-F09

La limite notée ci-dessus le 27/08/2026 (« la clôture ne confronte pas encore le numéraire compté au
solde théorique ») est comblée : `ActualCashAmount` est désormais un paramètre REQUIS de
`CloseCashierSessionCommand`, jamais une valeur facultative. L'écart se compare aux ESPÈCES attendues
(fonds initial + encaissements EN ESPÈCES uniquement, migration `AddCashierSessionDiscrepancy`), jamais
au total toutes méthodes — un virement ou un mobile money ne transite jamais par le tiroir-caisse
physique. Un écart non nul (manquant ou surplus) exige un motif avant de clôturer (422 sinon, la session
reste `Open`). Le rapport PDF affiche désormais espèces attendues/comptées/écart/motif. Écran `/caisse` :
comptage obligatoire dans la modale de clôture, motif révélé seulement après un premier refus serveur —
jamais anticipé côté client, qui ne connaît pas les espèces attendues. Vérifié en conditions réelles
(navigateur + PostgreSQL) : montant incorrect → 422 → motif → clôture → rapport à jour. Tests :
`CloseCashierSessionCommandValidatorTests.cs` (5/5), `CashierSessionClosingTests.cs` (6/6, dont la preuve
qu'un paiement non-espèces n'entre jamais dans l'écart).

### Inscription = calcul seul, encaissement = Caisse (30/08/2026)

L'écran `/inscriptions` mélangeait deux métiers : il **calculait** les frais dus **et** encaissait le
versement du jour (cases « ce frais est réglé », mode de règlement, création d'un `Payment` dans la
transaction). Contraire au principe déjà acté par la refonte du reçu du 25/08/2026 (`docs/design-
references/README.md` §1 : « le secrétariat n'encaisse aucun fonds »). Séparation appliquée :

- **`POST /api/v1/enrollments`** n'accepte plus `collectedFees` / `paymentMethod` (retirés de
  `CreateEnrollmentCommand`, du validateur et d'`openapi.yaml`). `CreateEnrollmentCommandHandler` fige
  le dû annuel, laisse `AmountPaid = 0` et ne crée **jamais** de `Payment` — la dépendance
  `ICurrentUserService` (n'y servait qu'à l'auteur du versement) a été retirée du constructeur.
- **Écran `/inscriptions`** : panneau renommé « Frais », barème en **lecture seule** + un simulateur
  de mensualités **purement indicatif** (`simMonths` / `simSubtotal()` dans `enrollments.js`, aucun
  appel réseau). Plus de mode de règlement, plus de « Encaissé ce jour ».
- **Caisse (`/caisse`) inchangée** : elle voit désormais l'intégralité du solde à encaisser et prend
  le premier versement comme les suivants (`RecordPaymentCommand`). Le solde/échéancier ne dépendaient
  déjà pas du `Payment` d'inscription (`InstallmentScheduleCalculator`).
- `EnrollmentReceiptDto` garde `CollectedLines` / `TotalCollected` / `PaymentMethod` (front + Finance)
  mais ils valent toujours vide / 0 / null pour une inscription ; `GetEnrollmentReceiptQuery` les
  renseigne encore à la relecture d'un reçu historique. Tests :
  `EnrollmentTests.A_New_Enrollment_Freezes_The_Debt_But_Records_No_Payment`,
  `EnrollmentsEndpointsTests.An_Enrollment_Never_Records_A_Payment_And_Leaves_The_Whole_Balance_Due`.
