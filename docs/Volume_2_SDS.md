# JANGALEKAT

# VOLUME 2 — Software Design Specification (SDS)

**Version :** 2.0
**Statut :** Document de référence pour le développement — remplace la version 1.0
**Changements de cette version :** suppression du multi-SGBD et de l'Offline-First (voir Volume 0 v2.0) ; ajout d'un chapitre sur le modèle multi-tenant et le contrat de module.

---

## Table des matières

1. Présentation technique
2. Choix technologiques
3. Architecture logicielle
4. Contrat de module et architecture fonctionnelle
5. Flux et workflows métier critiques
6. Modèle de domaine (aperçu)
7. Renvoi vers les volumes complémentaires

---

## 1. Présentation technique

### 1.1 Objectif

Ce document définit l'architecture technique officielle de Jangalekat. Il constitue la référence pour développer, maintenir, faire évoluer et déployer l'application. Toute décision technique doit être conforme à ce document et au Volume 0.

### 1.2 Objectifs techniques

Le système doit être : modulaire, sécurisé, rapide, évolutif, testable, maintenable, **multi-tenant**, **cloud-native**.

> Les objectifs « Offline-First » et « Multi-base de données » de la version 1.0 sont retirés : l'application est en ligne par conception (Volume 0 §0.8) et repose sur un unique moteur, PostgreSQL (Volume 0 §0.8).

### 1.3 Public concerné

Développeurs, architectes logiciels, testeurs, intégrateurs, futurs mainteneurs.

---

## 2. Choix technologiques

### 2.1 Backend

**ASP.NET Core 9**, langage **C#**. Retenu pour ses performances, sa robustesse, sa gestion native de la sécurité et son écosystème d'hébergement cloud mature.

### 2.2 Frontend

**ASP.NET Core MVC + Razor Views** pour la V1 (back-office web), avec une **API REST découplée** (Volume 4) dès le départ, pour permettre une application mobile ou un frontend SPA sans réécriture du backend.

### 2.3 ORM

**Entity Framework Core**, avec le fournisseur **Npgsql** (PostgreSQL uniquement).

### 2.4 Base de données

**PostgreSQL**, unique moteur, unique environnement pour dev/staging/production. Aucune abstraction multi-SGBD à maintenir dans le code métier — ce qui simplifie considérablement la couche Infrastructure par rapport à la version 1.0 de ce document.

### 2.5 Bibliothèques

