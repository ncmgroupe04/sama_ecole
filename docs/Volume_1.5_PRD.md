# SAMA ECOLE

# VOLUME 1.5 — Product Requirements Document (PRD)

**Version :** 2.1
**Statut :** Validé — remplace la version 1.0
**Changement de la v2.0 :** produit repositionné comme plateforme SaaS en ligne dès la V1 (voir Volume 0 v2.0). Les exigences non fonctionnelles liées au mode hors connexion et à la compatibilité multi-SGBD sont retirées.

**Changements de la v2.1 (27/07/2026) — réalignement stratégique du périmètre V1.** Le périmètre réellement livré avait divergé du périmètre écrit, dans les deux sens ; cette version acte l'écart au lieu de le laisser courir :
- **Élargissement du périmètre V1** (§3, §7, §8) : huit modules construits après la rédaction du MVP initial y entrent officiellement — Paie, Caisse, Trésorerie, Fiscalité, Discipline & Convocations, Infrastructures, Documents administratifs, Emploi du temps & Pointage — ainsi que les notifications SMS/WhatsApp sortantes, annoncées V2 et livrées en V1.
- **Report du Portail Parents & Élèves + messagerie en V3** (§8.1) : le Volume 1 §13 sort du périmètre V1. La V1 couvre la communication **sortante** (SMS/WhatsApp/e-mail + documents remis), pas la consultation en libre-service par les familles.
- **Report de l'export global « Exporter mes données »** en version suivante (§3, §7, §9).

---

## Table des matières

1. Présentation du produit
2. Utilisateurs
3. Modules du produit
4. Parcours utilisateurs
5. User stories
6. Priorisation des fonctionnalités
7. MVP (Version 1)
8. Roadmap produit
9. Critères d'acceptation
10. Exigences non fonctionnelles

---

## 1. Présentation du produit

### 1.1 Contexte

De nombreux établissements scolaires utilisent encore des cahiers, des fichiers Excel ou plusieurs logiciels distincts pour gérer les inscriptions, les notes, les paiements et les bulletins, ce qui entraîne erreurs, pertes d'information et charge administrative importante. Sama Ecole centralise ces activités dans une seule application accessible en ligne.

### 1.2 Vision

Créer le logiciel de gestion scolaire de référence au Sénégal, opéré comme une plateforme SaaS unique, capable d'évoluer vers d'autres pays d'Afrique de l'Ouest sans changement d'infrastructure.

### 1.3 Mission

Permettre à chaque établissement scolaire de gérer efficacement son administration, sa pédagogie et ses finances depuis n'importe quel navigateur connecté à Internet, avec une continuité de service assurée par une infrastructure cloud centralisée.

### 1.4 Objectifs stratégiques

- Automatiser les tâches répétitives.
- Réduire les erreurs de saisie.
- Produire rapidement les documents administratifs.
- Offrir une interface simple et moderne, accessible sur tout poste connecté.
- Garantir la sécurité et l'isolation des données de chaque établissement.
- Permettre l'onboarding de nouvelles écoles sans intervention technique lourde.

### 1.5 Valeur ajoutée

- Accessible partout, sans installation, mise à jour automatique et transparente pour toutes les écoles simultanément.
- Adaptation aux réalités des écoles sénégalaises (frais, matricules, mentions, format de date).
- Architecture multi-écoles dès l'origine, pensée pour le long terme.
- Aucune dépendance à un poste, un technicien ou une sauvegarde locale.

---

## 2. Utilisateurs

Six profils — détail des responsabilités et permissions au Volume 0 §0.9 et Volume 7.

