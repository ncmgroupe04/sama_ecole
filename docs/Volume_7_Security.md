# SAMA ECOLE

# VOLUME 7 — Security Architecture Specification (SAS) & Matrice des Rôles et Permissions

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0 (précédemment noyée dans un fichier partagé avec les Volumes 6, 8 et 9)
**Changements de cette version :** ce volume est désormais **autonome** (un fichier = un volume, cf. Volume 0 §0.13) ; suppression des références « poste de travail (version locale) » et « mode hors ligne » de la gestion des sessions et des licences.

---

## Table des matières

**Partie A — Architecture de sécurité**
1. Objectif et principes
2. Authentification
3. Gestion des rôles
4. Permissions
5. Sessions
6. Chiffrement
7. Journal d'audit
8. Protection des données et isolation multi-tenant
9. Sécurité des API
10. Sécurité des fichiers
11. Sauvegarde et restauration
12. Gestion des abonnements et licences

**Partie B — Matrice des Rôles et Permissions (RPM)**
13. Rôles officiels
14. Convention des permissions
15. Matrice détaillée par module
16. Permissions spéciales et double confirmation
17. Évolutivité de la matrice

---

# Partie A — Architecture de sécurité

## 1. Objectif et principes

La sécurité est intégrée dès la conception (« Security by Design »), jamais ajoutée après coup. Principes appliqués : **Least Privilege**, **Zero Trust**, **Defense in Depth**, **Secure by Default**, **Separation of Duties**. Chaque utilisateur ne possède que les droits strictement nécessaires à son rôle.

## 2. Authentification