| Bibliothèque | Usage |
|---|---|
| Entity Framework Core (Npgsql) | Accès aux données |
| ASP.NET Core Identity | Authentification |
| QuestPDF | Génération de documents PDF |
| Chart.js | Graphiques côté client |
| ClosedXML | Import/export Excel |
| Serilog | Journalisation structurée |
| AutoMapper | Mapping Entité ↔ DTO |
| FluentValidation | Validation des DTO entrants |
| Hangfire ou équivalent | Tâches asynchrones (imports de masse, envoi d'emails) |

Toute bibliothèque additionnelle doit être validée par l'équipe technique avant intégration.

---

## 3. Architecture logicielle

### 3.1 Architecture retenue

**Clean Architecture**, inchangée par rapport à la v1.0 — c'est un choix qui reste pertinent indépendamment du pivot vers le cloud.

### 3.2 Organisation des projets

```
Jangalekat.sln
│
├── Jangalekat.Domain          → Entités, interfaces, règles métier, Value Objects (aucune dépendance externe)
├── Jangalekat.Application     → Cas d'utilisation, services applicatifs, DTO, validations, interfaces de repositories
├── Jangalekat.Infrastructure  → EF Core (Npgsql), repositories, migrations, services techniques (PDF, email, fichiers, sauvegarde)
├── Jangalekat.Web             → Contrôleurs MVC, vues Razor, contrôleurs API REST, authentification, fichiers statiques
├── Jangalekat.Shared          → Constantes, énumérations, helpers, extensions
├── Jangalekat.Tests           → Tests unitaires, intégration, fonctionnels (Volume 8)
└── Jangalekat.Tools           → Scripts, outils d'import/export, outils de maintenance
```

### 3.3 Règles fondamentales

- Une couche ne peut jamais accéder à une couche supérieure ; les couches internes (Domain) ne dépendent jamais des couches externes.
- Toutes les dépendances sont injectées via le conteneur d'Injection de Dépendances natif d'ASP.NET Core.
- Aucun accès direct à la base de données depuis un contrôleur : tout passe par un service applicatif.
- Toute logique métier réside dans `Jangalekat.Application`, jamais dans les contrôleurs ni dans les entités EF Core.

### 3.4 Diagramme global d'architecture

```
UTILISATEURS (navigateur — web ou mobile futur)
        │
        ▼
ASP.NET Core MVC + API REST (Présentation)
        │
        ▼
Couche Application (Services, cas d'utilisation)
        │
        ▼
Couche Domaine (Entités, règles métier, interfaces)
        │
        ▼
Infrastructure (Entity Framework Core / Npgsql)
        │
        ▼
PostgreSQL (base unique, multi-tenant, hébergée cloud)
```

### 3.5 Diagramme des composants (modules)

```
Jangalekat
├── Authentification & Comptes
├── Tableau de bord
├── Paramètres Établissement
├── Gestion des utilisateurs
├── Gestion des années scolaires
├── Élèves
├── Enseignants
├── Classes & Matières
├── Inscriptions & Réinscriptions
├── Notes & Bulletins
├── Finance
│   ├── Paiements
│   ├── Dépenses
│   ├── Statistiques
│   └── Rapports
├── Abonnements & Écoles (Super Admin)
├── Journaux d'activité (audit)
└── Administration technique
```

Chaque module est autonome et communique avec les autres exclusivement via des services applicatifs — jamais d'accès direct entre modules au niveau des données.

### 3.6 Principes d'architecture transverses

- **Modularité** — chaque module évolue sans modifier les autres.
- **Faible couplage** — communication uniquement via services applicatifs.
- **Haute cohésion** — une responsabilité claire par module.
- **Injection de dépendances** — aucune instanciation directe de service.
- **Indépendance du domaine par rapport à la base de données** — le domaine ne connaît pas EF Core.
- **Isolation multi-tenant systématique** — toute requête de données passe par un filtre `SchoolId` appliqué globalement (voir §4.3 et Volume 3).
- **Évolutivité** — toute nouvelle fonctionnalité s'ajoute sans réécrire les modules existants (règle d'or, Volume 0 §0.12).

---

## 4. Contrat de module et architecture fonctionnelle

### 4.1 Principe du contrat de module

Chaque module métier (Élèves, Inscriptions, Finance, Bulletins, etc.) est documenté par un **contrat** qui précise :