| Profil | Rôle |
|---|---|
| Super Admin | Administration technique de la plateforme (abonnements, activation/suspension d'écoles, mises à jour) |
| Directeur | Administrateur fonctionnel de l'établissement |
| Secrétariat | Inscriptions, réinscriptions, dossiers élèves, classes |
| Finance | Paiements, dépenses, reçus, paie, caisse, trésorerie, fiscalité, statistiques financières |
| Enseignant | Notes, appréciations, bulletins, absences, ses propres créneaux d'emploi du temps |
| Surveillant | Vie scolaire : appel, retards, billets d'entrée/sortie, discipline, convocations, pointage des enseignants |

> Les rôles **Parent** et **Élève** n'existent pas en V1 : le portail qui les porterait est reporté en V3 (§8.1).

---

## 3. Modules du produit

- Tableau de bord
- Paramètres de l'établissement
- Gestion des utilisateurs
- Gestion des années scolaires
- Gestion des niveaux et classes
- Gestion des matières
- Gestion des enseignants
- Gestion des élèves
- Inscriptions et réinscriptions
- Finance et dépenses
- Notes et bulletins
- Rapports et statistiques
- Journaux d'activité (audit)
- Gestion des abonnements (Super Admin)
- Inscription self-service des établissements (formulaire public + validation Super Admin)
- Paiement des abonnements (Mobile Money, virement, carte)
- **Paie & bulletins de salaire** (contrats, calcul IPRES/CSS/CFCE/BRS, attestation de travail)
- **Caisse & journal de caisse** (sessions, écart de caisse, bordereau)
- **Trésorerie & décaissements** (vision consolidée, rapport financier + export `.xlsx`)
- **Fiscalité & TVA** (déclarations périodiques, PDF officiel)
- **Discipline & convocations des parents** (registre, PV, avis de convocation)
- **Infrastructures** (bâtiments & salles)
- **Documents administratifs officiels** (8 PDF)
- **Emploi du temps & pointage des enseignants**
- **Notifications SMS/WhatsApp sortantes** vers les parents (formule Premium)

> Le module « Sauvegardes » n'est plus un module utilisateur : en environnement cloud, les sauvegardes sont automatisées côté infrastructure (voir Volume 9).
>
> **« Exporter mes données » — acté pour la version suivante (27/07/2026).** Le bouton d'export **global** de l'établissement, un temps envisagé comme substitut à la « sauvegarde manuelle locale », **ne fait pas partie de la V1** : il n'a jamais été implémenté, et sa route `GET /api/v1/exports/school-data` est marquée reportée au Volume 4 §11.
>
> Ce report est assumé plutôt que subi : en V1, les besoins réels — transmettre au comptable, archiver, justifier auprès d'un tiers — sont couverts par des **exports par domaine** déjà livrés, dont le format est directement exploitable par leur destinataire (`.xlsx` comptable, PDF officiels), là où une archive globale « toutes les données de l'école » n'a pas de destinataire clair. Sont livrés : rapport financier consolidé `.xlsx`, export des présences, import/export Excel des notes, et les documents officiels PDF. La sauvegarde intégrale de la base reste une responsabilité d'infrastructure (Volume 9), jamais une action utilisateur.

---

## 4. Parcours utilisateurs

**Directeur (nouvel établissement) :** Formulaire d'inscription public → Attente de validation Super Admin → Email d'activation → Connexion → Paiement du premier abonnement → Paramétrage de l'établissement → Création de l'année scolaire → Création des utilisateurs.

**Directeur (établissement déjà actif) :** Connexion en ligne → Paramétrage de l'établissement → Création de l'année scolaire → Création des utilisateurs → Validation.

**Secrétaire :** Connexion → Inscription → Affectation à une classe → Impression du reçu.

**Enseignant :** Connexion → Sélection de la classe → Saisie des notes → Génération du bulletin.

**Finance :** Connexion → Encaissement → Impression du reçu → Consultation des statistiques.

**Super Admin :** Connexion à la console d'administration → Liste des demandes d'inscription en attente → Revue d'une demande → Approbation (création automatique établissement + compte Directeur + abonnement `AwaitingPayment`) ou Rejet.

---

## 5. User stories (exemples représentatifs)

- En tant que Directeur, je veux créer une nouvelle année scolaire afin de préparer la prochaine rentrée sans perdre les données des années précédentes.
- En tant qu'enseignant, je veux que le système applique automatiquement la notation sur 10 ou sur 20 selon le niveau de la classe.
- En tant que responsable Finance, je veux consulter l'historique des dépenses et l'imprimer.
- En tant que Directeur, je veux être alerté avant l'expiration de mon abonnement pour ne jamais être bloqué sans prévenir mon équipe.
- En tant que Super Admin, je veux activer une nouvelle école en moins de 10 minutes sans intervention technique sur site.

---

## 6. Priorisation des fonctionnalités

- **Critique** : indispensable au fonctionnement (ex. authentification, inscriptions, notes, bulletins, paiements).
- **Haute** : importante pour la première version (ex. statistiques financières de base, import de masse).
- **Moyenne** : prévue après le lancement (ex. paramétrage financier avancé, listes d'attente).
- **Faible** : amélioration future (ex. portail parents, BI avancée).

---

## 7. MVP (Version 1)

Le MVP est le périmètre **verrouillé** de la première mise en production. Toute fonctionnalité listée en dehors de ce chapitre est **Post-MVP par défaut**, quel que soit son niveau de détail dans le Volume 1 — le niveau de détail d'une spécification n'implique pas son inclusion dans la V1.

**Inclus dans le MVP :**

- Authentification et gestion des utilisateurs (6 rôles, dont Surveillant), suspension/blocage.
- Élèves, enseignants, classes, matières (CRUD complet + fiches détaillées).
- Inscriptions, réinscriptions, contrôle des places, documents (fiche + reçu).
- Paiements, dépenses, synchronisation Inscription ↔ Finance, reçus.
- Notes et génération de bulletins (format A5, notation /10 ou /20, mentions).
- Statistiques financières de base (tableau de bord, taux de recouvrement).
- Paramètres de l'établissement (matricules, dates, notation, logo/cachet).
- Gestion des abonnements multi-écoles (Super Admin) et isolation des données.
- Inscription self-service (formulaire public + validation Super Admin) — voir Volume 1 §11.5.
- Paiement réel des abonnements via agrégateur (Mobile Money, virement, carte) — voir Volume 1 §11.6.
- Journaux d'audit sur les actions critiques (finance, notes, utilisateurs).

**Ajoutés au périmètre V1 en cours de route, livrés et en production (état au 27/07/2026) :**

Ces modules ne figuraient pas au MVP initial ci-dessus. Ils ont été construits et livrés au fil des sprints, et sont désormais **dans le périmètre V1** — le MVP « verrouillé » a donc bougé, et ce chapitre l'acte plutôt que de laisser l'écart s'installer :

- Paie & bulletins de salaire — Volume 1 §14.
- Caisse & journal de caisse — Volume 1 §15.
- Trésorerie & décaissements, rapport financier consolidé + export `.xlsx` — Volume 1 §16.
- Fiscalité & TVA — Volume 1 §17.
- Discipline & convocations des parents — Volume 1 §18.
- Infrastructures (bâtiments & salles) — Volume 1 §19.
- Documents administratifs officiels (8 PDF) — Volume 1 §20.
- Emploi du temps & pointage des enseignants — Volume 1 §21.
- Notifications **SMS et WhatsApp sortantes** vers les parents — Volume 1 §13.7. Elles étaient annoncées « V2 et suivantes » : elles sont livrées en V1, en formule Premium.

**Explicitement Post-MVP (V1.1 et suivantes) :**

- Import de masse Excel/CSV **des enseignants** (V1.1). *(L'import des **élèves** et l'import/export Excel des **notes** sont livrés en V1.)*
- Liste d'attente automatisée avec notification (V1.1).
- Paramétrage financier avancé (application en masse par niveau, simulation de revenus détaillée) (V1.1).
- **Export global « Exporter mes données »** — acté pour la version suivante (voir §3 et Volume 4 §11).
- **Portail Parents & Élèves et messagerie** (Volume 1 §13, sous-sections 13.1 à 13.6) — **reporté en V3**, voir §8.
- Application mobile, BI/IA (V2 et suivantes, voir Volume 0 §0.11).

---

## 8. Roadmap produit

| Version | Contenu |
|---|---|
| V1 | Plateforme SaaS en ligne, multi-écoles dès le lancement. MVP ci-dessus **+** Paie, Caisse, Trésorerie, Fiscalité, Discipline & Convocations, Infrastructures, Documents administratifs, Emploi du temps & Pointage, notifications SMS/WhatsApp sortantes |
| V1.1 | Import de masse, listes d'attente, paramétrage financier avancé, **export global « Exporter mes données »** |
| V2 | Application mobile, intégration paiement mobile |
| **V3** | **Portail Parents & Élèves + messagerie** (Volume 1 §13.1 à 13.6) — consultation en ligne par les familles, comptes Parent/Élève, annonces de classe et messages enseignant↔parent |
| V4 | Statistiques avancées (BI), haute disponibilité multi-région |

### 8.1 Portail Parents & Élèves — pourquoi la V3 et pas la V1

Le chapitre 13 du Volume 1 est spécifié en détail, ce qui a longtemps laissé croire qu'il était engagé pour la V1. Il ne l'est pas, et ne l'a jamais été au sens du §7 : *le niveau de détail d'une spécification n'implique pas son inclusion dans la V1*.

**Ce que la V1 couvre à la place — la communication sortante.** L'établissement pousse l'information vers les familles par **SMS**, **WhatsApp** et **e-mail** (absences, retards, encaissements, relances d'impayés), et par les **documents remis en main propre** (convocation, avis d'échéance, bulletin, reçus). Le besoin premier des parents — *être informé* — est donc servi en V1. Ce qui est reporté, c'est le canal *entrant* : un compte, une session, une consultation en libre-service.

**Ce que le report économise.** Ouvrir le portail ajoute deux rôles (`Parent`, `Eleve`), une population d'utilisateurs externes à l'établissement, et surtout un **second niveau d'isolation** : aujourd'hui la sécurité sépare les écoles entre elles ; le portail impose de séparer les familles *à l'intérieur* d'une même école (un parent ne voit que ses enfants rattachés). C'est un modèle de sécurité distinct, à construire et à tester pour lui-même — le livrer à moitié serait le pire des résultats. La V3 lui donne le sprint dédié qu'il exige.

**Contraintes à tenir jusque-là :** ne pas ajouter les rôles `Parent`/`Eleve`, ne pas exposer d'endpoint de consultation à un tiers non-personnel de l'établissement, et ne pas traiter la convocation de parent (Volume 1 §18) comme une amorce de portail — c'est un document interne.

---

## 9. Critères d'acceptation (exemples)

- Une inscription n'est validée que si toutes les informations obligatoires sont renseignées et le dossier documentaire est au moins « En attente » (pas « vide »).
- Un bulletin est généré automatiquement selon les règles de calcul du niveau/classe concerné, sans débordement de page.
- Un rapport financier consolidé peut être régénéré à tout moment sur une période donnée, et son export `.xlsx` porte exactement les mêmes bornes et les mêmes montants que le rapport affiché à l'écran. *(L'export **global** de l'établissement est reporté — voir §3 et §8 ; ce critère porte donc sur les exports par domaine effectivement livrés.)*
- Un document officiel (reçu, bulletin, certificat, PV, convocation, attestation) s'imprime sans débordement de page, quel que soit le poste depuis lequel il est édité.
- Un enseignant ne peut ni créer, ni modifier, ni supprimer un créneau d'emploi du temps qui n'est pas le sien.
- Le système applique automatiquement la bonne notation selon le niveau scolaire, sans intervention manuelle de l'enseignant.
- Une école ne peut techniquement récupérer aucune donnée appartenant à une autre école, même en cas d'erreur applicative (garantie au niveau base de données, testée en Volume 8).

---

## 10. Exigences non fonctionnelles

- Temps de réponse inférieur à 2 secondes pour les opérations courantes, mesuré depuis Dakar sur une connexion 4G standard.
- Disponibilité cible : **99,5 %** en V1 (objectif SaaS mono-région), avec plan d'amélioration vers 99,9 % en V2+ (voir Volume 9).
- Interface moderne, responsive, utilisable sur ordinateur, tablette et smartphone via navigateur.
- Résilience aux coupures réseau courtes côté client (conservation des saisies en cours, voir Volume 0 §0.8) — **et non plus fonctionnement hors connexion complet**, qui est retiré du périmètre du produit.
- Sécurité des données et journalisation systématique des actions sensibles.
- Sauvegardes automatiques côté infrastructure, avec restauration testée régulièrement (Volume 9).
- Base de données unique : **PostgreSQL**, aucune compatibilité multi-SGBD à maintenir.
- Architecture multi-tenant permettant l'ajout d'une nouvelle école sans impact sur les écoles existantes ni interruption de service.

**Fin du Volume 1.5.**
