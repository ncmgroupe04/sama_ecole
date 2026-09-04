# SAMA ECOLE

# VOLUME 9 — Deployment & Operations Guide (DOG)

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0
**Changement majeur :** suppression complète des modes de déploiement Local (SQLite) et LAN (MySQL). **Un seul mode de déploiement : Cloud SaaS**, dès la première mise en production.

---

## Table des matières

1. Objectif
2. Architecture de déploiement (cloud, unique)
3. Environnements
3bis. Configuration & secrets
3ter. Cloud Run — variables requises et mise en service
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

Définir les procédures d'installation, de déploiement, de maintenance, de mise à jour, de sauvegarde et d'exploitation de Sama Ecole en tant que **plateforme cloud unique**, servant toutes les écoles clientes simultanément.

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

## 3bis. Configuration & secrets

Toute la configuration sensible (chaînes de connexion, clé de signature JWT, clés PayDunya, SMTP, SMS,
WhatsApp) est fournie exclusivement par variable d'environnement — jamais committée dans un
`appsettings*.json` (AGENTS.md, section « Ne jamais faire »).

`.env.example`, à la racine du dépôt, documente **chaque** clé attendue, sa forme (convention ASP.NET
Core `Section__Cle`), et si elle est requise ou optionnelle. C'est la référence unique — ne pas la
laisser dériver du code : toute nouvelle section de configuration (nouvel `IOptions<T>`) doit y être
ajoutée au moment où elle est introduite.

- **Développement** : copier `.env.example` en `.env` (jamais committé — `.gitignore`) ; Docker Compose
  le lit automatiquement (`docker-compose.yml`).
- **Staging/Production** : les mêmes clés sont posées comme variables d'environnement sur la
  plateforme d'hébergement (jamais dans un fichier versionné). Les gardes de démarrage
  (`RlsGuard`, la vérification de `Jwt:SigningKey` dans `Program.cs`, `EmailSenderGuard`) font échouer
  le déploiement plutôt que de démarrer silencieusement avec une configuration incomplète ou dangereuse
  (ex. rôle PostgreSQL propriétaire branché sur l'application — AGENTS.md règle #2).
- Les intégrations optionnelles à l'onboarding (PayDunya, SMTP, SMS, WhatsApp) utilisent le sentinel
  `"REMPLACER"` comme valeur de configuration explicitement « non configurée » (`*Options.IsConfigured`
  dans `SamaEcole.Infrastructure`) — permet de déployer une école sans SMS ni WhatsApp actifs sans que
  l'application ne tente d'appeler un agrégateur avec des clés invalides.
- Rotation d'un secret compromis : le remplacer côté plateforme d'hébergement puis redéployer (pas de
  procédure applicative dédiée — aucun secret n'est mis en cache au-delà de la durée de vie du
  processus).

## 3ter. Cloud Run — variables requises et mise en service

Cloud Run est une variante d'hébergement du §2 : il remplace « Nginx + Docker Compose » par son propre
frontal TLS et son propre ordonnanceur. L'image applicative, elle, ne change pas.

**`sama-ecole-prod` est le PROJET GCP, pas un nom de service.** Le service Cloud Run qui héberge ce
monolithe s'appelle **`sama-ecole-web`**, région `europe-west1`, image publiée sur Artifact Registry
(`europe-west1-docker.pkg.dev/sama-ecole-prod/sama-ecole-repo/sama-ecole-web`). Confondre les deux
fait chercher un service qui n'existe pas (`gcloud run services describe sama-ecole-prod` échoue par
« Cannot find service »).

Un second service, `sama-ecole-api` (image `gcr.io/sama-ecole-prod/sama-ecole-api`, Container
Registry et non Artifact Registry), existe encore dans le même projet — c'est un reliquat de
l'ancienne architecture front React + API séparée (voir `_old-web-repo.bundle` à la racine du dépôt,
et les clés `NEXT_PUBLIC_API_URL`/`VITE_API_URL` d'un `.env.production` local non versionné qui
pointent dessus). Il n'est plus le chemin de déploiement de ce dépôt ; ne pas y déployer par réflexe
sous prétexte que son nom contient « api ».

**Une seule image, une seule révision.** `SamaEcole.Web` est un monolithe : les 42 contrôleurs `api/v1`
et les vues Razor vivent dans le MÊME processus. Il n'y a pas de service « API » séparé à joindre par
HTTP, donc **aucune variable de type `Api__BaseUrl`** — le front appelle ses contrôleurs en interne. Le
service a donc besoin de sa base PostgreSQL et de sa configuration complète, exactement comme en §3bis.

### Deux contraintes propres à Cloud Run

1. **Le port est imposé par la plateforme.** Cloud Run injecte `PORT` et déclare la révision en échec si
   le conteneur n'écoute pas dessus dans le délai de démarrage. `Program.cs` lit `PORT` et appelle
   `UseUrls("http://0.0.0.0:$PORT")` — ne posez ni `PORT` ni `ASPNETCORE_URLS` à la main dans le
   service, ce sont deux sources de vérité pour la même chose.
