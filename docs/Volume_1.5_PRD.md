# SAMA ECOLE

# VOLUME 1.5 — Product Requirements Document (PRD)

**Version :** 2.0
**Statut :** Validé — remplace la version 1.0
**Changement principal :** produit repositionné comme plateforme SaaS en ligne dès la V1 (voir Volume 0 v2.0). Les exigences non fonctionnelles liées au mode hors connexion et à la compatibilité multi-SGBD sont retirées.

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

Cinq profils principaux — détail des responsabilités et permissions au Volume 0 §0.9 et Volume 7.

| Profil | Rôle |
|---|---|
| Super Admin | Administration technique de la plateforme (abonnements, activation/suspension d'écoles, mises à jour) |
| Directeur | Administrateur fonctionnel de l'établissement |
| Secrétariat | Inscriptions, réinscriptions, dossiers élèves, classes |
| Finance | Paiements, dépenses, reçus, statistiques financières |
| Enseignant | Notes, appréciations, bulletins, absences |

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

> Le module « Sauvegardes » n'est plus un module utilisateur : en environnement cloud, les sauvegardes sont automatisées côté infrastructure (voir Volume 9). Le Directeur conserve un bouton **Exporter mes données** (export complet de son établissement), qui remplace la notion de « sauvegarde manuelle locale ».

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

- Authentification et gestion des utilisateurs (5 rôles), suspension/blocage.
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

**Explicitement Post-MVP (V1.1 et suivantes) :**

- Import de masse Excel/CSV (V1.1).
- Liste d'attente automatisée avec notification (V1.1).
- Paramétrage financier avancé (application en masse par niveau, simulation de revenus détaillée) (V1.1).
- Application mobile, portail parents/élèves, SMS/WhatsApp, BI/IA (V2 et suivantes, voir Volume 0 §0.11).

---

## 8. Roadmap produit

| Version | Contenu |
|---|---|
| V1 | Plateforme SaaS en ligne, MVP ci-dessus, multi-écoles dès le lancement |
| V1.1 | Import de masse, listes d'attente, paramétrage financier avancé |
| V2 | Application mobile, intégration paiement mobile |
| V3 | Portail parents/élèves, notifications SMS/WhatsApp |
| V4 | Statistiques avancées (BI), haute disponibilité multi-région |

---

## 9. Critères d'acceptation (exemples)

- Une inscription n'est validée que si toutes les informations obligatoires sont renseignées et le dossier documentaire est au moins « En attente » (pas « vide »).
- Un bulletin est généré automatiquement selon les règles de calcul du niveau/classe concerné, sans débordement de page.
- Un export de données d'établissement peut être régénéré à tout moment et contient l'intégralité des données actives de l'école.
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
