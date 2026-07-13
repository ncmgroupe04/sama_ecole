# JANGALEKAT

# VOLUME 9 — Deployment & Operations Guide (DOG)

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0
**Changement majeur :** suppression complète des modes de déploiement Local (SQLite) et LAN (MySQL). **Un seul mode de déploiement : Cloud SaaS**, dès la première mise en production.

---

## Table des matières

1. Objectif
2. Architecture de déploiement (cloud, unique)
3. Environnements
4. Intégration et déploiement continus (CI/CD)
5. Sauvegardes
6. Restauration
7. Mises à jour
8. Onboarding d'un nouvel établissement
9. Supervision
10. Journalisation
11. Maintenance
12. Plan de reprise après sinistre (Disaster Recovery)
13. Gestion des versions

---

## 1. Objectif

Définir les procédures d'installation, de déploiement, de maintenance, de mise à jour, de sauvegarde et d'exploitation de Jangalekat en tant que **plateforme cloud unique**, servant toutes les écoles clientes simultanément.

> Ce document remplace la version 1.0, qui couvrait trois modes de déploiement (Local/LAN/SaaS) avec une base de code unique mais trois architectures cibles. Cette complexité disparaît entièrement (Volume 0 v2.0) : il n'y a plus qu'une seule architecture cible à opérer, ce qui réduit fortement la surface de risque opérationnel.

## 2. Architecture de déploiement (cloud, unique)

```
Internet
   │
   ▼
Nginx (reverse proxy + TLS)
   │
   ▼
ASP.NET Core (conteneurs Docker, plusieurs instances derrière un load balancer)
   │
   ▼
PostgreSQL managé (instance principale + réplique de lecture)
   │
   ▼
Stockage objet (fichiers : photos, logos, PDF générés, pièces justificatives)
```

Toutes les écoles (Directeur, Secrétariat, Finance, Enseignants) se connectent au même point d'entrée, avec l'isolation garantie par établissement (Volume 3 §2).

### 2.1 Composants

