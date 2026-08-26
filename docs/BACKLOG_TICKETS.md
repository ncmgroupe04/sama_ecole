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

---

## Module F — Finance

**JGK-F01** [C] — Paramétrage des frais par classe
Montant standard + application en masse + exceptions par classe, historique des modifications.
*Dépend de* : JGK-C02.

**JGK-F02** [C] — Enregistrement des paiements
`POST /finance/payments` avec verrouillage optimiste (`row_version`), génération de reçu.
*Critères* : deux paiements concurrents sur le même solde ne produisent jamais un état incohérent — test de concurrence obligatoire.

**JGK-F03** [H] — Dépenses
`GET/POST /finance/expenses`.

**JGK-F04** [H] — Tableau de bord financier
`GET /finance/dashboard` (encaissé jour/mois/année, solde dû, taux de recouvrement).
*Dépend de* : JGK-F02.

**JGK-F05** [M] — Rapports financiers exportables
Journal de caisse, rapport mensuel/annuel en PDF.

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
Agrège `TeacherHourRecord` (rapproché de `ScheduleSlot` pour signaler les écarts) et **pré-remplit** `HoursWorked` dans `GenerateFichePaieCommand` — valeur reste éditable, tout ajustement est historisé. La validation humaine reste seule à clôturer la fiche de paie (arbitrage acté : n'abroge pas la saisie manuelle, voir Volume_1 §14.3 amendé).
*Dépend de* : module Paie existant (`EmployeeContract`, `FichePaie`, `TeacherHourRecord`, `ScheduleSlot`). *Critères* : générer une fiche sans jamais avoir consulté la suggestion reste possible (non bloquant) ; un test vérifie qu'un écart entre heures pointées et créneaux planifiés est signalé, pas rejeté.

**JGK-K02** [M] — Moyens de paiement RH (Bancaire/Wave/Orange Money)
Nouveau champ moyen de paiement + coordonnées sur `EmployeeContract`, distinct du `PaymentMethod` finance élève.
*Dépend de* : `EmployeeContract` existant. *Critères* : coordonnées bancaires/mobile money jamais journalisées en clair dans un log applicatif (Volume 7).

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
*Dépend de* : JGK-L01, JGK-L02. *Critères* : vérification manuelle (skill `run`) — coupure réseau pendant une saisie caisse ou un appel, reprise sans doublon ni message d'erreur trompeur, pas de trace locale de la transaction après fermeture de l'onglet.

---

## Récapitulatif de dépendances (ordre d'implémentation conseillé)

```
A01 → A02 → A03 → A04 → { A05, A06, B01 }
B01 → B02 → { C01 → C02 → C03, B03 }
C02 → D01 → { D02 → D05, D03 → D04 }
{ D01, F01 } → E01 → E02
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
```