- ses responsabilités et ses limites (ce qu'il ne fait pas) ;
- les entités du domaine qu'il possède ;
- les services applicatifs qu'il expose aux autres modules ;
- les événements qu'il émet (ex. `InscriptionCréée`, `PaiementEnregistré`) ;
- les règles métier et invariants qu'il garantit.

Ce contrat permet de générer de façon cohérente le DDS (Volume 3), l'API (Volume 4) et les tests (Volume 8).

### 4.2 Exemple de contrat — Module Inscriptions

| Élément | Détail |
|---|---|
| Responsabilité | Créer et suivre le cycle de vie d'une inscription/réinscription, du dossier provisoire à l'inscription définitive |
| Entités possédées | `Enrollment`, `EnrollmentDocument`, `WaitingListEntry` |
| Services exposés | `CreateEnrollment()`, `ConfirmEnrollment()`, `TransferToWaitingList()` |
| Événement émis | `EnrollmentConfirmed` → consommé par le module Finance pour créer automatiquement la créance (voir §5.3) |
| Invariant garanti | Une inscription confirmée ne peut exister sans dossier au statut au moins « En attente » |

### 4.3 Filtre multi-tenant global

Tous les `DbContext` EF Core appliquent un **Global Query Filter** sur `SchoolId`, positionné automatiquement depuis le contexte de l'utilisateur authentifié (middleware de résolution de tenant). Ce filtre applicatif est une **défense en profondeur**, complémentaire — et non substitut — au Row-Level Security PostgreSQL défini au Volume 3. Les deux mécanismes doivent rester actifs simultanément.

---

## 5. Flux et workflows métier critiques

### 5.1 Flux d'une inscription

```
Secrétaire
   │
   ▼
Créer / sélectionner l'élève
   │
   ▼
Contrôle des informations obligatoires
   │
   ▼
Enregistrement définitif → génération transactionnelle du matricule (Volume 1 §2.1)
   │
   ▼
Création de l'inscription (statut : dossier "En attente" ou "Complet")
   │
   ▼
Événement EnrollmentConfirmed → transmission automatique du montant dû à Finance
   │
   ▼
Paiement (total ou partiel)
   │
   ▼
Impression du reçu
   │
   ▼
Élève inscrit
```

### 5.2 Flux des notes et bulletins

```
Enseignant
   │
   ▼
Sélection de la classe
   │
   ▼
Le système détecte automatiquement le système de notation de la classe (/10 ou /20)
   │
   ▼
Saisie des notes (validation de plage à la saisie : ex. 0-20)
   │
   ▼
Calcul automatique (totaux, moyennes, mentions)
   │
   ▼
Validation par l'enseignant → verrouillage de la saisie pour la période
   │
   ▼
Génération du bulletin PDF (QuestPDF, format A5)
```

### 5.3 Flux financier

```
Inscription confirmée
   │
   ▼
Montant défini par le secrétariat, transmis automatiquement à Finance (lecture seule pour Finance)
   │
   ▼
Encaissement (total ou échelonné)
   │
   ▼
Reçu généré
   │
   ▼
Journal comptable mis à jour
   │
   ▼
Statistiques et tableau de bord recalculés
```

### 5.4 Cas d'utilisation par rôle (synthèse)

| Rôle | Cas d'utilisation principaux |
|---|---|
| Super Admin | Gérer les abonnements, activer/suspendre une école, bloquer un utilisateur, superviser la plateforme |
| Directeur | Configurer l'établissement, créer une année scolaire, créer les utilisateurs, exporter les données |
| Secrétariat | Inscrire un élève, gérer un dossier, affecter une classe, imprimer un reçu |
| Enseignant | Saisir les notes, saisir les appréciations, générer les bulletins |
| Finance | Encaisser un paiement, enregistrer une dépense, imprimer un reçu, consulter les statistiques |

---

## 6. Modèle de domaine (aperçu)

Le modèle de domaine complet, entité par entité, est détaillé au Volume 3 (DDS). Les agrégats racines principaux sont :

- **School** (agrégat racine du multi-tenant — toute autre entité y est rattachée directement ou indirectement)
- **SchoolYear**
- **Student**, **Enrollment**
- **Teacher**, **ClassRoom**, **Subject**
- **Grade**, **ReportCard**
- **Payment**, **Expense**, **FeeSchedule**
- **User**, **Role**, **Permission**
- **Subscription** (agrégat racine côté Super Admin, hors périmètre `SchoolId`)

---

## 7. Renvoi vers les volumes complémentaires

- Schéma de données détaillé, stratégie multi-tenant (RLS) → **Volume 3 — DDS**
- Contrats REST, formats de requête/réponse → **Volume 4 — API Design Specification**
- Écrans, navigation, composants → **Volume 5 — UI/UX Design Specification**
- Conventions de code, Git, CI/CD → **Volume 6 — Development Guide**
- Authentification, autorisation, chiffrement, matrice de permissions → **Volume 7 — Security Architecture**
- Stratégie de tests → **Volume 8 — Test Strategy**
- Déploiement cloud, supervision, sauvegardes → **Volume 9 — Deployment & Operations Guide**

**Fin du Volume 2.**
