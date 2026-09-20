# Jangalekat — Backlog de tickets (prêt à l'emploi pour agents de code)

Chaque ticket est conçu pour être donné **seul, un par un**, à un agent de code (Claude Code, Cursor, Antigravity). Format : ID (traçable à `Volume_1.5_PRD.md` et à la RTM de `Volume_8_Test_Strategy.md`), description courte, critères d'acceptation testables, dépendances. Priorité selon `Volume_1_Cahier_des_Charges.md` §6 : **C**ritique / **H**aute / **M**oyenne / **F**aible.

> Règle d'usage : ne jamais donner plus de 2-3 tickets à la fois à un agent. Un ticket = une PR = une revue.

> **État du squelette fourni** : `JGK-A01` (solution + projets) est déjà fait. `JGK-D01` (création élève) a un **pattern de référence** implémenté (`Students/Commands/CreateStudent`) mais incomplet — `IMatriculeGenerator` n'a pas d'implémentation réelle, à traiter en même temps que `JGK-A02`/`JGK-A03`. Voir `docs/REPO_STRUCTURE.md` §État du squelette.

---

## Module A — Fondations & Authentification

**JGK-A01** [C] — Squelette de solution Clean Architecture
Créer les 5 projets .NET (`Domain`, `Application`, `Infrastructure`, `Persistence`, `Web`) selon `Volume_6_Dev_Guide.md` §2, avec références correctes entre couches et un `Program.cs` minimal qui démarre.
*Critères* : `dotnet build` réussit ; aucune référence de `Domain` vers une autre couche.

**JGK-A02** [C] — Connexion EF Core + PostgreSQL + première migration
Configurer `DbContext`, connexion via `ConnectionStrings:Default`, provider Npgsql uniquement. Générer la migration initiale pour `Schools`, `Users`, `Subscriptions`.
*Dépend de* : JGK-A01. *Critères* : `dotnet ef database update` crée les tables ; aucun autre provider EF référencé dans le `.csproj`.

**JGK-A03** [C] — Row-Level Security + Global Query Filter multi-tenant
Implémenter la policy RLS PostgreSQL sur `Users` et le Global Query Filter EF Core correspondant, avec injection du `SchoolId` courant via un `ITenantProvider`.
*Dépend de* : JGK-A02. *Critères* : un test d'intégration prouve qu'une requête avec le tenant A ne retourne jamais de ligne du tenant B, même en cas d'oubli du filtre côté C# (RLS seul doit suffire à bloquer).

**JGK-A04** [C] — Authentification JWT (login/refresh/logout)
Endpoints `/auth/login`, `/auth/refresh`, `/auth/logout` conformes à `openapi.yaml`. ASP.NET Core Identity pour le stockage utilisateur, JWT signé avec claims `sub`, `schoolId`, `role`.
*Dépend de* : JGK-A03. *Critères* : token expiré → 401 ; refresh token révoqué après logout ; mot de passe stocké hashé (Identity par défaut).

**JGK-A05** [H] — Gestion du cycle de vie utilisateur (suspension/blocage)
Endpoint `PATCH /users/{id}/status` avec motif obligatoire, historisation complète.
*Dépend de* : JGK-A04. *Critères* : un utilisateur `Blocked` ne peut plus obtenir de token ; l'historique des changements de statut est consultable.

**JGK-A06** [H] — Déconnexion automatique par inactivité
Middleware/mécanisme côté frontend expirant la session après `autoLogoutMinutes` (paramétrable par école).
*Critères* : configurable par établissement, valeur par défaut 10 minutes.

---

## Module B — Établissements & Abonnements (Super Admin)

**JGK-B01** [C] — CRUD Établissements (Super Admin)
`GET/POST /schools`, création avec compte Directeur initial.
*Dépend de* : JGK-A04. *Critères* : seul un `SuperAdmin` peut créer une école ; le Directeur créé reçoit ses identifiants par email.

**JGK-B02** [C] — Paramètres de l'établissement
`GET/PUT /schools/current/settings` (notation /10 ou /20, formats de matricule, format de date, durée de déconnexion).
*Critères* : les valeurs par défaut sont appliquées à la création ; seul le Directeur peut modifier.

**JGK-B03** [H] — Gestion des abonnements
`GET/PATCH /subscriptions/{schoolId}`, alertes automatiques 30/15/7 jours avant expiration (job planifié), passage automatique en lecture seule à expiration.
*Dépend de* : JGK-B01. *Critères* : un test vérifie qu'à J-30 une notification est déclenchée ; qu'après expiration, toute requête d'écriture d'un utilisateur de cette école renvoie `403` avec un code d'erreur explicite.

---

## Module C — Structure pédagogique

**JGK-C01** [C] — Années scolaires
`GET/POST /school-years`, une seule année active à la fois, années passées en lecture seule.
*Dépend de* : JGK-B02.

