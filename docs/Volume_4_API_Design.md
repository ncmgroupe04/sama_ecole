# JANGALEKAT

# VOLUME 4 — API Design Specification (ADS)

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0
**Changements de cette version :** ajout du chapitre Authentification (absent de la v1.0) ; suppression des références à la synchronisation locale/LAN/SaaS et au poste de travail local (§0.11, §0.17 de la v1.0), non pertinentes pour une plateforme en ligne.

---

## Table des matières

0. Fonctionnalités transversales
1. Authentification et gestion de session
2. API Écoles & Abonnements (Super Admin)
3. API Utilisateurs
4. API Élèves & Enseignants
5. API Classes & Matières
6. API Inscriptions
7. API Finance
8. API Notes
9. API Bulletins
10. API Présences
11. API Paramètres

---

## 0. Fonctionnalités transversales

### 0.1 Convention générale

Toutes les routes sont préfixées `/api/v1/`. Le versionnement d'API se fait par préfixe d'URL, jamais par en-tête caché, pour rester explicite et facile à déprécier proprement.

### 0.2 Pagination

```
GET /api/v1/students?page=1&pageSize=20
```

| Paramètre | Description |
|---|---|
| `page` | Numéro de page (défaut : 1) |
| `pageSize` | Taille de page (défaut : 20, max : 100) |
| `sort` | Champ de tri |
| `order` | `asc` / `desc` |

### 0.3 Recherche et filtres

```
GET /api/v1/students?search=Diallo
GET /api/v1/payments?schoolYear=2026&class=CM2A&status=PAID
```

La recherche porte sur les champs pertinents du module (matricule, nom, prénom, téléphone, numéro de reçu, référence de paiement).

### 0.4 Format des erreurs

```json
{
  "success": false,
  "message": "Validation échouée.",
  "errors": { "lastName": ["Le nom est obligatoire."] }
}
```

| Code | Signification | Statut HTTP |
|---|---|---|
| `ValidationError` | Erreur de saisie | 400 |
| `Unauthorized` | Authentification requise ou expirée | 401 |
| `Forbidden` | Permission insuffisante | 403 |
| `NotFound` | Ressource inexistante | 404 |
| `Conflict` | Doublon ou conflit de concurrence | 409 |
| `BusinessRuleError` | Règle métier violée | 422 |
| `InternalError` | Erreur interne | 500 |

Toute erreur est journalisée (Volume 7, journalisation de sécurité).

### 0.5 Fichiers

