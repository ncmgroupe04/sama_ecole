# SAMA ECOLE
## Documentation Technique et Fonctionnelle — Système de Gestion Scolaire

# VOLUME 0 — Vision Produit & Décisions d'Architecture

**Version :** 2.0
**Statut :** Validé — remplace intégralement la version 1.0
**Changement majeur de cette version :** abandon du modèle Offline-First / déploiement local, au profit d'une **application 100 % en ligne (Cloud SaaS) dès la version 1**.

---

## Table des matières

- 0.1 Présentation du projet
- 0.2 Vision du produit
- 0.3 Mission
- 0.4 Objectifs
- 0.5 Public cible
- 0.6 Problèmes résolus
- 0.7 Valeurs du logiciel
- 0.8 Décisions d'architecture (définitives)
- 0.9 Philosophie de gestion et rôles
- 0.10 Principes techniques transverses
- 0.11 Roadmap
- 0.12 Règle d'or du projet
- 0.13 Journal des décisions (changelog architectural)

---

## 0.1 Présentation du projet

**Sama Ecole** est un logiciel professionnel de gestion scolaire conçu pour répondre aux besoins des établissements d'enseignement du Sénégal, avec une architecture évolutive permettant une adaptation à d'autres pays d'Afrique de l'Ouest.

Le projet est développé comme une **plateforme SaaS multi-écoles accessible en ligne**, hébergée sur un serveur cloud, accessible par navigateur (et plus tard par application mobile) depuis n'importe quel poste connecté à Internet.

Le logiciel couvre :

- Administration et paramétrage
- Inscriptions et réinscriptions
- Élèves et enseignants
- Classes et matières
- Notes et bulletins
- Finance (frais, paiements, dépenses)
- Statistiques et rapports
- Sauvegardes automatisées côté serveur
- Gestion des abonnements et licences (multi-écoles)

## 0.2 Vision

Construire une solution de gestion scolaire :

- moderne ;
- simple à utiliser ;
- rapide ;
- fiable ;
- sécurisée ;
- accessible partout, à tout moment, depuis n'importe quel appareil connecté ;
- évolutive vers plusieurs pays et plusieurs milliers d'écoles.

L'objectif est que Sama Ecole puisse être utilisé aussi bien par une petite école primaire que par un groupe scolaire comprenant plusieurs établissements, sur une seule plateforme partagée.

## 0.3 Mission

Mettre à disposition des établissements scolaires un logiciel :

- simple à prendre en main ;
- accessible en ligne, sans installation lourde ;
- capable de fonctionner correctement même avec une connexion Internet instable (résilience réseau côté client) ;
- centralisé, pour permettre un support, une maintenance et des mises à jour continues sans intervention sur chaque poste.

## 0.4 Objectifs

- Réduire les tâches administratives.
- Automatiser les calculs scolaires et financiers.
- Produire rapidement les documents administratifs (reçus, bulletins, rapports).
- Réduire les erreurs humaines.
- Garantir la sécurité et la confidentialité des données de chaque établissement.
- Permettre l'ajout continu de nouvelles écoles sans impact sur les écoles existantes (élasticité SaaS).
- Offrir une continuité de service même en cas de coupure réseau ponctuelle côté utilisateur.

## 0.5 Public cible

- Écoles maternelles
- Écoles primaires
- Collèges
- Lycées
- Établissements mixtes
- Centres de formation
- Groupes scolaires multi-sites

## 0.6 Problèmes résolus

- Gestion manuelle des notes.
- Bulletins réalisés sous Excel.
- Difficulté de suivi des paiements.
- Perte de documents (papier, fichiers locaux non sauvegardés).
- Absence de statistiques fiables.
- Manque de traçabilité.
- Erreurs de calcul.
- Multiplication des fichiers et des versions.
- Dépendance à un poste ou un technicien unique pour la maintenance.

## 0.7 Valeurs du logiciel

Huit principes guident toutes les décisions :