| Composant | Choix |
|---|---|
| Système hôte | Linux Ubuntu LTS |
| Conteneurisation | Docker |
| Orchestration | Docker Compose au lancement ; migration vers Kubernetes prévue à partir de quelques centaines d'écoles (Volume 0 §0.8) sans changement d'image applicative |
| Reverse proxy / TLS | Nginx + certificats automatiques (Let's Encrypt ou équivalent managé) |
| Base de données | PostgreSQL managé (sauvegardes automatiques, réplication, haute disponibilité fournies par l'hébergeur) |
| Stockage de fichiers | Stockage objet compatible S3 (jamais le disque local du conteneur applicatif, qui est éphémère) |
| Cache | Redis managé |

## 3. Environnements

| Environnement | Usage |
|---|---|
| **Développement** | Poste des développeurs, Docker Compose local, base PostgreSQL locale de test (jamais de données réelles) |
| **Staging** | Réplique de la production, utilisée pour valider chaque déploiement et chaque migration avant mise en production |
| **Production** | Environnement servant les écoles clientes réelles |

## 4. Intégration et déploiement continus (CI/CD)

Pipeline déclenché à chaque fusion sur `main` (Volume 6 §9) :

1. Build et exécution des tests unitaires et d'intégration (Volume 8).
2. Exécution du test critique d'isolation multi-tenant (Volume 8 §5) — échec = blocage automatique du déploiement.
3. Construction de l'image Docker.
4. Déploiement automatique en **staging**.
5. Exécution des tests End-to-End en staging.
6. Validation manuelle (déploiement en production non automatique pour les versions majeures).
7. Sauvegarde automatique de la base de production **avant** application des migrations.
8. Application des migrations EF Core.
9. Déploiement en production (déploiement progressif — ex. rolling update — pour éviter toute interruption de service).
10. Contrôle de bon fonctionnement automatique (health check) post-déploiement.

En cas d'échec à une étape quelconque, retour automatique à la version précédente.

## 5. Sauvegardes

- **Automatiques**, gérées au niveau de l'infrastructure : sauvegarde complète quotidienne + sauvegarde incrémentielle continue (Point-in-Time Recovery) fournie par le service PostgreSQL managé.
- Rétention : 30 jours glissants minimum.
- Chiffrement au repos (AES-256).
- Contenu : base de données, fichiers du stockage objet, configuration applicative.
- **Aucune action requise de l'utilisateur final** : contrairement à la version 1.0 de ce document, il n'existe plus de « sauvegarde manuelle déclenchée par le Directeur » — celle-ci est remplacée par la fonctionnalité **Exporter mes données** (Volume 4 §11), qui sert un usage différent (portabilité des données de l'école, pas la reprise après sinistre).

## 6. Restauration

Procédure : 1. Sélection du point de restauration → 2. Vérification d'intégrité → 3. Confirmation par un Super Admin → 4. Restauration sur environnement isolé → 5. Vérification fonctionnelle → 6. Bascule en production → 7. Reprise du service.

Toute restauration est enregistrée dans le journal d'audit plateforme (Volume 7 §7).

## 7. Mises à jour

| Type | Exemple |
|---|---|
| Correctif (patch) | Correction de bug, aucune migration de schéma |
| Version mineure | Nouvelle fonctionnalité, migration additive |
| Version majeure | Changement structurant, communication préalable aux écoles |

Processus : sauvegarde automatique → migration de base → déploiement progressif → contrôle automatique → notification aux établissements concernés si changement visible. **Toutes les écoles sont mises à jour simultanément** — c'est l'un des bénéfices directs du modèle SaaS unique par rapport à l'ancien modèle multi-déploiement, où chaque poste local devait être mis à jour individuellement.

## 8. Onboarding d'un nouvel établissement

> Ce chapitre remplace le chapitre « Migration vers le SaaS » de la version 1.0, qui décrivait le passage d'une école d'un mode local vers le cloud. Cette étape disparaît puisqu'il n'existe plus de mode local à migrer depuis : chaque école démarre directement en ligne.

1. Le Super Admin crée l'établissement (Volume 4 §2) et son plan d'abonnement.
2. Création automatique du compte Directeur initial, avec envoi d'un lien d'activation par email.
3. Le Directeur se connecte, configure son établissement (Volume 1 §10).
4. Si l'école migre depuis un système existant (Excel, autre logiciel) : import de masse des élèves/enseignants (Volume 1 §3, Post-MVP V1.1) ou saisie manuelle initiale.
5. Activation complète — durée cible : moins de 10 minutes sans intervention technique sur site (Volume 1.5, User Story Super Admin).

## 9. Supervision

Surveillance continue de : disponibilité de l'API (health checks), latence des requêtes, état de la base de données (connexions, requêtes lentes), espace disque et stockage objet, validité des abonnements arrivant à expiration, taux d'erreur applicatif. Alertes automatiques (email/Slack/SMS interne équipe) en cas d'anomalie, avec seuils définis par service.

## 10. Journalisation

Événements enregistrés : connexions, erreurs applicatives, sauvegardes, restaurations, mises à jour, paiements, impressions, imports/exports — centralisés (ex. via Serilog + un puits de journalisation centralisé) et non plus dispersés poste par poste comme dans l'ancien modèle LAN.

## 11. Maintenance

- **Préventive :** vérification de la base, optimisation des index, nettoyage des journaux anciens, contrôle des sauvegardes (test de restauration périodique, pas seulement une vérification d'existence du fichier).
- **Corrective :** résolution des incidents, restauration ciblée, analyse post-incident (post-mortem).

## 12. Plan de reprise après sinistre (Disaster Recovery)

| Scénario | Réponse |
|---|---|
| Panne d'une instance applicative | Basculement automatique vers une autre instance (load balancer) |
| Panne de la base de données principale | Bascule vers la réplique (managé par l'hébergeur) |
| Corruption de données | Restauration à partir du dernier point de sauvegarde valide (§6) |
| Panne totale de la région d'hébergement | Reconstruction depuis les sauvegardes chiffrées vers une région de secours — objectifs RTO/RPO à définir avec l'hébergeur retenu |

Chaque scénario est documenté avec une procédure testée périodiquement (exercice de reprise), pas uniquement rédigée sur papier.

## 13. Gestion des versions

Format : `MAJEURE.MINEURE.CORRECTIF` (ex. `1.0.0`, `1.1.0`, `1.1.3`, `2.0.0`). Un changelog est maintenu et publié, avec une distinction claire entre changements visibles pour les écoles et changements internes.

**Fin du Volume 9.**

---

## Note de clôture — volumes restants

Les volumes suivants restent recommandés mais non bloquants pour démarrer le développement (cohérent avec le Volume 7 v1.0 original) : **Volume 10 — Business Continuity Plan**, **Volume 11 — User Manuals**, **Volume 12 — Administrator & Technical Operations Manual**, **Volume 13 — Product Roadmap & Release Management**. Ils peuvent être rédigés en parallèle du développement du MVP (Volume 1.5, Chapitre 7), sans retarder son démarrage.
