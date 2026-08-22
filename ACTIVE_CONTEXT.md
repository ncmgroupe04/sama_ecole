# ACTIVE_CONTEXT — Périmètre V1 de Sama Ecole

État de référence du périmètre livré, tenu à jour à chaque clôture de sprint. Ce fichier ne porte
**aucune règle** : les règles non négociables vivent dans `AGENTS.md`, la spécification fonctionnelle
dans `docs/Volume_1_Cahier_des_Charges.md`. Il répond à une seule question — *qu'est-ce qui est dans
la V1, et qu'est-ce qui n'y est pas ?*

**Dernière mise à jour : 27/07/2026** (sprint de finalisation, audit de conformité P0/P1).

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

Le socle V1 (Élèves, Inscriptions, Classes, Matières, Enseignants, Notes & Bulletins, Frais,
Présences, Surveillance générale, Abonnements & Facturation, Console Super Admin) est livré depuis
les sprints précédents.

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