**JGK-C02** [C] — Classes (nomenclature libre)
`GET/POST /classrooms`. Aucune liste de niveaux codée en dur — l'établissement définit librement ses classes.
*Critères* : une classe créée apparaît immédiatement disponible dans les modules Finance et Notes sans configuration additionnelle (test d'intégration croisé).

**JGK-C03** [H] — Matières et coefficients
`GET/POST /subjects`, rattachées à un niveau.
*Dépend de* : JGK-C02.

---

## Module D — Élèves & Enseignants

**JGK-D01** [C] — Création élève avec génération de matricule à l'enregistrement
`POST /students`. Le matricule est calculé **dans la même transaction** que l'insertion, selon `studentMatriculeFormat` de l'établissement.
*Dépend de* : JGK-C02. *Critères* : un test simule l'ouverture de 3 formulaires en parallèle sans enregistrement — aucun numéro n'est consommé ; les matricules générés sont strictement séquentiels sans trou.

**JGK-D02** [H] — Fiche élève complète
`GET /students/{id}` incluant historique scolaire, notes, paiements.
*Dépend de* : JGK-D01.

**JGK-D03** [C] — Création enseignant avec matricule à l'enregistrement
`POST /teachers`, même règle que JGK-D01.

**JGK-D04** [M] — Fiche enseignant complète
`GET /teachers/{id}` avec classes/matières attribuées, historique.

**JGK-D05** [H] — Gabarit de dashboard réutilisable (Razor + Tailwind)
Construire le gabarit générique de listing (barre latérale, barre supérieure, cartes KPI, filtres, tableau paginé, menu d'actions) **reproduisant exactement `docs/design-references/dashboard-reference.jpg`** (Volume 5 §2, description dans `docs/design-references/README.md` §3), en couleur primaire violet/indigo (`tailwind.config.js`). **Le menu latéral n'affiche que les modules autorisés pour le rôle connecté** (Volume 5 §3.2), Super Admin inclus. Appliqué en premier lieu à la vue "Liste des élèves" (JGK-D01/D02), puis réutilisé tel quel pour les autres modules (Enseignants, Classes, Paiements...).
*Dépend de* : JGK-D02. *Critères* : comparaison visuelle avec la référence (structure, couleurs, disposition des cartes et du tableau) ; le gabarit est un composant/layout partagé, pas dupliqué copier-coller par vue ; connecté avec 2 rôles différents (ex. Enseignant et Directeur), le menu affiché diffère et correspond exactement à la matrice du Volume 7 §15 ; taper directement l'URL d'un module non affiché renvoie une erreur d'autorisation côté API, pas seulement une absence de lien.

---

## Module E — Inscriptions

**JGK-E01** [C] — Inscription/réinscription avec calcul financier automatique
`POST /enrollments`. Déclenche automatiquement le calcul des frais dus (inscription, scolarité, frais annexes) dans le module Finance.
*Dépend de* : JGK-D01, Module F (frais paramétrés). *Critères* : le service Finance ne peut pas modifier directement `totalDue` généré — seule une action Secrétariat/Admin historisée peut le corriger (test d'autorisation négatif obligatoire).

**JGK-E02** [H] — Documents d'inscription (fiche + reçu PDF)
Génération PDF via QuestPDF. **Le reçu reproduit exactement `docs/design-references/receipt-reference.png`** (Volume 1 §7.3bis), y compris la mention obligatoire *"Il est demandé aux parents de garder minutieusement leur reçu après le paiement."*
*Dépend de* : JGK-E01. *Critères* : comparaison visuelle avec la référence (mise en page, ordre des champs, tableau des frais, mention obligatoire présente).

**JGK-E03** [H] — Notification e-mail du Directeur à chaque inscription
Un audit a confirmé qu'aucune notification n'est déclenchée lors de l'inscription d'un élève. `CreateEnrollmentCommandHandler` publie un événement de domaine MediatR `StudentEnrolledEvent` (nom de l'élève, matricule, date/heure — pas l'entité complète) après le `SaveChangesAsync` de la transaction d'inscription, jamais avant. Un `INotificationHandler` dédié envoie un e-mail via `IEmailSender` à tous les utilisateurs `Role.Directeur` actifs de l'école (`SchoolId` de l'événement) — jamais à `School.Email`, adresse de contact imprimée sur le reçu et pas forcément surveillée. Un échec d'envoi (SMTP transitoire) est journalisé et n'interrompt ni les autres destinataires ni l'inscription elle-même : `SmtpEmailSender.SendAsync` relance l'exception SMTP, le handler doit l'absorber pour chaque destinataire pris isolément. Aucun Directeur actif trouvé -> avertissement journalisé, aucune exception.
*Dépend de* : JGK-E01. *Critères* : un Directeur actif reçoit l'e-mail à l'inscription ; aucun Directeur actif ne produit ni exception ni e-mail ; l'échec d'envoi à un destinataire n'empêche pas l'envoi aux autres et ne remonte jamais jusqu'à l'appelant (test dédié obligatoire, comme pour `NotifyParentOnAttendanceEventHandler`).

---

## Module F — Finance

**JGK-F01** [C] — Paramétrage des frais par classe
Montant standard + application en masse + exceptions par classe, historique des modifications.
*Dépend de* : JGK-C02.

**JGK-F02** [C] — Enregistrement des paiements
`POST /finance/payments` avec verrouillage optimiste (`row_version`), génération de reçu. Refuse (422) tout encaissement hors d'une session de caisse ouverte pour l'utilisateur courant (Volume 1 §15.1).
*Critères* : deux paiements concurrents sur le même solde ne produisent jamais un état incohérent — test de concurrence obligatoire.
**Effet de bord corrigé (27/08/2026)** : l'écran `/caisse` n'exposait AUCUN moyen d'ouvrir ou de clôturer une session de caisse — `POST /finance/sessions/open` et `POST /finance/sessions/{id}/close` existaient côté API sans jamais être appelés par aucun fichier JS du dépôt, alors que la contrainte ci-dessus est inconditionnelle. Conséquence réelle : aucun encaissement n'a jamais pu aboutir en production. Corrigé par l'ajout de `GET /finance/sessions/current` (`GetCurrentCashierSessionQueryTests.cs`, 4/4) et son câblage complet sur `/caisse` — bandeau d'ouverture (fonds initial), statut en direct (total encaissé, nombre de versements), et modale de clôture avec accès au rapport PDF. Vérifié en conditions réelles (navigateur + PostgreSQL) : ouverture → encaissement (avec coupure réseau simulée, JGK-L03) → clôture → téléchargement du rapport, de bout en bout.
**Second bug corrigé au passage** : `DailyClosingReportPdfGenerator` ne posait pas `QuestPDF.Settings.License = LicenseType.Community` dans son constructeur statique, contrairement aux 25 autres générateurs du projet — le rapport de clôture renvoyait donc systématiquement une erreur 500, jamais un PDF, depuis sa livraison. Mis au jour en cliquant réellement le bouton depuis l'écran, jamais par un test (aucun test n'appelait ce générateur jusqu'ici).

**JGK-F03** [H] — Dépenses
`GET/POST /finance/expenses`.

**JGK-F04** [H] — Tableau de bord financier
`GET /finance/dashboard` (encaissé jour/mois/année, solde dû, taux de recouvrement).
*Dépend de* : JGK-F02.

**JGK-F05** [M] — Rapports financiers exportables
Journal de caisse, rapport mensuel/annuel en PDF.

**JGK-F09** [H] — Contrôle du comptage physique et écarts de caisse à la clôture
**Statut : livré (29/08/2026).** `CloseCashierSessionCommand` exige désormais `ActualCashAmount` (espèces réellement comptées dans le tiroir-caisse) — paramètre requis de la commande, jamais une valeur facultative. L'écart se compare aux ESPÈCES attendues (`ExpectedCashAmount` = fonds initial + encaissements EN ESPÈCES uniquement de la session), jamais au total toutes méthodes confondues : un virement, un chèque ou un versement mobile money ne transite jamais par le tiroir-caisse physique et ne peut donc jamais participer à un manquant ou un surplus constaté au comptage — même distinction que `TotalCashInRegister`, déjà présent sur le rapport PDF avant ce ticket. Un écart non nul (manquant ou surplus) exige `DiscrepancyReason` avant de pouvoir clôturer (422 sinon) ; la session reste `Open` tant qu'il n'est pas fourni. L'écriture d'ajustement historisée exigée par le ticket vit directement sur la ligne `CashierSession` elle-même (`ActualCashAmount`, `DiscrepancyAmount`, `DiscrepancyReason`) plutôt que dans une table séparée : une session close ne se rouvre ni ne se re-clôture jamais, ce qui en fait déjà un enregistrement immuable — `IAuditableRequest` journalise en plus qui a clôturé, quand, avec quelles valeurs (JGK-H01). Le rapport de clôture PDF (`DailyClosingReportPdfGenerator`) affiche désormais un bloc « Contrôle de caisse » : espèces attendues, espèces comptées, écart (coloré), motif. Écran `/caisse` : le champ de comptage est obligatoire dans la modale de clôture ; un premier essai en 422 révèle le champ motif plutôt que de l'exiger par anticipation — le serveur seul connaît les espèces attendues.
*Dépend de* : JGK-F02 (câblage session de caisse). *Livrables* : migration `AddCashierSessionDiscrepancy` (3 colonnes nullables, strictement additive) ; `CloseCashierSessionCommandValidatorTests.cs` (5/5, dont le rejet d'un montant négatif) ; `CashierSessionClosingTests.cs` (6/6, dont `Non_Cash_Payments_Never_Enter_The_Expected_Cash_Amount` — le test qui aurait échoué si l'écart avait été comparé au total toutes méthodes). Vérifié en conditions réelles (navigateur + PostgreSQL, skill `run`) : montant compté incorrect → 422 → motif saisi → clôture réussie → rapport PDF à jour, de bout en bout.

---

## Module G — Notes & Bulletins

**JGK-G01** [C] — Saisie de notes avec verrouillage optimiste
`POST /grades`. Conflit de version → `409 Conflict` explicite, jamais d'écrasement silencieux.
*Dépend de* : JGK-C03, JGK-D01. *Critères* : test simulant deux enseignants modifiant la même note simultanément — le second doit recevoir `409`.

**JGK-G02** [C] — Calcul automatique des moyennes et mentions
Application automatique de la notation /10 ou /20 selon le niveau ; mentions personnalisables.

**JGK-G03** [C] — Génération de bulletin PDF (format A5 portrait)
`POST /report-cards/generate`. **Reproduit exactement `docs/design-references/bulletin-reference.png`** (Volume 1 §8.1) : mêmes colonnes, mêmes blocs de synthèse, mêmes mentions. Ajustement automatique des colonnes, suppression des décimales inutiles (`17.0` → `17`), aucun débordement de page.
*Dépend de* : JGK-G02. *Critères* : test visuel/snapshot sur un cas à 12 matières ne dépasse pas une page A5 ; comparaison visuelle avec la référence.

---

## Module H — Journalisation & Audit

**JGK-H01** [H] — Journal d'audit centralisé
`GET /audit-logs`, capture automatique de : connexions, erreurs, paiements, impressions, exports, changements de statut utilisateur, actions Super Admin.
*Critères* : chaque écriture sensible (JGK-F02, JGK-G01, JGK-A05) produit une entrée d'audit correspondante — vérifié par test d'intégration croisé.

---

## Module I — Inscription self-service & Paiement des abonnements

**JGK-I01** [C] — Formulaire public d'inscription
`POST /registration-requests` (sans authentification). Hache le mot de passe immédiatement, génère une `trackingReference` unique, envoie un email de confirmation. Anti-spam (captcha ou honeypot) obligatoire.
*Critères* : le mot de passe en clair n'apparaît dans aucun log ; deux soumissions avec le même email sont autorisées (une école peut retenter) mais chaque `trackingReference` est unique.

**JGK-I02** [C] — Suivi public d'une demande
`GET /registration-requests/{trackingReference}/status`, sans authentification.
*Dépend de* : JGK-I01.

**JGK-I03** [C] — Revue et approbation par le Super Admin
`GET /admin/registration-requests`, `POST /admin/registration-requests/{id}/approve`, `POST /admin/registration-requests/{id}/reject`. L'approbation crée, dans une seule transaction : `School` (Active), `User` Directeur (Active, mot de passe déjà défini), `Subscription` (`AwaitingPayment`).
*Dépend de* : JGK-I01, JGK-B01. *Critères* : un test vérifie l'atomicité (si la création de l'un des trois échoue, aucun des trois n'est créé) ; un rejet n'a aucun effet sur `Schools`/`Users`.

**Complément (20/09/2026)** : `SubmitRegistrationRequestHandler` envoie désormais, en plus de la confirmation au demandeur, une alerte à `Registration__AdminNotificationEmail` (optionnelle — vide par défaut, aucune alerte alors) avec le détail de la demande et un lien vers `/admin/inscriptions`. L'e-mail d'approbation (`ApproveRegistrationRequestHandler`) porte désormais un lien de connexion direct (`Registration__PublicBaseUrl` + `/login`). Nouveau réglage `RegistrationSettings` (`SamaEcole.Application.Registration`), même idiome que `AuthSettings`/`StateIntegrationSettings` — voir `.env.example` et Volume 9 §Variables à poser sur le service.

**JGK-I04** [C] — Restriction d'accès `AwaitingPayment`
Middleware d'autorisation bloquant tout endpoint hors `/subscriptions/{schoolId}/payments` et profil utilisateur tant que `Subscriptions.Status = AwaitingPayment`, réutilisant la logique de mode restreint existante (JGK-B03).
*Dépend de* : JGK-I03. *Critères* : un token JWT valide émis avant paiement ne donne accès à aucun autre module tant que le paiement n'est pas confirmé — vérifié même si le token n'a pas expiré.

**JGK-I05** [C] — Initiation de paiement d'abonnement
`POST /subscriptions/{schoolId}/payments` : crée une `SubscriptionPayment` (`Initiated`), appelle l'agrégateur (PayDunya/CinetPay), retourne l'URL de redirection.
*Dépend de* : JGK-I04.

**JGK-I06** [C] — Webhook de confirmation de paiement
`POST /webhooks/payments/{provider}` : vérifie la signature HMAC, déduplique par `ProviderTransactionRef`, passe `SubscriptionPayment` à `Confirmed`, met à jour `Subscription` (`Active`, nouvelle `EndDate` selon la période choisie).
*Dépend de* : JGK-I05. *Critères* : une signature invalide → `401` + entrée d'audit, aucune mise à jour ; un même `ProviderTransactionRef` reçu deux fois ne confirme/n'étend la date qu'une seule fois (test de rejeu obligatoire).

**JGK-I07** [H] — Historique des paiements (Directeur)
`GET /subscriptions/{schoolId}/payments`.
*Dépend de* : JGK-I06.

---

## Module J — Examens officiels (CFEE/BFEM/BAC)

**JGK-J01** [C] — Sessions et dossiers d'examen
`GET/POST /exams/sessions`, `GET/POST /exams/dossiers`. Une session par (année scolaire, type d'examen, série), un dossier par élève de classe d'examen (CM2/3ème/Terminale et séries) rattaché à une session.
*Dépend de* : JGK-C01, JGK-C02, JGK-D01. *Critères* : un élève ne peut avoir qu'un seul dossier par session (`UNIQUE`) ; création possible uniquement depuis une classe cohérente avec le type d'examen (pas de dossier BFEM depuis une classe de CM2).

**JGK-J02** [C] — Contrôle d'état civil et audit automatique
`PUT /exams/dossiers/{id}` (numéro/présence de l'extrait de naissance, conformité nom/prénom/date/lieu de naissance). `GET /exams/dossiers/audit` relève les dossiers `Incomplet` et le détail des pièces/champs manquants ou non conformes.
*Dépend de* : JGK-J01. *Critères* : un dossier sans extrait de naissance déclaré présent ne peut jamais passer `Complet` ; l'audit détecte à la fois l'absence de pièce et une incohérence déclarée (ex. conformité marquée `false`).

**JGK-J03** [H] — Attribution centre d'examen &amp; numéro de table
`POST /exams/dossiers/{id}/assign-center`. Le numéro de table est généré **dans la transaction d'attribution**, jamais à l'ouverture du dossier (même règle que le matricule, AGENTS.md #3).
*Dépend de* : JGK-J01. *Critères* : numéro de table unique par session (`UNIQUE` partiel) ; un test simule des attributions concurrentes sans doublon ni trou anormal.

**JGK-J04** [H] — Fiches de candidature PDF (unitaire + impression par lot)
`GET /exams/dossiers/{id}/candidate-form/pdf`, `POST /exams/dossiers/candidate-forms/pdf` (lot, filtrable par session/classe). QuestPDF, prêtes à signer.
*Dépend de* : JGK-J02. *Critères* : le lot ne contient que des dossiers `Complet` ou `Transmis` (jamais `Incomplet`) sauf drapeau explicite de forçage journalisé.

**JGK-J05** [M] — Dispatch des convocations
`GET /exams/dossiers/{id}/convocation/pdf`, `POST /exams/sessions/{id}/dispatch-convocations`. Réutilise le canal SMS/WhatsApp existant (`Feature.SmsNotifications`), pas de nouveau canal.
*Dépend de* : JGK-J03. *Critères* : dispatch impossible tant que centre et numéro de table ne sont pas attribués ; chaque envoi produit une entrée d'audit (Module H).

**JGK-J06** [H] — Export ministériel (Excel/CSV IEF/IA)
`GET /exams/export/ministerial?examSessionId=...`. Réutilise le mécanisme d'export `.xlsx` déjà en place pour les rapports financiers (JGK-F05).
*Dépend de* : JGK-J02. *Critères* : colonnes et format conformes au relevé attendu par l'IEF/IA (à valider avec un gabarit réel avant livraison) ; export journalisé à l'audit.

**JGK-J07** [M] — Résultats de délibération &amp; statistiques
`PUT /exams/dossiers/{id}/result`, `GET /exams/statistics` (taux de réussite par série/classe, comparaison interannuelle).
*Dépend de* : JGK-J01. *Critères* : les statistiques ne portent que sur les dossiers `Transmis`/`Valide` d'une session clôturée.

**JGK-J08** [F] — Lecture des dossiers filtrée par classe assignée (Enseignant)
**Statut : livré (26/08/2026).** `GET /exams/dossiers` et `GET /exams/dossiers/{id}` ouverts au rôle `Enseignant`, restreints aux classes qui lui sont attribuées sur l'année ACTIVE (`TeacherAssignments`, `ExamDossierScopeAuthorizer` — même mécanique que `AttendanceScopeAuthorizer` pour l'appel). Un `classroomId` explicite hors de la portée de l'Enseignant sur la liste, ou une URL de dossier tapée directement hors de ses classes, renvoient tous deux 403 — jamais une absence silencieuse de résultat pour la fiche détaillée.
*Dépend de* : JGK-J01. *Critères vérifiés* : `ExamDossierScopeTests.cs` — un enseignant authentifié ne voit que les dossiers des classes où il a une affectation active ; taper directement l'URL d'un dossier hors de ses classes renvoie une erreur d'autorisation, pas une absence de lien.
*Effet de bord corrigé* : `GetExamDossierDetailQueryHandler` appelait `EF.Property<uint>(dossier, "xmin")` hors d'une requête LINQ — invalide en dehors d'une traduction SQL, donc `GET /exams/dossiers/{id}` renvoyait 500 sur TOUT appel, y compris Directeur/Secrétariat, depuis la livraison de J02. Aucun test n'exerçait ce Handler avant `ExamDossierScopeTests.cs` ; corrigé en réutilisant le motif de `AssignExamCenterCommandHandler` (relecture du `xmin` par une requête `Select` dédiée).

---

## Module K — Extension RH &amp; Paie

**JGK-K01** [H] — Suggestion automatique des heures de vacation
**Statut : livré (26/08/2026), câblage écran terminé le 27/08/2026.** Agrège `TeacherHourRecord` (rapproché de `ScheduleSlot` pour signaler les écarts) via `GET /finance/employee-contracts/{id}/suggested-hours` — consultatif, n'écrit rien. L'écran `/paie` propose un bouton « Suggérer les heures depuis le pointage » dans la modale de génération de fiche (visible seulement pour un contrat Vacataire) : il affiche le total suggéré et les écarts, et ne **pré-remplit** `HoursWorked` que sur un clic explicite « Utiliser cette suggestion » — la valeur reste éditable, la validation humaine reste seule à clôturer la fiche de paie (arbitrage acté, Volume_1 §14.3 amendé). L'API a existé plusieurs jours sans consommateur avant ce câblage : à surveiller pour tout futur ticket « API only ».
*Dépend de* : module Paie existant (`EmployeeContract`, `FichePaie`, `TeacherHourRecord`, `ScheduleSlot`). *Critères vérifiés* : `GetSuggestedPayrollHoursQueryHandlerTests.cs` — un écart entre heures pointées et créneaux planifiés est signalé, pas rejeté ; générer une fiche sans jamais avoir consulté la suggestion reste possible (non bloquant, `payroll.js`).

**JGK-K02** [M] — Moyens de paiement RH (Bancaire/Wave/Orange Money)
**Statut : livré (26/08/2026), câblage écran terminé le 27/08/2026.** Champ moyen de paiement + référence de compte sur `EmployeeContract`, distinct du `PaymentMethod` finance élève. Les modales Nouveau contrat et Modifier le contrat de l'écran `/paie` exposent désormais ce champ (Espèces par défaut), affiché en colonne du tableau des contrats.
*Dépend de* : `EmployeeContract` existant. *Critères vérifiés* : coordonnées bancaires/mobile money jamais journalisées en clair dans un log applicatif (aucun appel de log ne référence `PayoutAccountReference`, Volume 7).

---

## Module L — Résilience réseau (caisse &amp; pointage)

**JGK-L01** [H] — Clé d'idempotence sur encaissement caisse
**Statut : livré (26/08/2026).** Clé UUID générée côté client (`Payment.IdempotencyKey`, migration `AddPaymentIdempotencyKey`), index unique partiel `(SchoolId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL`. Un retry avec la MÊME clé rejoue le résultat déjà produit (même `PaymentId`/`ReceiptNumber`) au lieu de créer un second paiement — vérifié avant toute autre logique du Handler, y compris si la session de caisse a été fermée entre-temps. Reste strictement dans le cadre D-01 (Volume_0 §0.13) : aucune base locale, aucune queue persistée, aucune transaction confirmée sans réponse serveur.
**Le pointage d'absences n'a pas eu besoin d'une clé dédiée** : `SubmitAttendanceSheetCommand` porte déjà une contrainte d'unicité naturelle (classe, matière, date, créneau) qui refuse en 409 un second appel identique — c'est déjà une forme d'idempotence fonctionnelle. Un retry y est donc sûr par construction ; c'est au FRONTEND (JGK-L02) d'interpréter un 409 pendant un retry comme une confirmation, pas comme une erreur à afficher.
*Dépend de* : module Caisse (JGK-F02). *Critères vérifiés* : `PaymentIdempotencyTests.cs` — même clé soumise deux fois = un seul paiement et un seul débit du solde ; clés différentes = deux paiements réels ; clé absente = fonctionne comme avant ce ticket.

**JGK-L02** [M] — Extension `network-guard.js` (retry, état « en attente d'envoi »)
**Statut : livré (26/08/2026), câblage écran restant — voir JGK-L03.** `window.networkGuard.submitWithRetry(url, options, config)` : retry avec backoff exponentiel (1s → 15s, 5 tentatives) tant que l'onglet reste ouvert, sur une erreur RÉSEAU uniquement (jamais sur une réponse HTTP, même une erreur métier). Callback `onStateChange('sending'|'retrying'|'done'|'failed', attempt)` pour piloter l'état UI. `newIdempotencyKey()` génère l'UUID à inclure par l'appelant dans son body AVANT le premier essai. Aucune persistance de la saisie en attente (ni `localStorage`, ni IndexedDB) — perte assumée si l'onglet se ferme avant confirmation.
*Dépend de* : JGK-L01. *Critères* : la fonction ne retente jamais une réponse HTTP reçue (succès ou erreur métier), seulement l'absence de réponse.

**JGK-L03** [M] — Câblage de `submitWithRetry` sur les écrans Caisse et Pointage
**Statut : livré (26/08/2026).** `window.api.postWithRetry` (nouveau, `wwwroot/js/api.js`) câble `submitWithRetry` derrière la même normalisation d'erreurs que `post()`. Le formulaire d'encaissement (`/caisse`) génère une `idempotencyKey` à l'ouverture (`newIdempotencyKey()`, régénérée à chaque nouvel encaissement dans `startNewPayment()`), l'inclut dans le body de `POST /finance/payments`, et affiche l'état « en attente d'envoi » sur `onStateChange`. Le formulaire d'appel (`/attendance`, écran Présences) appelle `postWithRetry` sur `POST /attendance` et traite un **409 reçu pendant un retry** (`lastAttempt > 1`, pas au premier essai) comme un succès silencieux — la feuille a déjà été enregistrée par la tentative précédente dont la réponse s'est perdue.
*Dépend de* : JGK-L01, JGK-L02. *Critères vérifiés (27/08/2026, skill `run`, navigateur réel + PostgreSQL)* : coupure réseau simulée sur le premier essai de `POST /finance/payments` (session de caisse ouverte au préalable, voir JGK-F02) — un seul appel aboutit derrière, le reçu s'affiche normalement, un seul paiement en base. Cette vérification a d'abord été bloquée par l'absence de session de caisse côté écran (corrigé, voir JGK-F02) puis par un bug du générateur PDF de clôture (corrigé, voir JGK-F02) : sans le câblage complet de bout en bout, cette case n'aurait jamais pu être cochée honnêtement.

---

## Module M — Intégration étatique (SIMEN / Planète / STATEDUC)

> **Statut d'ensemble : LIVRÉ (back-end 30/08/2026, front-end 30/08/2026).** Abstractions, schéma,
> migrations `AddStateIntegrationModule` + `FixMutationCertificateReasonDefault` (colonnes + table
> `student_mutation_certificates` + policy RLS + fonction SECURITY DEFINER
> `verify_mutation_certificate`), handlers, documents QuestPDF, sérialiseur Planète, contrôleur
> (`/api/v1/state-integration`, 11 routes dont la vérification publique du QR + le registre + la
> révocation). **Écrans** : `/integration-etatique` (3 onglets : export Planète, rapport STATEDUC,
> registre des certificats avec révocation) + bandeau « Statut du relais SIMEN » ; champ IEN sur la
> fiche élève (saisie officielle / génération provisoire) ; page publique `/verifier/mutation/{token}`.
>
> `dotnet build` **vert (0 erreur)** ; `SimenComplianceTests` **26/26** ; `RlsCoverageTests` **vert** ;
> suite unitaire **1004/1004** ; intégration ciblée (Students, ReportCards, RLS) **vert** ; parcours
> complet vérifié au navigateur/`curl` sur PostgreSQL réel (skill `run`) : login Directeur → IEN
> officiel + provisoire (`P…` + clé Luhn) → export Planète CSV (BOM, `;`, cellules vides) → STATEDUC
> JSON/PDF/Excel → délivrance d'un certificat (`MUT-2025-0001`, PDF+QR) → révocation (motif obligatoire,
> 2ᵉ révocation en 409) → vérification publique anonyme du certificat révoqué (statut seul, aucune
> donnée d'élève).
>
> **Écart trouvé et corrigé pendant la vérification :** `StudentMutationCertificate.Reason` portait un
> `HasDefaultValue(Autre)` — or la valeur CLR par défaut de l'enum (`Demenagement` = 0) est aussi celle
> qu'EF lit comme « non affectée » : une mutation réellement pour « Déménagement » aurait été
> enregistrée « Autre » en silence. Défaut retiré (`FixMutationCertificateReasonDefault`, additive), le
> Handler fournit toujours `Reason`. La génération d'IEN provisoire sans code établissement renvoyait un
> 500 (InvalidOperationException) — désormais un 409 actionnable, même message que le refus de l'export
> Planète.
>
> **Reste à faire :** champ de saisie du code établissement national et des coordonnées GPS dans
> *Paramètres → Établissement* (JGK-M05) ; tests d'intégration dédiés (agrégats STATEDUC, concurrence
> sur la séquence IEN, refus 409 code absent).
>
> **Cadre non négociable du module** : aucune API publique du SIMEN n'existe. Le module produit des
> FICHIERS que l'école transmet par la voie habituelle. Rien dans l'interface ne doit laisser croire à
> un dialogue avec le ministère — d'où le bandeau « Statut du relais » qui affiche l'indisponibilité
> plutôt qu'une action de transmission. → Volume 1 §23.

**JGK-M01** [H] — IEN : champ, index et générateur de secours
Champ `Students.IenNumber` (nullable, `varchar(24)`) + `IsIenProvisional`, index unique **partiel**
`(SchoolId, IenNumber) WHERE IenNumber IS NOT NULL`. `IIenGeneratorService` / `NationalIenGenerator`
(Persistence) : format provisoire `P` + code établissement (6) + millésime (2) + séquence (5) + clé
Luhn, séquence partagée avec `MatriculeGenerator` (même sérialisation sous concurrence, même
annulation sur rollback). `AssignStudentIenCommand` : saisie d'un officiel, ou génération d'un
provisoire.
*Dépend de* : `Students`, `MatriculeGenerator`. *Critères* : un IEN officiel écrase un provisoire ; un
provisoire ne remplace **jamais** un officiel (409) ; deux élèves de la même école ne peuvent porter le
même IEN (409 nommant le porteur actuel) ; l'absence de `NationalSchoolCode` fait échouer la
génération avec un message actionnable, jamais un numéro à code inventé.
*Livré (30/08/2026)* : migration `AddStateIntegrationModule`, `IenNumberFormat` (forme + clé Luhn, dans Application), `NationalIenGenerator` (séquence). Tests : `SimenComplianceTests` (forme, clé, provisoire vs officiel). *Reste* : test de concurrence sur la séquence (intégration, non écrit).

**JGK-M02** [H] — Export « Planète Ready » (CSV / JSON)
`GetPlaneteExportQuery` + `PlaneteExportSerializer` (une seule définition de colonnes pour les deux
formats). CSV point-virgule + BOM UTF-8 (Excel fr), dates ISO 8601.
*Dépend de* : JGK-M01, JGK-M05 (code établissement). *Critères* : le périmètre est l'**inscription non
annulée** de l'exercice et la classe **de l'inscription** (figée), pas l'état courant de l'élève ;
aucune valeur inventée (cellule vide, jamais « - ») ; aucune donnée financière ; refus explicite si le
code établissement manque ; le CSV et le JSON du même export portent les mêmes colonnes dans le même
ordre.
*Livré (30/08/2026)* : `GetPlaneteExportQuery`/Handler/Validator, `PlaneteExportSerializer`. Tests `SimenComplianceTests` : parité colonnes CSV/JSON, cellule vide (jamais « - »/« null »), échappement RFC 4180, BOM UTF-8, compteurs de qualité. *Reste* : test d'intégration du refus 409 (code établissement absent).

**JGK-M03** [M] — Relais API SIMEN (contrat seul)
`ISimenBridgeService` (transmission de lot, recherche d'IEN officiels) +
`UnavailableSimenBridgeService`, qui **refuse chaque appel explicitement**. `GET /simen/status` pour
que l'écran n'affiche pas une action vouée à l'échec.
*Dépend de* : JGK-M02. *Critères* : aucune réussite simulée — un succès factice ferait croire à l'école
que sa déclaration est faite ; `LookupOfficialIensAsync` renvoie une liste **vide** et non N résultats
à IEN nul (« je n'ai pas cherché » ≠ « je n'ai rien trouvé »).
*Le jour de l'implémentation réelle* : secret en configuration, **webhook entrant signé HMAC vérifié
avant écriture** (règle #11 — aucune route client ne marque « Transmis »), appel journalisé, et
résultat de recherche **validé par un humain** avant écriture sur une fiche (rapprochement par
nom/date/lieu de naissance : les homonymes sont fréquents).

**JGK-M04** [H] — Rapport annuel STATEDUC (PDF A4 paysage + Excel)
`GetStateducReportQuery` (effectifs par niveau, pyramide des âges, ratios F/G, qualifications et
statuts enseignants, infrastructures), `StateducReportDocument` + `StateducReportExcelGenerator`.
Champs `Teacher` ajoutés : `Gender` (nullable), `AcademicQualification`, `ProfessionalQualification`,
`CivilServiceStatus`, `CivilServiceMatricule`, `FirstAppointmentDate`.
*Dépend de* : `Enrollments`, `Teachers`, `Buildings`/`Rooms`. *Critères* : l'âge est calculé à la
**date d'observation**, jamais « aujourd'hui » ; JSON, PDF et Excel sortent du **même** Handler ; les
lacunes de saisie sont comptées à part (colonne « genre non saisi », ligne « non renseigné », tranche
« âge non déterminé ») et **nommées dans un encadré** du PDF ; les ratios sont `null` — jamais `0` —
quand le dénominateur est nul ; les enseignants archivés sont exclus.
*Arbitrage acté* : la troisième colonne « genre non saisi » n'existe pas sur le formulaire officiel.
Elle est ajoutée parce que les deux seules alternatives étaient fausses — imputer d'office à l'une des
deux colonnes (faux et indétectable), ou déduire le genre du prénom (faux pour une part importante des
prénoms sénégalais, et faux en silence).
*Livré (30/08/2026)* : `GetStateducReportQuery`/Handler, `StateducReportDocument` (A4 paysage), `StateducReportExcelGenerator`, colonnes `Teacher`, migration. *Reste* : tests d'agrégation (pyramide à date fixe, ratios à effectif nul) — non écrits.

**JGK-M05** [M] — Champs réglementaires (établissement & examens)
`School` : `NationalSchoolCode` (unique global, partiel), `MinistryAuthorizationNumber`,
`SchoolDistrictCode`, `GpsLatitude`/`GpsLongitude` (`numeric(9,6)`) + `GpsCoordinates` **calculé, non
mappé**. `ExamDossier` : `ExamCenterCode`, `TableNumber`, `CivilRegistryDocumentStatus`.
*Dépend de* : module Examens (JGK-J01). *Critères* : `CivilRegistryDocumentStatus` **complète** sans
remplacer `BirthCertificatePresent`/`CivilStatusConforming`, encore lus par
`GetExamDossierAuditQuery` ; `TableNumber` (place en salle) reste distinct de `CandidateNumber`
(numéro d'inscription) ; les coordonnées GPS ne sont jamais stockées en chaîne.
*Livré (30/08/2026, back-end + écrans)* : colonnes `School`/`ExamDossier`, `GpsCoordinates` calculé non
mappé (`builder.Ignore`), migration, index unique partiel global sur `NationalSchoolCode`.
**Écrans** :
- *Paramètres → Établissement* — nouveau bloc « Intégration étatique (SIMEN) » : code établissement
  national, n° d'autorisation ministérielle, code de circonscription, coordonnées GPS (deux champs,
  les deux ensemble ou aucune, 422 sinon ; `gpsCoordinates` affiché en lecture seule). Étend
  `UpdateCurrentSchoolCommand` + `SchoolProfileDto` + `GetCurrentSchoolQuery`.
- *Fiche enseignant* (création + modification) — bloc replié « Informations pour le rapport
  STATEDUC » (facultatif) : genre, diplôme académique, diplôme professionnel, statut administratif,
  matricule de solde, date de première prise de service. Nouveaux champs sur `Teacher`,
  `CreateTeacherCommand`, `UpdateTeacherCommand`, `TeacherProfileDto`. Défauts « NonRenseigne » — le
  rapport les compte dans une ligne dédiée.

*Vérifié au navigateur / `curl` (skill `run`, PostgreSQL réel)* : `PUT /schools/current` persiste code
+ GPS ; `gpsCoordinates` = « 14.692800, -17.446700 » ; latitude seule → 422 ; **`GET
/state-integration/planete/export` passe de 409 à 200** dès le code posé (objectif du ticket) ; le nom
du fichier Planète neutralise désormais les caractères non alphanumériques du code ; l'agrégat STATEDUC
compte correctement un enseignant sans qualification/genre (`unreportedQualificationTeachers`,
`teachersWithoutGender`).

> **Bug pré-existant repéré au passage → corrigé par JGK-T01 (voir ci-dessous).** Décocher une matière
> sur la fiche enseignant échouait en 500 (`42501: permission denied for table teacher_subjects`).

**JGK-M06** [H] — Certificat de mutation avec QR de vérification
Table tenant `student_mutation_certificates` + `GenerateStudentMutationCertificateCommand` +
`StudentMutationCertificateDocument` (A4 portrait, une page). Numéro `MUT-{YEAR}-{SEQ:4}` généré dans
la transaction (règle #3, `MatriculeKind.MutationCertificate`).
*Dépend de* : JGK-M01, `IQrCodeService`. *Critères* : le QR encode une **URL de vérification**, jamais
l'état civil ; le code est **32 hex d'aléa cryptographique**, jamais l'`Id` (table non énumérable) ;
son index unique est **global et sans `SchoolId`** — le scan vient d'un tiers hors plateforme ; la
classe quittée est **figée** ; un solde impayé **n'empêche pas** la délivrance et le certificat
n'affiche **aucun montant** ; une erreur se corrige par révocation, jamais par réécriture.
*Livré (30/08/2026)* : entité + `GenerateStudentMutationCertificateCommand`/Handler/Validator,
`StudentMutationCertificateDocument` (QR via `IQrCodeService`), migration (table + policy RLS +
fonction SECURITY DEFINER `verify_mutation_certificate`), route publique
`GET /api/v1/state-integration/certificates/verify/{token}` (anonyme, rate-limité, réponse sans
donnée d'élève, `unknown` en 200), `VerifyMutationCertificateQuery`/Handler. `RlsCoverageTests` vert.
*Écran (30/08/2026)* : registre `/integration-etatique` (onglet « Certificats de mutation ») avec révocation (motif obligatoire) ; délivrance et « Gérer l'IEN » sur la fiche élève ; page publique `/verifier/mutation/{token}` (statut seul, aucune donnée d'élève). Routes ajoutées : `GET /certificates`, `POST /certificates/{id}/revoke`.

**JGK-M07** [M] — Livret de compétences (PDF multi-pages)
`SkillsBookletModel` + `SkillsBookletDocument` (A4 portrait, **plusieurs pages admises**), échelle
NA/ECA/A/E dérivée du pourcentage de réussite (`SkillAcquisition`, seuils 40/60/85 — source unique).
*Dépend de* : `EvaluationStructure` (grille APC du bulletin). *Critères* : le nombre de colonnes de
périodes est **lu sur la configuration**, jamais codé en dur ; une case vide signifie « non évaluée »
et **jamais** « non acquis » ; la légende des abréviations est obligatoire (le livret est d'abord
destiné à la famille).
*Livré (30/08/2026)* : `GetSkillsBookletPdfQuery`/Handler (fusionne les grilles APC trimestre par trimestre via `EvaluationStructureBuilder`), `SkillsBookletDocument` (A4 portrait multi-pages), route `GET /api/v1/state-integration/students/{id}/skills-booklet`, `SkillAcquisition` (seuils 40/60/85). Tests `SimenComplianceTests` : seuils, null si pas de note / barème nul. *Écran (30/08/2026)* : le livret se télécharge depuis la fiche élève (route `GET /state-integration/students/{id}/skills-booklet`). Pas d'onglet dédié — c'est une pièce jointe au dossier, comme le bulletin.

---

## Module P — Cahier de texte & Vie pédagogique

> Numérotation volontairement discontinue après M (N/O réservés à d'autres chantiers non encore
> rédigés). Ce module ne porte pour l'instant que JGK-P04 ; il n'y a pas de P01-P03 à chercher
> ailleurs.

**JGK-P04** [M] — Cahier de texte / Journal de classe
Journal pédagogique tenu par matière et par classe : chaque séance donne lieu à une entrée
(date, titre, contenu résumé, devoirs éventuels + date de rendu) saisie par l'Enseignant qui a
effectivement assuré le cours. Table tenant `class_journal_entries` (`SchoolId`, `ClassroomId`,
`SubjectId`, `TeacherId`, `SessionDate`, `Topic`, `Content`, `Homework` nullable, `HomeworkDueDate`
nullable, `RowVersion`/`xmin`, soft delete). CQRS : `CreateClassJournalEntryCommand`,
`UpdateClassJournalEntryCommand`, `DeleteClassJournalEntryCommand` (soft delete), `GetClassJournalQuery`
(paginé, filtrable par classe/matière/période).

*Rôles* : écriture réservée à l'Enseignant **titulaire du créneau** (vérifié contre `ScheduleSlot` —
un enseignant ne peut journaliser une séance qu'il n'a pas au planning) ; Directeur/Secrétariat/
Surveillant en lecture seule sur toutes les entrées de l'école (contrôle pédagogique, même trio
d'oversight que `ParentSummonsController`) ; **aucun accès Parent/Élève** (module Portails hors
périmètre V1, AGENTS.md — ne pas ajouter de route de consultation ouverte à un tiers non-personnel).

*Règle métier* : une entrée n'est modifiable **librement par son auteur** que dans les 15 jours
suivant `SessionDate` ; passé ce délai, seul Directeur/Secrétariat peut corriger, et la correction est
historisée (même esprit que la règle #4 Finance : on ne réécrit pas silencieusement un historique
pédagogique). Refus : `403` (rôle non concerné), `409` (créneau non planifié pour cet enseignant à
cette date/classe/matière), `422` (`SessionDate` future — on journalise ce qui a été fait, jamais un
programme prévisionnel).

*Dépend de* : JGK-C02 (Classes), JGK-C03 (Matières), module Emploi du temps existant (`ScheduleSlot`,
livré — voir `ACTIVE_CONTEXT.md` §Pédagogie). *Critères* : un Enseignant sans créneau sur
(classe, matière, date) ne peut pas créer d'entrée (409, message nommant le créneau attendu) ; une
entrée passée à J+16 est refusée en écriture pour son auteur (403) mais reste modifiable par le
Directeur, avec trace de correction ; le Global Query Filter + la RLS isolent `class_journal_entries`
par école (test `MultiTenant`, table ajoutée à `TenantTables`) ; aucune route n'est accessible sans
authentification, et aucun rôle Parent/Élève n'existe dans l'énumération des rôles autorisés.

*Hors périmètre de ce ticket* (à traiter séparément si besoin futur) : export PDF du cahier de texte
pour l'inspection, rappel automatique si aucune entrée n'a été saisie pour un créneau planifié.

---

## Correctifs

**JGK-T01** [H] — Retrait de matière enseignant (privilège SQL vs soft-delete)
**Statut : livré (30/08/2026).** Décocher une matière sur la fiche enseignant échouait en 500
(`42501: permission denied for table teacher_subjects`) : `UpdateTeacherCommandHandler` retire une
qualification par un `DELETE` physique, or `AddTeachers` n'accordait au rôle applicatif que
`SELECT, INSERT, UPDATE`. **Option A retenue** — micro-migration `GrantDeleteOnTeacherSubjects` qui
accorde `DELETE` sur cette table de liaison (`Down` : `REVOKE`). Justification : `teacher_subjects`
n'est qu'un lien « qualifié pour », sans valeur d'audit ; le `DELETE` est déjà accordé à des tables
de même nature (`refresh_tokens`, `classrooms`, `AddPayrollAndTax`, `AddScheduleAndDisbursements`).
Les tables à donnée métier historisée (élèves, notes, paiements) restent sans `DELETE` — l'invariant
de la règle #6 pour celles-là est intact. `TeacherSubjectConfiguration` documente l'écart.
*Critères vérifiés* : `TeacherSubjectUnassignmentTests.cs` (catégorie `MultiTenant`) — avec le rôle
BRIDÉ, retirer une matière ne lève plus rien ET la ligne a disparu physiquement (contrôle SQL brut,
filtres EF hors jeu : un soft-delete silencieux ferait échouer ce second contrôle). Suite Teacher
d'intégration : 14/14. `dotnet build` 0 erreur ; unitaires 1004/1004.

**JGK-T02** [H] — `TagHelperContext.Items` ne restitue pas le contenu d'un Tag Helper enfant à son parent
**Statut : documenté (03/09/2026), cause racine non corrigée — 1 écran sur 22 contourné ponctuellement
(03/09/2026, commit `da8a3d8`).** `<modal-subtitle>`, `<modal-title>`, `<modal-footer>`
(`ModalShellTagHelper`) et `<stat-hint>` (`StatCardTagHelper`) communiquent avec leur parent via
`TagHelperContext.Items`, en s'appuyant sur le patron documenté (et attendu) de partage par référence
tout au long de l'arborescence d'une vue. Vérifié par instrumentation directe que ce n'est pas le cas
sur cet environnement (SDK `10.0.302` **et** `9.0.315`, testés tous les deux) : l'enfant et le parent
reçoivent chacun une instance différente du dictionnaire — le contenu écrit par l'enfant n'atteint
jamais le parent. Défaut silencieux (zéro erreur, ni serveur ni console) : le sous-titre ou l'indication
disparaît simplement du rendu. **21 fichiers de vue restent touchés** (22 d'origine), dont
`_Layout.cshtml` (modale universelle « Accès refusé », partagée par toute l'application). Détail
complet, preuve de diagnostic et pistes de résolution : `docs/technical-debt/taghelper-context-issue.md`.
*Contournements déjà en place, non généralisés* : `ReceiptA5TagHelper` (reçu A5 de `/caisse` et
`/inscriptions`) évite le patron défaillant par un découpage de contenu par marqueurs HTML plutôt que
des Tag Helpers enfants — voir sa doc XML. `Views/ParentSummons/Index.cshtml` remplace ses deux
`<modal-subtitle>` par un `<div>` simple portant les mêmes classes (même esprit, technique plus légère
pour un cas à une seule ligne) — voir « Progrès » dans le document technique.
*Dépend de* : aucune. *Critères d'acceptation pour la clôture* : sur les 21 fichiers restants listés
dans `docs/technical-debt/taghelper-context-issue.md`, le sous-titre/titre/pied/indication concerné
s'affiche à l'écran ; `npm test` et la suite .NET complète restent au vert.

---

## Récapitulatif de dépendances (ordre d'implémentation conseillé)

```
A01 → A02 → A03 → A04 → { A05, A06, B01 }
B01 → B02 → { C01 → C02 → C03, B03 }
C02 → D01 → { D02 → D05, D03 → D04 }
{ D01, F01 } → E01 → { E02, E03 }
F01 → F02 → { F03, F04 → F05 }
{ C03, D01 } → G01 → G02 → G03
Transverse (dès A04) : H01
A04 → { B01 → I03 } → I04 → I05 → I06 → I07
I01 → I02
I01 → I03
{ C01, C02, D01 } → J01 → { J02 → J04, J03 → J05, J07 }
J02 → J06
Paie/Pointage existants (EmployeeContract, FichePaie, TeacherHourRecord, ScheduleSlot) → K01
EmployeeContract → K02
F02 → L01
{ L01, L02 } → L03
D01 → M05 → M01 → { M02 → M03, M06 }
{ C01, D03, Infrastructures } → M04
Grille APC (EvaluationStructure) → M07
{ C02, C03, ScheduleSlot } → P04
```