- **ASP.NET Core Identity** + **JWT Bearer** (Volume 4 §1).
- Connexion par email + mot de passe.
- **Politique de mot de passe :** minimum 12 caractères, au moins une majuscule, une minuscule, un chiffre, un caractère spécial ; interdiction des suites simples (`123456`, `password`) et des données personnelles évidentes (prénom, nom, nom de l'école).
- Verrouillage du compte après 5 tentatives échouées consécutives (durée configurable).
- Le Directeur peut configurer l'expiration périodique des mots de passe et l'historique des anciens mots de passe (interdiction de réutilisation immédiate).

## 3. Gestion des rôles

Rôles hiérarchisés en visibilité (pas en héritage automatique de droits, voir §16) :

```
Super Admin → Directeur → Secrétariat / Finance / Enseignant
```

Le Super Admin ne gère jamais la pédagogie ou la finance d'une école. Le Directeur administre uniquement son propre établissement (isolation garantie par le multi-tenant, §8).

## 4. Permissions

Chaque fonctionnalité est protégée par une permission nommée `Module.Action` (ex. `Students.Read`, `Finance.Export`, `ReportCards.Publish`, `Users.Suspend`). Aucune action n'est exécutée sans vérification explicite de permission via **ASP.NET Core Policy-Based Authorization**, piloté par la matrice de la Partie B.

## 5. Sessions

Chaque session possède : identifiant unique, heure de connexion, heure de dernière activité, adresse IP, agent utilisateur (navigateur/appareil). Fermeture automatique après une période d'inactivité configurable (défaut 15 minutes, Volume 1 §1.3).

## 6. Chiffrement

- Mots de passe hachés avec ASP.NET Core Identity (PBKDF2, évolutif vers Argon2 si besoin).
- Toutes les communications en **HTTPS/TLS** obligatoire, sans exception (plus de distinction « LAN sans TLS » : tout trafic transite désormais par Internet, Volume 0 §0.8).
- Informations d'abonnement signées pour empêcher toute falsification côté client.
- Sauvegardes chiffrées au repos (AES-256) — détail opérationnel au Volume 9.

## 7. Journal d'audit

Toute opération sensible est enregistrée : connexion/déconnexion, création, modification, suppression logique, restauration, impression, export, changement de mot de passe, suspension/activation d'utilisateur.

Informations enregistrées : utilisateur, date/heure UTC, module, action, résultat, adresse IP. Les journaux sont **consultables mais jamais modifiables** (append-only), y compris par un administrateur.

## 8. Protection des données et isolation multi-tenant

Le système protège : données des élèves, des parents/tuteurs, données financières, notes, bulletins, abonnements. **Aucun utilisateur, y compris un Super Admin en usage courant, ne peut consulter les données pédagogiques ou financières d'une école qu'il n'administre pas.**

Le mécanisme technique garantissant cette isolation est spécifié au Volume 3 §2 (Row-Level Security PostgreSQL + Global Query Filter EF Core) — ce chapitre n'en donne que le principe de sécurité ; le Volume 3 en donne l'implémentation.

## 9. Sécurité des API

- HTTPS obligatoire sur toutes les routes, sans exception.
- Authentification et vérification de permission systématiques.
- Limitation de la taille des requêtes et **rate limiting** par utilisateur/IP (protection contre le brute force et les abus).
- Journalisation des appels sensibles (Volume 4 §0.6).
- Une future API publique (intégrations tierces) utilisera un système de clé API distinct, avec des quotas propres.

## 10. Sécurité des fichiers

Formats autorisés : PDF, PNG, JPG/JPEG, XLSX. Chaque fichier importé est vérifié sur : extension réelle (pas seulement déclarée), type MIME, taille maximale, contenu (scan antivirus recommandé en V1.1). Les noms de fichiers sont systématiquement renommés côté serveur (jamais le nom fourni par l'utilisateur, pour éviter les attaques par traversée de chemin).

## 11. Sauvegarde et restauration

Seuls le Directeur (pour son établissement) et le Super Admin (en maintenance plateforme) peuvent déclencher une restauration. Toute restauration crée automatiquement une entrée dans le journal d'audit. Détail opérationnel complet : Volume 9.

---

## 12. Gestion des abonnements et licences

Le contrôle de licence est désormais **une vérification serveur systématique à chaque requête sensible** (et non un mécanisme devant « fonctionner même hors ligne » comme dans la version 1.0 de ce document — cette exigence n'a plus de sens pour une application qui n'existe qu'en ligne). Le système prévoit : vérification de la date d'expiration, période de grâce configurable, passage en mode restreint puis blocage progressif (Volume 1 §11.3), conservation intégrale des données en toutes circonstances.

## 12bis. Inscription self-service et paiement des abonnements

**Restriction d'accès `AwaitingPayment`** : le middleware d'autorisation qui applique déjà le mode restreint à l'expiration (§12) doit appliquer exactement la même logique tant que `Subscriptions.Status = AWAITING_PAYMENT` — un Directeur nouvellement approuvé n'a accès qu'aux endpoints `/subscriptions/{schoolId}/payments` et à son propre profil, à l'exclusion de tout autre module. Cette vérification se fait à chaque requête, jamais seulement à la connexion (un token JWT émis avant paiement reste valide après paiement : le statut de l'abonnement, pas le contenu du token, gouverne l'accès).

**Vérification de signature webhook — non négociable** : toute requête reçue sur `/webhooks/payments/{provider}` doit voir sa signature HMAC vérifiée avec la clé secrète du fournisseur **avant** tout traitement. Une signature absente ou invalide entraîne un rejet `401` et une entrée d'audit, sans aucune exception de contournement en environnement de développement (utiliser le bac à sable/sandbox de l'agrégateur pour les tests, jamais un webhook non vérifié).

**Aucune confiance dans le client pour l'état d'un paiement** : le champ `SubscriptionPayments.Status` ne peut être positionné à `CONFIRMED` que par le traitement serveur du webhook. Aucune route accessible au Directeur ne permet de déclarer un paiement comme effectué — une simple redirection navigateur après paiement n'est qu'une indication d'expérience utilisateur, jamais une preuve de paiement.

**Déduplication** : `SubscriptionPayments.ProviderTransactionRef` est unique — un webhook reçu deux fois (retry de l'agrégateur) ne doit jamais produire une double confirmation ni une double extension de la date d'expiration.

**Protection du formulaire public d'inscription** (`POST /registration-requests`) : rate limiting par IP, captcha ou honeypot obligatoire, et le mot de passe soumis est haché immédiatement côté serveur — jamais transmis en clair au-delà de la requête HTTPS initiale, jamais journalisé (y compris dans les logs d'erreur).

---

# Partie B — Matrice des Rôles et Permissions (RPM)

## 13. Rôles officiels

| Rôle | Description |
|---|---|
| Super Administrateur | Éditeur de la plateforme, gestion des écoles et des abonnements |
| Directeur | Administrateur de son établissement |
| Secrétariat | Gestion administrative et des inscriptions |
| Finance | Gestion financière |
| Enseignant | Gestion pédagogique |

## 14. Convention des permissions

Format : `Module.Action` — ex. `Students.Read`, `Students.Create`, `Finance.Export`, `ReportCards.Publish`, `Settings.Update`.

## 15. Matrice détaillée par module

**Tableau de bord**

| Permission | Super Admin | Directeur | Secrétariat | Finance | Enseignant |
|---|---|---|---|---|---|
| Voir le tableau de bord | ✔ | ✔ | ✔ | ✔ | ✔ |

**Gestion des écoles (plateforme)**

| Action | Super Admin | Directeur |
|---|---|---|
| Créer une école | ✔ | ✖ |
| Modifier une école | ✔ | ✖ |
| Suspendre / réactiver une école | ✔ | ✖ |

**Élèves**

| Action | Super Admin | Directeur | Secrétariat | Finance | Enseignant |
|---|---|---|---|---|---|
| Voir | ✔ | ✔ | ✔ | ✔ | ✔ |
| Créer / Modifier / Archiver | ✖ | ✔ | ✔ | ✖ | ✖ |
| Import Excel/CSV | ✖ | ✔ | ✔ | ✖ | ✖ |
| Export PDF | ✖ | ✔ | ✔ | ✔ | ✖ |

**Enseignants**

| Action | Super Admin | Directeur | Secrétariat |
|---|---|---|---|
| Voir | ✔ | ✔ | ✔ |
| Créer / Modifier | ✖ | ✔ | ✔ |
| Suspendre | ✖ | ✔ | ✖ |

**Classes**

| Action | Directeur | Secrétariat | Enseignant |
|---|---|---|---|
| Voir / Consulter effectifs | ✔ | ✔ | ✔ |
| Créer / Modifier | ✔ | ✔ | ✖ |

**Inscriptions**

| Action | Directeur | Secrétariat | Finance |
|---|---|---|---|
| Nouvelle inscription / Réinscription | ✔ | ✔ | ✖ |
| Annulation | ✔ | ✔ | ✖ |
| Impression | ✔ | ✔ | ✔ |

**Finance**

| Action | Directeur | Finance |
|---|---|---|
| Encaisser / Dépenses | ✔ | ✔ |
| Voir statistiques / Export | ✔ | ✔ |
| **Modifier un montant dû issu d'une inscription** | ✔ | **✖** (Volume 1 §7.2) |

**Notes**

| Action | Directeur | Enseignant |
|---|---|---|
| Voir | ✔ | ✔ |
| Saisir | ✖ | ✔ |
| Modifier | ✔ | ✔ * |
| Valider / Publier | ✔ | ✖ |

\* Jusqu'à la publication du bulletin, qui verrouille la saisie (Volume 2 §5.2).

**Bulletins**

| Action | Directeur | Enseignant |
|---|---|---|
| Générer / Imprimer | ✔ | ✔ |
| Publier | ✔ | ✖ |

**Paramètres de l'école**

| Action | Directeur |
|---|---|
| Informations, année scolaire, notation, matricules, mentions, utilisateurs | ✔ |
| Export de données (remplace « Sauvegardes/Restaurations » de la v1.0, désormais automatisées côté infrastructure — Volume 9) | ✔ |

**Abonnements**

| Action | Super Admin | Directeur |
|---|---|---|
| Voir | ✔ | ✔ (lecture seule) |
| Modifier / Suspendre / Réactiver | ✔ | ✖ |

**Journaux d'audit**

| Action | Super Admin | Directeur |
|---|---|---|
| Consulter / Exporter | ✔ | ✔ (périmètre de son école) |
| Modifier / Supprimer | ✖ | ✖ |

## 16. Permissions spéciales et double confirmation

Certaines opérations exigent une nouvelle saisie du mot de passe : restauration d'une sauvegarde, publication des bulletins, clôture de l'année scolaire, suppression logique de données sensibles, changement de l'année scolaire active.

## 17. Évolutivité de la matrice

Les rôles ne s'héritent pas automatiquement : chaque rôle reçoit explicitement ses permissions, ce qui simplifie les audits et évite les droits involontaires. Nouveaux rôles anticipés (roadmap) : Bibliothécaire, Surveillant général, Comptable, Responsable des examens, Parent d'élève, Élève.

**Fin du Volume 7.**