Formats autorisés : JPG, PNG (photos, logo, cachet, signatures), PDF (justificatifs, documents d'inscription). Stockage objet cloud (Volume 9) — jamais sur le disque du serveur applicatif, pour rester compatible avec une exécution multi-instance sans état (« stateless »).

### 0.6 Journalisation

Chaque opération sensible (création, modification, suppression logique, impression, export, connexion, changement de mot de passe) est enregistrée avec : utilisateur, date/heure UTC, adresse IP, action réalisée. Détail complet au Volume 7.

### 0.7 Transactions et concurrence

Les opérations critiques (inscription, paiement, génération de bulletin) sont transactionnelles avec retour arrière complet en cas d'échec. Le contrôle de concurrence est optimiste (`RowVersion`/`xmin`) : en cas de modification simultanée, le premier enregistrement gagne et le second utilisateur reçoit une erreur `Conflict` (409) lui demandant de recharger les données.

### 0.8 Formats

- Dates techniques : ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ssZ`) dans tous les échanges API. L'affichage local (Volume 1 §12.1) est une responsabilité du client, jamais de l'API.
- Nombres : suppression des décimales inutiles à l'affichage (`17` plutôt que `17.0`), gérée côté client à partir d'une valeur numérique brute renvoyée par l'API.

### 0.9 Suppression logique

Aucune suppression physique de donnée métier. Un enregistrement supprimé est marqué `IsDeleted = true` et reste restaurable par le Directeur ou le Super Admin selon le périmètre.

### 0.10 Performances

Toutes les listes sont paginées, toutes les requêtes évitent le problème N+1 (chargement explicite ou projection), et les réponses utilisent des DTO ne renvoyant que les champs nécessaires — jamais l'entité EF Core brute.

---

## 1. Authentification et gestion de session

> Chapitre ajouté dans cette version : absent de la v1.0, alors qu'il est le point d'entrée de toute API en ligne.

### 1.1 Principe

Authentification par **JWT** (access token courte durée, ~15 min) + **refresh token** (durée plus longue, stocké en cookie `HttpOnly` `Secure`). Aucune session serveur en mémoire : l'API est sans état, condition nécessaire à une future montée en charge horizontale (Volume 9).

### 1.2 Endpoints

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/auth/login` | Authentifie l'utilisateur (email + mot de passe), retourne access + refresh token |
| `POST` | `/api/v1/auth/refresh` | Échange un refresh token valide contre un nouveau access token |
| `POST` | `/api/v1/auth/logout` | Invalide le refresh token courant |
| `POST` | `/api/v1/auth/forgot-password` | Déclenche l'envoi d'un email de réinitialisation |
| `POST` | `/api/v1/auth/reset-password` | Applique un nouveau mot de passe à partir d'un jeton reçu par email |

### 1.3 Contexte tenant

Chaque JWT contient les claims `sub` (UserId), `schoolId`, `role`. Le middleware de résolution de tenant lit `schoolId` depuis le token — jamais depuis un paramètre de requête modifiable par le client — et l'utilise pour positionner le filtre RLS PostgreSQL (Volume 3 §2.2). Un Super Admin possède un token sans `schoolId` et accède aux endpoints du Chapitre 2 uniquement.

---

## 2. API Écoles & Abonnements (Super Admin)

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/admin/schools` | Créer un établissement + compte Directeur initial (voie manuelle directe, conservée pour les cas hors self-service) |
| `GET` | `/api/v1/admin/schools` | Lister les établissements |
| `PATCH` | `/api/v1/admin/schools/{id}/suspend` | Suspendre un établissement |
| `PATCH` | `/api/v1/admin/schools/{id}/activate` | Réactiver un établissement |
| `POST` | `/api/v1/admin/subscriptions` | Créer/renouveler un abonnement |
| `GET` | `/api/v1/admin/subscriptions/expiring` | Lister les abonnements arrivant à expiration (30/15/7 jours) |
| `GET` | `/api/v1/admin/registration-requests` | Lister les demandes d'inscription (filtrable par statut) |
| `POST` | `/api/v1/admin/registration-requests/{id}/approve` | Approuver → crée établissement + Directeur + abonnement `AwaitingPayment` (Volume 1 §11.5) |
| `POST` | `/api/v1/admin/registration-requests/{id}/reject` | Rejeter (motif obligatoire) |

## 2bis. API Inscription self-service & Paiement (public + Directeur)

| Méthode | Route | Auth | Description |
|---|---|---|---|
| `POST` | `/api/v1/registration-requests` | **Aucune** (public) | Soumettre le formulaire d'inscription (Volume 1 §11.5). Anti-spam obligatoire (captcha/honeypot). |
| `GET` | `/api/v1/registration-requests/{trackingReference}/status` | **Aucune** (public, via référence) | Suivre l'état d'une demande sans authentification |
| `POST` | `/api/v1/subscriptions/{schoolId}/payments` | Directeur | Initier un paiement (Mobile Money, virement, carte) — retourne l'URL de redirection agrégateur |
| `GET` | `/api/v1/subscriptions/{schoolId}/payments` | Directeur | Historique des paiements de l'établissement |
| `POST` | `/api/v1/webhooks/payments/{provider}` | **Aucune** (public, vérifié par signature HMAC) | Callback de l'agrégateur (PayDunya/CinetPay) confirmant ou infirmant un paiement — voir Volume 7 §Paiements pour la vérification obligatoire de signature |

## 3. API Utilisateurs

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/users` | Créer un utilisateur |
| `GET` | `/api/v1/users` | Lister les utilisateurs de l'établissement |
| `PATCH` | `/api/v1/users/{id}/suspend` | Suspendre (motif requis) |
| `PATCH` | `/api/v1/users/{id}/block` | Bloquer définitivement |
| `PATCH` | `/api/v1/users/{id}/reactivate` | Réactiver |
| `POST` | `/api/v1/users/{id}/reset-password` | Déclencher une réinitialisation |

## 4. API Élèves & Enseignants

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/students` | Créer un élève (génère le matricule en transaction, Volume 1 §2.1) |
| `GET` | `/api/v1/students` | Lister (pagination, recherche, filtres) |
| `GET` | `/api/v1/students/{id}` | Fiche détaillée (Volume 1 §4.1) |
| `PUT` | `/api/v1/students/{id}` | Modifier |
| `POST` | `/api/v1/students/import` | Import de masse Excel/CSV (asynchrone, Post-MVP V1.1) |
| `GET` | `/api/v1/students/export/excel` | Export |
| *(mêmes endpoints en `/teachers`)* | | |

## 5. API Classes & Matières

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/classrooms` | Créer une classe (aucune liste figée, Volume 1 §5.1) |
| `GET` | `/api/v1/classrooms` | Lister avec effectifs (total, garçons, filles) |
| `POST` | `/api/v1/subjects` | Créer une matière |

## 6. API Inscriptions

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/enrollments` | Créer une inscription/pré-inscription |
| `POST` | `/api/v1/enrollments/{id}/confirm` | Confirmer → émet `EnrollmentConfirmed` (Volume 2 §5.1) |
| `GET` | `/api/v1/enrollments/{id}/receipt` | Reçu d'inscription (PDF) |
| `GET` | `/api/v1/classrooms/{id}/availability` | Places disponibles |
| `POST` | `/api/v1/waiting-list` | Inscrire sur liste d'attente (Post-MVP V1.1) |

## 7. API Finance

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/fee-categories` | Créer une catégorie de frais |
| `POST` | `/api/v1/school-fees` | Paramétrer les frais (application globale ou par classe, Volume 1 §7.4) |
| `POST` | `/api/v1/payments` | Encaisser un paiement |
| `GET` | `/api/v1/payments/{id}/receipt` | Reçu (PDF) |
| `POST` | `/api/v1/expenses` | Enregistrer une dépense |
| `GET` | `/api/v1/finance/dashboard` | Tableau de bord (encaissé jour/mois/année, taux de recouvrement) |
| `GET` | `/api/v1/finance/statistics/{classes\|levels\|debtors\|monthly}` | Statistiques détaillées |
| `GET` | `/api/v1/finance/reports/{income\|expenses}` | Rapports (PDF/Excel) |

## 8. API Notes

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/grades` | Saisir une note (validation de plage selon `GradingSettings`) |
| `POST` | `/api/v1/grades/publish` | Publier/verrouiller la saisie pour la période |
| `GET` | `/api/v1/grades/calculate` | Recalcul des moyennes/totaux |

## 9. API Bulletins

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/report-cards/generate` | Générer un bulletin (A5, QuestPDF) |
| `GET` | `/api/v1/report-cards/{id}/print` | Impression |
| `POST` | `/api/v1/report-cards/{id}/publish` | Publier le bulletin |

## 10. API Présences

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/attendance` | Enregistrer une absence |
| `GET` | `/api/v1/attendance/report` | Rapport d'absences |

## 11. API Paramètres

| Méthode | Route | Description |
|---|---|---|
| `GET` / `PUT` | `/api/v1/settings/school` | Paramètres établissement (logo, cachet, signature, format de date) |
| `GET` / `PUT` | `/api/v1/settings/grading` | Système de notation par niveau/classe |
| `GET` / `PUT` | `/api/v1/settings/registration-numbers` | Format des matricules |
| `GET` | `/api/v1/exports/school-data` | Export complet des données de l'établissement (remplace la « sauvegarde manuelle » locale — Volume 1.5 §3) |

**Fin du Volume 4.**