1. Simplicité
2. Fiabilité
3. Rapidité
4. Sécurité
5. Évolutivité
6. Modularité
7. Traçabilité
8. Performance

---

## 0.8 Décisions d'Architecture (définitives)

> **Ces décisions remplacent celles du Volume 0 v1.0. Toute mention contraire dans un volume antérieur (SQLAlchemy, MySQL, mode local/LAN) est obsolète et doit être ignorée.**

### Backend

**ASP.NET Core 9** (Web API)

Pourquoi :
- Architecture robuste, typée, performante.
- Écosystème mature (Identity, EF Core, hébergement cloud natif).
- Excellent support multi-tenant.

### ORM

**Entity Framework Core** — et uniquement Entity Framework Core.

> Toute référence à SQLAlchemy (Python) dans une version antérieure du cahier des charges est une erreur de cohérence et est supprimée. Sama Ecole est un projet .NET de bout en bout.

### Base de données

**PostgreSQL — unique moteur de base de données, dès le premier jour, pour tous les environnements** (développement, staging, production).

Ce point change radicalement l'architecture précédente :

| | Ancienne approche (abandonnée) | Nouvelle approche (retenue) |
|---|---|---|
| Local | SQLite | — (supprimé) |
| Intermédiaire | MySQL | — (supprimé) |
| Final | PostgreSQL | **PostgreSQL dès le jour 1, environnement unique** |

Pourquoi ce choix :
- Une seule base à maîtriser, tester et administrer élimine tout risque de migration future ratée entre trois moteurs différents.
- PostgreSQL gère nativement le **Row-Level Security (RLS)**, indispensable à l'isolation multi-tenant (voir Volume 3).
- PostgreSQL supporte des volumes de plusieurs centaines d'écoles et plusieurs millions de lignes sans changement d'architecture.
- EF Core avec le fournisseur `Npgsql` est mature et bien supporté.

Il n'y a donc plus de notion de « version locale », « version LAN » ou « version SaaS » de la base de données : **il n'existe qu'une seule base, hébergée, partagée par toutes les écoles, isolée logiquement par établissement**.

### Architecture logicielle

**Clean Architecture** (Domain / Application / Infrastructure / API), inchangée par rapport à la v1.0.

Pourquoi :
- Séparation claire des responsabilités.
- Testabilité.
- Indépendance du domaine métier par rapport à la base de données ou au framework web.

### Authentification & Autorisation

**ASP.NET Core Identity** + **JWT** (jetons d'accès/refresh) pour les clients web et mobile, + **Policy-Based Authorization** pilotée par une matrice de permissions (Volume 7).

### Multi-tenant

**Stratégie retenue : base de données unique, schéma partagé, isolation par `SchoolId` + Row-Level Security PostgreSQL.**

Alternatives écartées et pourquoi :
- *Base de données par école* : trop coûteux à opérer et à faire évoluer au-delà de quelques dizaines d'écoles.
- *Schéma par école* : complexifie les migrations EF Core sans bénéfice suffisant à l'échelle visée (centaines, potentiellement milliers d'écoles).

Détails complets : Volume 3, chapitre « Stratégie Multi-Tenant ».

### Génération PDF

**QuestPDF** (inchangé).

### Graphiques

**Chart.js** côté client (inchangé).

### Identifiants

**UUID (v7 de préférence, triable chronologiquement)** sur toutes les entités.

### Frontend

**ASP.NET Core MVC avec Razor** pour la V1 (back-office web), avec une API REST découplée dès le départ pour permettre une future application mobile ou un frontend SPA sans réécriture du backend.

### CSS / Design system

**Tailwind CSS** (décision définitive, tranchée en juillet 2026 — voir Journal des décisions D-13). Choisi plutôt que Bootstrap car purement utilitaire : il permet d'implémenter fidèlement le design system du Volume 5 sans avoir à surcharger des styles de composants imposés par défaut. Compilation via Tailwind CLI/PostCSS, intégrée à `SamaEcole.Web` (voir `docs/REPO_STRUCTURE.md`).