2. **Toute garde de démarrage qui échoue produit le même message.** `The user-provided container failed
   to start and listen on the port` ne dit RIEN du port dans ce cas : c'est le processus qui s'est
   arrêté avant d'écouter. La cause réelle est toujours dans les journaux de la révision, quelques
   lignes plus haut — clé de configuration manquante, ou base injoignable.

### Variables à poser sur le service

| Variable | Statut | Effet si absente |
|---|---|---|
| `ConnectionStrings__Default` | **requise** | `AddPersistence` : « ConnectionStrings:Default est manquant » |
| `Jwt__SigningKey` | **requise** (≥ 32 octets, aléatoire, propre à l'environnement) | `Program.cs` refuse de démarrer |
| `Smtp__Host`, `Smtp__User`, `Smtp__Password`, `Smtp__FromAddress` | **requises** | `EmailSenderGuard` refuse de démarrer (voir la sortie de secours ci-dessous) |
| `ForwardedHeaders__Enabled=true` | **requise** | Le frontal Cloud Run termine TLS : sans cela l'application voit l'IP du frontal pour toutes les requêtes (les limiteurs par IP s'effondrent sur une partition unique) et `Request.Scheme` reste `http` |
| `ASPNETCORE_ENVIRONMENT=Production` | recommandée | Défaut de l'image ; à poser explicitement pour lever toute ambiguïté (`Staging` sur la recette) |
| `Auth__PublicBaseUrl`, `PayDunya__PublicBaseUrl` | requises en production | URL publique du service — sert à construire les liens envoyés par e-mail et les retours PayDunya |
| `Sms__*`, `WhatsApp__*`, `PayDunya__*` | optionnelles | Avertissement au démarrage, canal inactif (§3bis) |
| `ConnectionStrings__Migrations` | **à NE PAS poser** | Rôle propriétaire, exempté de RLS. Il n'appartient qu'au travail de migration (AGENTS.md règle #2) |
| `SAMA_RETOUR_MODE_TEST_AUTORISE` | **à NE PAS poser en production** | `Program.cs` refuse explicitement `true` en `Production` |

`ConnectionStrings__Default` doit porter le rôle **applicatif** (`sama_ecole_app`). Sur une base gérée
joignable par Internet, gardez `SslMode=Require`. `RlsGuard` vérifie au démarrage que ce rôle ne
contourne pas la RLS et réessaie la connexion (4 tentatives, 2 s + 4 s + 8 s) le temps qu'une base gérée
se réveille ; passé ce délai il refuse de démarrer avec un message explicite plutôt que de servir des
données dont l'isolation n'a pas pu être vérifiée.

### Base de données Cloud SQL — se connecter par le socket Unix, pas par un nom d'hôte TCP

La production utilise l'instance Cloud SQL PostgreSQL `sama-ecole` (nom de connexion
`sama-ecole-prod:europe-west1:sama-ecole`), IP publique uniquement (pas d'IP privée activée à ce
jour). **Sur Cloud Run, une instance Cloud SQL se rattache au SERVICE, pas seulement à la base :**

```bash
gcloud run services update sama-ecole-web \
  --region europe-west1 \
  --add-cloudsql-instances=sama-ecole-prod:europe-west1:sama-ecole
```

Sans cet indicateur, Cloud Run ne provisionne pas le proxy Cloud SQL Auth géré et rien ne route vers
l'instance — la panne observée le 04/09/2026 sur `sama-ecole-web-00008-bjd` correspond exactement à
ça : `RlsGuard` réessayait 4 fois puis échouait sur une `SocketException` (résolution DNS/route
absente pour le nom d'hôte configuré), jamais sur un identifiant ou un mot de passe refusé. Le
message de `RlsGuard` distingue maintenant ce cas (voir `SamaEcole.Persistence.RlsGuard.ContainsSocketException`).

Une fois l'instance rattachée, `ConnectionStrings__Default` doit pointer le **socket Unix** que le
proxy expose dans le conteneur, pas un nom d'hôte TCP :

```
Host=/cloudsql/sama-ecole-prod:europe-west1:sama-ecole;Port=5432;Database=sama-ecole-db;Username=sama_ecole_app;Password=<mot de passe du rôle applicatif>;SSL Mode=Disable
```

`SSL Mode=Disable` est correct ici et pas un relâchement de sécurité : le socket Unix ne sort jamais
du conteneur, le chiffrement vers Cloud SQL est déjà assuré par le proxy géré (mTLS), et Npgsql
n'accepte pas de négocier TLS sur un socket Unix. Ne PAS réutiliser `SslMode=Require;Trust Server
Certificate=true`, qui suppose une connexion TCP.

Le nom de la base (`sama-ecole-db`, avec des tirets) et le rôle applicatif (`sama_ecole_app`) sont
confirmés par `gcloud sql databases list` / `gcloud sql users list --instance=sama-ecole` — à
vérifier avant de coller cette chaîne, un nom de base qui ne correspond à rien produit une erreur
différente (rejet PostgreSQL, pas `SocketException`) une fois la connectivité réseau réglée.

### Appliquer les migrations sur Cloud SQL — Cloud Run Job

`tools/SamaEcole.Tools` (rôle **propriétaire** `sama_ecole`, jamais le rôle applicatif — voir son
README) tourne en production comme un **Cloud Run Job**, pas comme un conteneur local ni un script
sur le poste du développeur. Mis en place le 04/09/2026, image sur le même Artifact Registry que
le service web :

```bash
gcloud builds submit --config=cloudbuild-tools.yaml --substitutions=_IMAGE=europe-west1-docker.pkg.dev/sama-ecole-prod/sama-ecole-repo/sama-ecole-tools:latest .

gcloud run jobs deploy sama-ecole-migrate \
  --image=europe-west1-docker.pkg.dev/sama-ecole-prod/sama-ecole-repo/sama-ecole-tools:latest \
  --region=europe-west1 \
  --set-cloudsql-instances=sama-ecole-prod:europe-west1:sama-ecole \
  --set-env-vars="ConnectionStrings__Migrations=Host=/cloudsql/sama-ecole-prod:europe-west1:sama-ecole;Port=5432;Database=sama-ecole-db;Username=sama_ecole;Password=<mot de passe du role proprietaire>;SSL Mode=Disable" \
  --max-retries=0 \
  --task-timeout=300

gcloud run jobs execute sama-ecole-migrate --region=europe-west1 --wait
```

**`--set-cloudsql-instances`, pas `--add-cloudsql-instances`** : ce dernier n'existe que pour
`gcloud run services deploy/update` ; sur un Job, gcloud refuse l'argument (constaté le
04/09/2026). L'exécution du job affiche le nombre de migrations en attente puis les applique ;
relancer le même job sur une base déjà à jour est sans danger — il rapporte simplement
« Aucune migration en attente » et sort en 0.

Comme pour `ConnectionStrings__Default` (§ précédente), le mot de passe du rôle propriétaire
passe par une variable d'environnement du Job, jamais par un fichier versionné, et devrait migrer
vers Secret Manager au même titre.

### Déployer sans serveur SMTP (recette, démonstration, première mise en service)

`Smtp__AllowUnconfigured=true` lève l'échec de démarrage — et rien d'autre. L'application n'utilise
alors PAS `LoggingEmailSender` (réservé à Development, il écrit le corps des messages en clair, mot de
passe provisoire du Directeur compris) mais `UnconfiguredEmailSender`, qui ne journalise que
destinataire et objet et qui échoue au premier envoi. Un avertissement est émis à chaque démarrage.
À réserver aux environnements dont les utilisateurs n'attendent pas leurs e-mails.

### Commandes

Le contexte de build est la RACINE du dépôt : `Dockerfile` (racine) et `src/SamaEcole.Web/Dockerfile`
sont deux copies identiques de la même image, à garder synchronisées.

```bash
gcloud builds submit --tag europe-west1-docker.pkg.dev/sama-ecole-prod/sama-ecole-repo/sama-ecole-web:latest

gcloud run deploy sama-ecole-web \
  --image europe-west1-docker.pkg.dev/sama-ecole-prod/sama-ecole-repo/sama-ecole-web:latest \
  --region europe-west1 \
  --platform managed
```

**Sans `--set-env-vars`/`--update-env-vars` volontairement.** Sur un service déjà déployé au moins une
fois, `gcloud run deploy` reprend automatiquement les variables d'environnement de la révision
précédente si on ne les repasse pas explicitement — c'est ce qui permet de rebuilder et redéployer une
correction de code sans jamais ressaisir `ConnectionStrings__Default` ni `Jwt__SigningKey` en clair
dans une commande. Ne les repasser en `--set-env-vars` que pour les CHANGER, jamais pour les redire à
l'identique.

Idéalement, ces deux clés (et `Smtp__Password` le jour où le SMTP réel est configuré) migrent vers
Secret Manager (`--set-secrets`, valeur `nom-du-secret:latest`) plutôt que de rester des variables
d'environnement en clair, lisibles par quiconque a le droit de décrire le service et conservées dans
l'historique de chaque révision. Non fait à ce jour sur `sama-ecole-web` — à planifier, pas urgent tant
que l'accès au projet GCP reste restreint à l'équipe.

### Vérification post-déploiement

```bash
curl -fsS https://<service>/health/live    # le processus répond, sans dépendance
curl -fsS https://<service>/health/ready   # PostgreSQL joignable — c'est la sonde qui compte
```

Les migrations ne sont **jamais** appliquées par le service web : elles passent par l'outil dédié
(`tools/SamaEcole.Tools`, rôle propriétaire), comme le service `migrate` de `docker-compose.yml`.

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
