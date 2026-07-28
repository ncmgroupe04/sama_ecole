# PROJECT_SPEC - Spécifications Techniques Globales et Architecture (SamaEcole)

Ce document centralise les spécifications techniques et l'architecture globale de l'application SaaS **SamaEcole**, en stricte conformité avec le Cahier des Charges (Volume 0 & AGENTS.md). 

> **MISE EN GARDE ARCHITECTURALE**
> Toute référence historique à *Supabase*, à des bases de données de type *SQLite/MySQL*, ou à un mode *Hors-Ligne (Offline-First)* est déclarée **obsolète**. L'application repose exclusivement sur un backend .NET/PostgreSQL et fonctionne à 100 % en ligne. 

---

## 1. ARCHITECTURE BACKEND & SÉCURITÉ (.NET / PostgreSQL)

Le backend repose sur l'écosystème **ASP.NET Core 9** et applique le patron *Clean Architecture* couplé au *CQRS*.

### Structure du projet
L'application est découpée afin de garantir la séparation des responsabilités :
- **Controllers** (`SamaEcole.Web` / API) : Couche d'exposition HTTP. Ne contiennent aucune logique métier. Ils reçoivent les requêtes et les délèguent immédiatement à MediatR.
- **Handlers & Commands / Queries** (`SamaEcole.Application`) : Logique d'exécution (CQRS). Les opérations d'écriture (ex: `CreateStudentCommand`, `CreatePaymentCommand`) sont strictement séparées de la lecture.
- **DTOs & Validators** : Validation des données entrantes via FluentValidation.
- **Services** : Interfaces abstraites, injectées via l'IoC (ex: `ITenantProvider`).

### Gestion du Multitenant & Isolation (PostgreSQL)
L'application fonctionne selon le principe **Base de données unique, schéma partagé**. L'isolation totale des données entre les écoles est garantie par un mécanisme de sécurité strict à deux niveaux :
1. **Row-Level Security (RLS) PostgreSQL** : Une `Policy` PostgreSQL filtre chaque requête en base pour vérifier que l'identifiant du tenant courant (transmis dans une session ou context RLS) correspond à la colonne `SchoolId`.
2. **Global Query Filter EF Core** : Une sécurité applicative redondante configurée au sein du `DbContext` qui ajoute automatiquement le filtre `.Where(e => e.SchoolId == tenantId)` à toutes les requêtes d'entités métiers.

L'application se connecte avec le rôle `sama_ecole_app` qui ne possède aucun droit de contournement de la RLS (`NOBYPASSRLS`).

### Sécurité et Autorisations par Rôles
- **Authentification** : Gérée via ASP.NET Core Identity et sécurisée par des jetons d'accès **JWT** pour l'API. Le jeton embarque les *claims* essentiels (`sub`, `schoolId`, `role`).
- **Rôles** : L'accès aux endpoints est protégé par politiques (Policies) en fonction de profils métiers prédéfinis :
  - **Super Admin** : Gestion de la plateforme technique et abonnements.
  - **Directeur** : Paramétrage global de l'école.
  - **Financier / Caissier** : Encaissement, reçus et suivi.
  - **Secrétaire** : Inscriptions et suivi administratif.

---

## 2. RÉSILIENCE RÉSEAU PWA (Remplacement du Hors-Ligne)

Conformément à la vision SaaS (100% cloud), **le mode hors-ligne transactionnel ou la synchronisation multi-postes n'existe pas**. La source de vérité est systématiquement le serveur. Toutefois, pour mitiger l'instabilité des réseaux, le frontend implémente des mécanismes de *Résilience Réseau*.

### Service Worker (`sw.js`)
Le Service Worker n'intercepte que le contenu non-sensible afin de garantir des chargements ultra-rapides :
- **Cache-First** : Réservé exclusivement aux assets statiques (Tailwind CSS, Javascript Alpine/Vanilla, polices et images).
- **Network-Only** : Toutes les requêtes HTTP pointant vers `/api/*` ainsi que toutes les mutations métier (POST, PUT, DELETE) contournent le cache. Le Service Worker n'a aucun droit de stockage persistant de données métiers sur IndexedDB.

### Gestion des brouillons et coupures réseau
- **`form-draft.js`** : Enregistre à la volée les états des formulaires (textes saisis, sélections) dans le `localStorage` du navigateur. Les clés de stockage sont systématiquement préfixées par le `SchoolId` (`[schoolId]_draft_formX`).
- **`network-guard.js`** : Détecte les pertes de connexion et empêche visuellement la validation d'un formulaire jusqu'au retour de la connexion, évitant la perte silencieuse de clics et fournissant un feedback clair à l'utilisateur.

---

## 3. MOTEUR D'IMPRESSION & DOCUMENTS PDF (QuestPDF)

La génération de documents (bulletins, reçus de caisse) s'effectue côté serveur pour garantir une uniformité parfaite, selon la charte graphique de l'application, en exploitant la librairie .NET **QuestPDF**.

### Templates PDF
Les documents héritent d'interfaces et templates standardisés :
- **`PaymentReceiptPdfDocument` (Reçu de Caisse)** : Produit le ticket de transaction financière. Il intègre formellement la mention exigée par le cahier des charges : *"Il est demandé aux parents de garder minutieusement leur reçu après le paiement."*
- **`EnrollmentReceiptPdfDocument` (Reçu d'Inscription)** : Fournit la preuve d'inscription administrative de l'élève.

### Endpoints
La génération est exposée via des endpoints REST dédiés, par exemple :
- `/api/v1/finance/payments/{id}/receipt/pdf`
- `/api/v1/enrollments/{id}/receipt/pdf`

### Chaîne de rendu Frontend JS
L'affichage sur le client est opéré de la manière suivante :
1. **Appel Fetch** depuis le client avec les headers d'autorisation (`Authorization: Bearer <JWT>`).
2. Le backend génère et retourne un flux binaire (`application/pdf`).
3. Le code JS convertit la réponse réseau en un **Blob**.
4. Création d'une URL éphémère via `URL.createObjectURL(blob)`.
5. **Injection Iframe** : L'URL est passée à l'attribut `src` d'une `<iframe>` dans une modale d'aperçu, garantissant une lecture native sans dépendance tierce.

---

## 4. BINDINGS ET LOGIQUE DE LA MODALE D'APERÇU

La consultation et l'exploitation des PDF utilisent une **Modale d'Aperçu** intégrée au Dashboard.

### Actions (Boutons)
- **Imprimer** : Invoque silencieusement le contexte de l'Iframe via Javascript (`iframe.contentWindow.print()`), déclenchant la fenêtre d'impression native de l'OS sans rafraîchissement.
- **Télécharger** : Construit à la volée une balise `<a>` invisible dotée de l'attribut `download="Recu_{ID}.pdf"`, injecte l'URL du Blob et simule un clic.
- **Fermer** : Appelle obligatoirement la fonction `URL.revokeObjectURL(blob)` pour libérer la ressource mémoire du navigateur, avant de masquer le composant modale et supprimer le Blob.

### Gestion des Exceptions
Afin de préserver la qualité de l'UX, la logique de la modale **proscrit totalement l'usage bloquant des fenêtres `alert()` natives**. 
Toute exception (erreur HTTP 404, token invalide 401, timeout réseau) est capturée (`.catch()`) par le gestionnaire d'événement Fetch, puis restituée via les composants UI de la plateforme (Toast notifications ou bannières de message d'erreur intégrées à la vue).