### Hébergement & Déploiement

**Cloud dès le jour 1** :
- Conteneurisation **Docker**.
- Hébergement **Linux (Ubuntu LTS)**.
- Reverse proxy **Nginx** + certificats TLS automatiques (Let's Encrypt / gestion managée).
- Orchestration **Kubernetes** prévue comme évolution (pas nécessaire au lancement, mais l'architecture ne doit pas s'y opposer).
- Base de données PostgreSQL en mode **managé** (sauvegardes automatiques, réplication, haute disponibilité) recommandé dès que le budget le permet.

### Résilience réseau côté client (remplace l'ancien « Offline-First »)

L'application n'est plus une application locale avec synchronisation différée. Elle est **en ligne par conception**. Pour rester utilisable dans un pays où la connectivité peut être instable, on retient :

- Une interface qui **conserve les saisies en cours en mémoire locale du navigateur (état de formulaire)** pendant une coupure courte, avec nouvel envoi automatique à la reconnexion.
- Des **temporisations et messages clairs** en cas de perte de connexion (« Connexion perdue — nouvelle tentative en cours »), plutôt qu'une perte silencieuse de données.
- Aucune base de données locale, aucune synchronisation différée, aucune résolution de conflits multi-postes : **la source de vérité est toujours le serveur**, ce qui élimine une classe entière de problèmes (conflits d'écriture concurrents, désynchronisation, corruption locale) présents dans l'ancienne approche Offline-First.
- **Renvoi automatique, concrétisé (26/08/2026, ticket JGK-L02) :** `network-guard.js` expose `submitWithRetry`, qui retente une écriture métier avec backoff exponentiel PENDANT que l'onglet reste ouvert, sans jamais rien persister de la tentative — si l'onglet se ferme avant confirmation, la saisie est perdue par conception, c'est la frontière qui garde ce mécanisme hors d'« offline-first ». Sur l'encaissement de caisse, un retry est rendu sûr par une **clé d'idempotence** générée côté client à l'ouverture du formulaire (ticket JGK-L01, `Payment.IdempotencyKey`) : le serveur rejoue le résultat déjà produit plutôt que de créer un second paiement. Le pointage d'absences n'a pas eu besoin d'un mécanisme dédié : sa clé métier naturelle (classe, matière, date, créneau) joue déjà ce rôle — un retry sur le même appel se heurte à la contrainte d'unicité déjà en place, que l'appelant interprète comme une confirmation plutôt que comme une erreur.

> Une évolution future en PWA avec cache de lecture (consultation hors-ligne de données déjà chargées) reste possible sans remettre en cause ce principe, et est notée dans la roadmap (Volume 13, hors périmètre V1).

---

## 0.9 Philosophie de gestion et rôles

Le logiciel ne doit pas dépendre d'un développeur ou d'un technicien pour fonctionner au quotidien. Chaque établissement doit être autonome dans son usage courant.

| Rôle | Responsabilité |
|---|---|
| **Super Admin** | Administrateur technique de la plateforme : licences, abonnements, activation/suspension d'écoles, supervision globale, mises à jour. Ne gère jamais la pédagogie d'une école. |
| **Directeur** | Administrateur fonctionnel de son établissement : paramètres, années scolaires, utilisateurs, mentions, bulletins, sauvegardes, paramètres pédagogiques et financiers. |
| **Enseignant** | Notes, appréciations, bulletins, absences de ses classes. |
| **Finance** | Paiements, dépenses, reçus, rapports et statistiques financières. |
| **Secrétariat** | Élèves, inscriptions, réinscriptions, documents administratifs. |

La matrice détaillée des permissions par rôle et par module est spécifiée au Volume 7 (Sécurité).

## 0.10 Principes techniques transverses

Toutes les données doivent être :

- **historisées** (aucune suppression physique des données sensibles — utilisation de suppression logique `IsDeleted`/`DeletedAt`) ;
- **auditables** (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` sur chaque table) ;
- **sauvegardables** automatiquement côté serveur, sans action requise de l'utilisateur ;
- **restaurables** avec vérification d'intégrité ;
- **isolées par établissement** (`SchoolId` + RLS, sans exception).

## 0.11 Roadmap

| Version | Contenu |
|---|---|
| **V1** | Plateforme SaaS en ligne, mono-région, fonctionnalités MVP (Volume 1.5, Chapitre 7), multi-écoles dès le lancement. |
| **V1.1+** | Fonctionnalités additionnelles du cahier des charges (import de masse, matricules avancés, paramétrage financier avancé — voir Volume 1). |
| **V2** | Application mobile (consultation parents/enseignants), intégration paiement mobile (Wave, Orange Money) via API. |
| **V3** | Portail parents/élèves, notifications SMS/WhatsApp, statistiques avancées (BI). |
| **V4** | Haute disponibilité multi-région, IA (analyse prédictive, détection d'anomalies financières). |

## 0.12 Règle d'or du projet

> **Aucune décision technique ne doit empêcher une évolution future du logiciel.**

Chaque module, chaque table, chaque API et chaque interface doit être conçu pour évoluer sans refonte complète. Cette règle reste la référence pour tous les volumes suivants — elle a d'ailleurs directement motivé le choix d'une base de données unique et d'une architecture cloud dès la V1, plutôt que trois architectures à faire converger plus tard.

## 0.13 Journal des décisions (changelog architectural)

| # | Décision | Remplace / rejette |
|---|---|---|
| D-01 | PostgreSQL unique, dès le jour 1, cloud natif, EF Core exclusivement, plus d'Offline-First | SQLite local → MySQL LAN → PostgreSQL SaaS (V1.0) |
| D-02 | Rejet définitif de SQLAlchemy comme ORM | Suggestion contradictoire du Vol.1 §36 (V1.0), incompatible avec un backend .NET |
| D-03 | Multi-tenant par base partagée + Row-Level Security PostgreSQL + Global Query Filter EF Core | Absence de stratégie définie (V1.0) |
| D-04 | Frontend ASP.NET Core MVC + Razor, API REST découplée dès le départ | — |
| D-05 | UUID v7 (triable chronologiquement) sur toutes les entités | UUID v4 non ordonnable (V1.0) |
| D-06 | Sauvegardes automatiques gérées par la plateforme (infra), export de données à la demande du Directeur | Bouton de sauvegarde manuelle locale (V1.0) |
| D-07 | Authentification par JWT (access + refresh token) + ASP.NET Core Identity | Authentification par cookie de session |
| D-08 | Scope du MVP gelé, fonctionnalités additionnelles en backlog Post-MVP explicite | Cahier des charges enrichi en continu sans priorisation (V1.0-1.4) |
| D-09 | Verrouillage optimiste (`RowVersion`/`xmin`) sur Notes, Paiements, Frais | Résolution de conflits de synchronisation offline (obsolète avec D-01) |
| D-10 | Inscription self-service (formulaire Directeur complet) + validation obligatoire du Super Admin avant activation | Création d'établissement réservée au seul Super Admin (première version de ce document) ; formulaire de simple prise de contact (version intermédiaire) |
| D-11 | Paiement réel des abonnements via agrégateur (PayDunya/CinetPay), Mobile Money + Virement + Carte, confirmation exclusivement par webhook signé HMAC | Abonnement purement déclaratif sans paiement réel |
| D-12 | Statut `AwaitingPayment` sur `Subscriptions`, mode restreint étendu à la période précédant le premier paiement confirmé | — |
| D-13 | Tailwind CSS comme framework CSS | Bootstrap (écarté : impose son propre style, plus difficile à aligner sur le design system du Volume 5) |

Ce tableau doit être mis à jour à chaque décision d'architecture majeure future, pour éviter que l'historique des choix ne se perde comme cela s'est produit entre les Volumes 0 et 1.

**Fin du Volume 0.**
