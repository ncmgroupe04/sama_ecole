# SAMA ECOLE

# VOLUME 4 — API Design Specification (ADS)

**Version :** 2.1
**Statut :** Document de référence — remplace la version 1.0
**Changements de la v2.0 :** ajout du chapitre Authentification (absent de la v1.0) ; suppression des références à la synchronisation locale/LAN/SaaS et au poste de travail local (§0.11, §0.17 de la v1.0), non pertinentes pour une plateforme en ligne.
**Changements de la v2.1 (27/07/2026) — rattrapage documentaire :** ajout des chapitres 12 à 20, qui décrivent des modules **déjà livrés en production** et jusqu'ici absents de ce volume (Paie, Caisse, Trésorerie, Fiscalité, Discipline & Convocations, Infrastructures, Documents administratifs, Emploi du temps & Pointage, Notifications sortantes SMS/WhatsApp). Ces chapitres documentent l'existant : ils ne décrivent aucune route à construire. Le §11 signale par ailleurs l'export global de données comme **reporté** (voir Volume 1.5 §8).

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
12. API Paie & Bulletins de salaire
13. API Caisse & Journal de caisse
14. API Trésorerie & Décaissements
15. API Fiscalité & TVA
16. API Discipline & Convocations
17. API Infrastructures (Bâtiments & Salles)
18. API Documents administratifs
19. API Emploi du temps & Pointage enseignants
20. API Notifications sortantes (SMS / WhatsApp)
21. API Inventaire (patrimoine, stock, prêts de matériel)

> **Chapitres 12 à 21 — modules livrés.** Contrairement aux chapitres 1 à 11, rédigés avant construction, ces chapitres ont été écrits **après** la mise en production pour rattraper l'écart entre le code et la documentation. Les routes qui y figurent sont celles réellement exposées par les contrôleurs de `src/SamaEcole.Web/Controllers/` ; en cas de divergence future, le code fait foi et ce volume doit être corrigé.

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

Toute erreur non gérée par un contrôleur remonte à `ExceptionHandlingMiddleware`, qui renvoie
systématiquement cette enveloppe (jamais une exception brute, jamais un format `ProblemDetails`
standard) :

```json
{
  "code": "VALIDATION_ERROR",
  "message": "Une ou plusieurs erreurs de validation se sont produites.",
  "details": { "lastName": ["Le nom est obligatoire."] },
  "traceId": "0HN...`"
}
```

`details` est `null` sauf pour `VALIDATION_ERROR` (dictionnaire champ → messages, issu de
`FluentValidation`). `traceId` correspond à `HttpContext.TraceIdentifier` et sert à corréler avec les
logs serveur (Volume 7).

| Code | Signification | Statut HTTP |
|---|---|---|
| `VALIDATION_ERROR` | Erreur de saisie (FluentValidation) | 422 |
| `CONCURRENCY_CONFLICT` | Écriture concurrente (RowVersion/xmin) — règle #5 | 409 |
| `BUSINESS_RULE_VIOLATION` | Règle métier bloquant l'opération vu l'état de la ressource | 409 |
| `INVALID_CREDENTIALS` | Authentification échouée (message volontairement générique) | 401 |
| `FORBIDDEN` | Permission insuffisante | 403 |
| `NOT_FOUND` | Ressource inexistante | 404 |
| `CLIENT_CLOSED_REQUEST` | Requête annulée côté client | 499 |
| `PAYMENT_PROVIDER_ERROR` | Agrégateur de paiement (PayDunya/CinetPay) injoignable ou en erreur | 502 |
| `INVALID_WEBHOOK_SIGNATURE` | Signature HMAC de webhook absente ou invalide (§12bis) | 401 |
| `INTERNAL_ERROR` | Erreur interne non anticipée | 500 |

Toute erreur est journalisée (Volume 7, journalisation de sécurité) ; seul `INTERNAL_ERROR` est loggé
en `Error` avec la stack trace complète côté serveur — jamais renvoyée au client.

Exception documentée : les endpoints de génération de PDF (reçus, bulletins, certificats, billets…)
utilisent le format RFC 7807 `ProblemDetails` natif d'ASP.NET Core (`{type, title, status, detail}`)
plutôt que cette enveloppe, car ils sont généralement ouverts par navigation directe du navigateur et
non par le client `fetch()` JSON. Leur champ `detail` est toujours un message générique — jamais le
message d'exception brut.

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

## 2ter. API Annuaire public des établissements (B2C)

Vitrine grand public : la seule surface de l'application servie à un visiteur n'appartenant à aucun établissement. **Lecture seule et entièrement anonyme** — aucun verbe d'écriture n'y est exposé.

| Méthode | Route | Auth | Description |
|---|---|---|---|
| `GET` | `/api/v1/public/schools` | **Aucune** (public) | Annuaire paginé des établissements ayant consenti. Filtres : `search` (nom), `city`, `region`, `cycle`, `page`, `pageSize` (≤ 48) |
| `GET` | `/api/v1/public/schools/{schoolId}` | **Aucune** (public) | Fiche d'un établissement de l'annuaire |

**Consentement explicite.** Une école n'y figure que si son Directeur a activé `isPubliclyListed` via `PUT /api/v1/schools/current` (§11) — **faux par défaut**. L'inscription, l'approbation par le Super Admin et l'activation de l'abonnement ne publient jamais un établissement : seul un geste délibéré de son Directeur le fait. La ville est obligatoire pour publier (premier critère de recherche d'un parent).

**Étanchéité — trois barrières indépendantes** (voir Volume 7 §8) :

1. **La RLS échoue en fermeture.** Une requête anonyme n'a pas de tenant : `app.current_school_id` est absente, les policies des tables élèves/notes/paiements comparent leur `SchoolId` à `NULL` et ne renvoient aucune ligne. Ce n'est pas un filtre applicatif qu'on aurait pensé à écrire, c'est le comportement par défaut de la base.
2. **La vue `public_school_directory`** (migration `AddPublicSchoolDirectory`) fige la liste des colonnes servies et la condition de consentement. Les Handlers n'interrogent qu'elle, jamais la table `schools`. NINEA, RCCM, statut, champs d'audit et en-têtes administratifs (IA/IEF) sont hors de portée, même d'un `SELECT *`.
3. **L'API Data de Supabase (PostgREST) est fermée** : schéma `public` retiré des schémas exposés, aucun droit sur `anon`/`authenticated`. L'annuaire n'est joignable que par ce contrôleur, donc toujours à travers le rate limiting et la journalisation.

**Cycles proposés** (« filières ») : dérivés des classes réellement ouvertes par l'établissement, jamais d'une liste déclarative qui deviendrait fausse dès la rentrée suivante. La vue les agrège hors RLS — contournement nécessaire (un visiteur anonyme n'a pas de tenant) et borné à des libellés de cycle pour des écoles consentantes.

**404 plutôt que 403** sur la fiche d'une école non listée : un 403 confirmerait son existence, ce que refuse précisément un établissement ayant choisi de ne pas être publié.

> **Périmètre.** Cet annuaire n'ouvre aucun accès aux données d'un établissement (ni élèves, ni notes, ni présences, ni finances). Il ne constitue donc pas le « portail Parents et Élèves » reporté en V3 (Volume 1 §13) : c'est une vitrine d'établissements, au même titre qu'une plaquette.

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
| `GET` | `/api/v1/students/export/pdf` | « LISTE DES ÉLÈVES » en PDF (filtres `classroomId`, `activeYearOnly`) — Directeur/Secrétariat/Finance |
| `GET` | `/api/v1/teachers/export/pdf` | « LISTE DES ENSEIGNANTS » en PDF (filtre `status`) — Super Admin/Directeur/Secrétariat, **jamais Finance** |
| *(mêmes endpoints en `/teachers`)* | | |

> Les deux exports PDF partagent les largeurs de colonnes de `PdfColumnWidths` (Infrastructure/Documents) :
> un matricule, une date ou un téléphone occupent la même largeur sur tous les documents imprimables de
> la plateforme, et les téléphones passent tous par `PhoneFormatter.FormatSenegal`.

## 5. API Classes & Matières

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/classrooms` | Créer une classe (aucune liste figée, Volume 1 §5.1). `series` optionnelle (catalogue `GET /coefficients/catalog` : séries du Baccalauréat, anciens codes L1/TECH acceptés) — une série avec modèle national reçoit son programme (`class_subjects`) et ses coefficients officiels, rapportés dans `template` : Lycée seulement, `422` sinon (Évolution N°4) |
| `GET` | `/api/v1/classrooms` | Lister avec effectifs (total, garçons, filles) |
| `POST` | `/api/v1/subjects` | Créer une matière |
| `GET` | `/api/v1/coefficients/catalog` | Catalogue fermé des séries de lycée (Directeur, Secrétariat) |
| `GET` | `/api/v1/coefficients?series=` ou `?classroomId=` | Grille des coefficients d'une série OU d'une classe (`schoolYearId` facultatif, défaut : année active) : base, surcharge, coefficient effectif, origine, `yearHasGrades`. Exactement une portée, sinon `422` (Directeur, Secrétariat) |
| `PUT` | `/api/v1/coefficients` | Poser ou corriger une surcharge sur l'année active. Correction : `rowVersion` obligatoire, `409` si périmé ou absent. `422` pour un coefficient hors ]0 ; 20], une classe ou une matière de primaire/maternelle (Directeur seul) |
| `DELETE` | `/api/v1/coefficients/{id}?rowVersion=` | « Rétablir » : suppression logique de la surcharge (Directeur seul) |
| `POST` | `/api/v1/coefficients/apply-template` | « Appliquer le modèle » national d'une série (`overwrite` facultatif). `422` pour une série sans modèle (TECH). Ne modifie jamais `Subject.Coefficient` (Directeur seul) |
| `POST` | `/api/v1/coefficients/carry-over` | « Reprendre l'année précédente » : recopie les surcharges d'une autre année vers l'année active sans rien écraser (Directeur seul) |
| `GET` | `/api/v1/class-subjects?classroomId=` | Programme d'une classe (Évolution N°6) : matières, coefficient effectif de l'année active et origine, valeur officielle de la série, groupe d'options, nombre d'élèves par option, élèves sans option par groupe (Directeur, Secrétariat) |
| `POST` | `/api/v1/class-subjects` | Ajouter une matière propre à l'établissement : `subjectId` OU `newSubjectName`, `coefficient` et `optionGroup` facultatifs. `422` au primaire/maternelle ou si la matière figure déjà au programme (Directeur seul) |
| `PUT` | `/api/v1/class-subjects/{id}` | `isActive`, `optionGroup`, `rowVersion` (xmin) — `409` si périmé (Directeur seul) |
| `POST` | `/api/v1/class-subjects/reset` | « Réinitialiser aux coefficients officiels du Sénégal » (`classroomId`) : programme et coefficients de classe du modèle, année active. `422` sans série ou sans modèle (Directeur seul) |
| `POST` | `/api/v1/class-subjects/assign-default-options` | Donne l'option par défaut aux élèves de la classe sans choix, année active (Directeur, Secrétariat) |
| `GET` | `/api/v1/class-subjects/options?classroomId=` ou `?studentId=` | Groupes d'options d'une classe (inscription) ou de la classe d'un élève avec ses choix (fiche) ; option par défaut = la plus fréquente de l'établissement (Directeur, Secrétariat) |
| `PUT` | `/api/v1/class-subjects/students/{studentId}/options` | Options de l'élève pour l'année active (`classSubjectIds` : liste complète, une par groupe au plus). `422` pour une option hors classe ou deux options d'un même groupe (Directeur, Secrétariat) |

## 6. API Inscriptions

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/enrollments` | Créer une inscription/pré-inscription. `subjectOptionIds` facultatif (Évolution N°6) : options retenues, une par groupe ; un groupe sans choix reçoit l'option par défaut. Enregistrées dans la même transaction |
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
| `POST` | `/api/v1/grades` | Saisir une note (validation de plage selon `GradingSettings`) — Directeur, Secrétariat, Enseignant |
| `PUT` | `/api/v1/grades/{id}` | Corriger une note (`rowVersion` obligatoire). Enseignant : dans la fenêtre `gradeEditWindowDays` ET auteur ou affecté, `403` sinon ; Directeur/Secrétariat : sans limite |
| `GET` | `/api/v1/grades/sheet/print` | Fiche de saisie papier, PDF vierge (`classroomId`, `subjectId`, `termId`, `evaluationType` obligatoires) — élèves par ordre alphabétique, cases Note et Appréciation vides |
| `POST` | `/api/v1/grades/publish` | Publier/verrouiller la saisie pour la période |
| `GET` | `/api/v1/grades/calculate` | Recalcul des moyennes/totaux |

## 9. API Bulletins

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/report-cards/generate` | Générer un bulletin (A5, QuestPDF) |
| `GET` | `/api/v1/report-cards/{id}/print` | Impression |
| `POST` | `/api/v1/report-cards/{id}/publish` | Publier le bulletin |
| `GET` | `/api/v1/report-cards/class-deliberation/pdf?classroomId=&termId=` | PV du conseil de classe d'une PÉRIODE : en-tête institutionnel, tableau Filles/Garçons/Total (effectif, présents, classés, admis, taux de réussite), distinctions, décisions (Directeur, Enseignant, Secrétariat) |
| `GET` | `/api/v1/report-cards/class-deliberation/annual/pdf?classroomId=&schoolYearId=` | PV ANNUEL (Évolution N°7) : moyennes/rangs annuels, décisions saisies ou « Proposé : … » (Directeur, Enseignant, Secrétariat) |
| `POST` | `/api/v1/report-cards/council-decisions/apply-proposals` | Enregistre la décision proposée pour les élèves sans décision sur la période (`classroomId`, `termId`) ; jamais d'écrasement (Directeur, Secrétariat) |
| `GET` / `PUT` | `/api/v1/report-cards/council-rules` | Seuils du conseil sur /20 : Félicitations, Tableau d'honneur, Encouragements, note éliminatoire, passage, redoublement. Lecture Directeur/Enseignant/Secrétariat, écriture Directeur ; `422` si incohérents |

## 10. API Présences

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/attendance` | Enregistrer une absence. `422` si la date est un jour de repos de l'établissement (`workingDays`, Évolution N°3). Avec `scheduleSlotId` (Évolution N°5), l'appel porte sur un cours de l'emploi du temps et la période est **dérivée** du cours |
| `GET` | `/api/v1/attendance/report` | Rapport d'absences |
| `GET` | `/api/v1/attendance/roster` | Feuille d'appel. `scheduleSlotId` facultatif (Évolution N°5) ; sans lui, `period` reste obligatoire (appel libre). Un billet d'entrée actif présélectionne l'élève en `Late` |
| `GET` | `/api/v1/attendance/slots?classroomId&date` | Cours de la classe ce jour-là (Évolution N°5). Un Enseignant ne reçoit que **ses** cours ; jour de repos → liste vide |
| `GET` | `/api/v1/absences/today-slots?studentId&date` | Cours du jour de la classe d'un élève, cours en cours / suivant signalés — sélecteur du cours visé par un billet d'entrée (SuperAdmin, Directeur, Surveillant) |
| `POST` | `/api/v1/absences/late-arrivals` | Retard. `targetScheduleSlotId` facultatif : émet un billet d'entrée visant ce cours (`Issued`). `409` si un billet actif existe déjà pour (élève, cours, jour) ; minutes ≤ 240 avec un cours visé |
| `POST` | `/api/v1/billets/{id}/accept` | L'enseignant **titulaire** du cours visé, ou le Directeur, accepte l'élève en classe. Idempotent ; `403` autre enseignant ; `422` billet annulé ou sans cours visé |
| `POST` | `/api/v1/billets/{id}/cancel` | Surveillant ou Directeur annule un billet non accepté ; la ligne d'appel retrouve son statut d'avant. `422` si déjà accepté |
| `GET` | `/api/v1/reports/attendance/by-subject` | Rapport d'assiduité par matière : séances appelées et répartition des statuts (Directeur, Secrétariat, SuperAdmin) |

> **Évolution N°5 — ce qui n'a pas changé.** Sans `scheduleSlotId`, `POST /attendance` et la feuille d'appel se
> comportent exactement comme avant (appel libre). Le rapport `GET /reports/attendance` gagne, en fin de ligne,
> `daysRecorded`, `fullAbsenceDays` et `partialAbsenceDays` — des **compteurs calculés**, pas de nouveaux statuts —
> et son taux de présence reste `(Présents + Retards) / lignes d'appel`. Détail des schémas : `openapi.yaml`.

## 11. API Paramètres

| Méthode | Route | Description |
|---|---|---|
| `GET` / `PUT` | `/api/v1/settings/school` | Paramètres établissement (logo, cachet, signature, format de date) |
| `GET` / `PUT` | `/api/v1/settings/grading` | Système de notation par niveau/classe |
| `GET` / `PUT` | `/api/v1/settings/registration-numbers` | Format des matricules |
| ~~`GET`~~ | ~~`/api/v1/exports/school-data`~~ | **Reporté après la V1** — voir la note ci-dessous |
| `GET` | `/api/v1/schools/current/mode` | Régime de l'établissement : `isLive`, `wentLiveAt`, `revertToTestAvailable` (drapeau d'environnement) |
| `POST` | `/api/v1/schools/current/go-live` | Bascule en mode réel (Directeur, mot « CONFIRMER » ou nom de l'école) — définitive |
| `POST` | `/api/v1/schools/current/reset-data` | « Zone de danger » : remise à neuf en mode test (Directeur, mot « PURGER » ou nom de l'école) |
| `DELETE` | `/api/v1/school-years/{id}` | Suppression d'une année scolaire (Directeur, **libellé exact** à recopier) |
| `POST` | `/api/v1/school-years/{id}/apply-evaluation-periods` | Rejoue sur l'année le découpage en périodes choisi dans les réglages (`evaluationPeriodType` : `Trimester` / `Semester` / `Custom` de 2 à 6). Directeur ; `422` dès qu'une note ou une appréciation de bulletin existe sur l'année, ou si elle est terminée |

> **Mode bac à sable, purge et suppression d'année — les trois gardes.** `reset_school_data` et
> `delete_school_year` sont les **seules** exceptions à la règle #6 (aucune suppression physique), et
> elles ne valent **qu'en mode test** : les deux fonctions PostgreSQL le revérifient elles-mêmes, en
> plus du Handler, parce qu'un `SECURITY DEFINER` est exempté de RLS et qu'un futur appelant pourrait
> oublier la règle. En **mode réel**, `reset-data` répond 409 `RESET_UNAVAILABLE_LIVE_MODE`, et
> `DELETE /school-years/{id}` n'accepte qu'une année **vide** — qu'il archive en suppression logique —
> et refuse les autres en 409 `SCHOOL_YEAR_HAS_DATA`. Le retour au mode test
> (`POST /schools/current/dev/revert-to-test`) n'est **monté** que sur les environnements jetables ;
> l'écran affiche malgré tout son bouton, désactivé, pour que l'absence s'explique. Périmètre exact de
> la purge et arbitrages : `ACTIVE_CONTEXT.md` §2.

> **Export global « Exporter mes données » — reporté.** Cette route était spécifiée mais n'a jamais été implémentée ; elle est actée pour la **version suivante** (Volume 1.5 §8). En V1, un Directeur qui doit sortir ses données dispose des exports **par domaine** déjà livrés, qui couvrent les besoins réels de reporting et d'archivage : rapport financier consolidé `.xlsx` (§14), export des présences (`GET /api/v1/reports/attendance/export`), import/export Excel des notes, et l'ensemble des PDF officiels du §18. La sauvegarde intégrale de la base reste, elle, une responsabilité d'infrastructure (Volume 9) — jamais une action utilisateur.

---

## 12. API Paie & Bulletins de salaire

Gestion des contrats du personnel (enseignants titulaires, vacataires, personnel administratif), calcul de la paie mensuelle et édition du bulletin de salaire PDF.

**Rôles :** `Directeur`, `Finance`. Aucun employé ne consulte sa propre fiche via l'API en V1 (pas de portail employé — Volume 1.5 §8).

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/finance/employee-contracts` | Liste des contrats du personnel |
| `POST` | `/api/v1/finance/employee-contracts` | Créer un contrat (type, salaire de base ou taux horaire, moyen de paiement) |
| `PATCH` | `/api/v1/finance/employee-contracts/{id}` | Réviser un contrat actif (salaire, taux, prime, moyen de paiement) |
| `GET` | `/api/v1/finance/payroll` | Liste des fiches de paie (filtrable par période) |
| `POST` | `/api/v1/finance/payroll` | Générer la fiche de paie d'un employé pour un mois donné |
| `GET` | `/api/v1/finance/payroll/{id}/pdf` | Bulletin de salaire (PDF A4) |
| `POST` | `/api/v1/finance/employee-contracts/{contractId}/hour-records` | Déclarer des heures effectuées (vacataires) |
| `GET` | `/api/v1/finance/employee-contracts/{contractId}/hour-records` | Relevé des heures déclarées |
| `GET` | `/api/v1/finance/employee-contracts/{contractId}/hour-records/sheet` | Fiche de suivi des heures (données) |
| `GET` | `/api/v1/finance/employee-contracts/{contractId}/hour-records/sheet/pdf` | Fiche de suivi des heures (PDF) |
| `GET` | `/api/v1/finance/employee-contracts/{contractId}/suggested-hours` | **Suggestion d'heures pour la paie** (ticket JGK-K01), rapprochée de l'emploi du temps |
| `GET` | `/api/v1/finance/employee-contracts/{contractId}/work-certificate` | Attestation de travail (données) |
| `GET` | `/api/v1/finance/employee-contracts/{contractId}/work-certificate/pdf` | Attestation de travail (PDF) |

**Règles :**
- Le calcul de la paie — brut (salaire de base, ou heures × taux horaire), retenues salariales (IPRES plafonnée, BRS), charges patronales (IPRES employeur, CSS plafonnée, CFCE), net à payer — est porté par `PayrollCalculator` dans la couche Application, jamais par le contrôleur ni par l'entité (règle #8 d'`AGENTS.md`).
- Les heures déclarées via `hour-records` constituent une **fiche de suivi vérifiable**, volontairement non branchée sur le calcul automatique de la paie : le montant dû à un vacataire reste saisi et validé par un humain.
- **`suggested-hours` est purement consultatif** (Volume 1 §14.3 amendé) : il agrège `hour-records` du mois et signale un écart avec l'emploi du temps planifié, mais n'écrit rien et ne modifie en rien le contrat de `POST /finance/payroll` — la Direction pré-remplit `hoursWorked` avec la valeur suggérée côté client, l'ajuste si besoin, puis soumet la commande existante inchangée.
- **`payoutMethod`** (`Cash`/`BankTransfer`/`Wave`/`OrangeMoney`) et **`payoutAccountReference`** (RIB/IBAN ou numéro mobile money, texte libre) sont distincts du `PaymentMethod` du module Finance élèves (§7) — deux domaines qui ne partagent jamais une énumération. Coordonnées jamais journalisées en clair (Volume 7).
- Un contrat n'est jamais supprimé physiquement (règle #6) : il est clôturé.

---

## 13. API Caisse & Journal de caisse

Ouverture/fermeture de la caisse de guichet, journal des encaissements de la journée et rapport de clôture.

**Rôles :** `Directeur`, `Finance`.

| Méthode | Route | Description |
|---|---|---|
| `POST` | `/api/v1/finance/sessions/open` | Ouvrir une session de caisse (fonds de caisse initial) |
| `POST` | `/api/v1/finance/sessions/{id}/close` | Clôturer la session — ne prend que l'identifiant de session |
| `GET` | `/api/v1/finance/sessions/{id}/closing-report` | Rapport de clôture (PDF) — ventilation par mode de paiement et par catégorie de frais, détail des transactions |
| `GET` | `/api/v1/finance/daily-cash-register/pdf` | Journal de caisse du jour (PDF, bordereau) |
| `GET` | `/api/v1/finance/dashboard` | Tableau de bord financier (encaissé jour/mois/année, recouvrement) |

**Règles :**
- La clôture **calcule** le solde de fermeture : `fonds d'ouverture + somme des paiements non annulés de la session`. Elle ne prend **aucun montant en paramètre**.
- Une session déjà close ne peut pas être re-clôturée : la seconde tentative est rejetée en `400`. Tout redressement passe par une écriture nouvelle, jamais par une modification rétroactive (règle #4).
- Les paiements annulés sont **exclus** du total, sans être effacés.
- Le rapport de clôture est reproductible à l'identique après coup : il se recalcule à partir des paiements de la session, il n'est pas figé dans un blob.

- **`POST /finance/payments` (l'encaissement lui-même) accepte un `idempotencyKey` optionnel** (ticket JGK-L01, résilience réseau — Volume 0 §0.8) : généré côté client à l'ouverture du formulaire, jamais régénéré à chaque tentative. Un retry après coupure réseau avec la MÊME clé ne crée jamais un second paiement — le serveur rejoue le résultat déjà produit (même `paymentId`/`receiptNumber`), y compris si la session de caisse a été close entre-temps.

> **Limite connue — pas de rapprochement de caisse.** La clôture n'enregistre pas le **montant physiquement compté** par le caissier, et ne calcule donc **aucun écart** (`compté − théorique`). Le solde produit est purement théorique. Le contrôle qui donne sa valeur à une caisse — confronter l'espèce comptée au calcul — reste à construire ; il suppose un champ « montant compté » dans la commande de clôture et sa persistance sur la session. À arbitrer avant d'annoncer un module Caisse complet.

---

## 14. API Trésorerie & Décaissements

Vision consolidée entrées/sorties et registre des décaissements (dépenses de l'établissement).

**Rôles :** `Directeur`, `Finance`, `SuperAdmin`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/finance/treasury` | Tableau de bord Trésorerie — encaissements et décaissements consolidés, solde |
| `GET` | `/api/v1/finance/disbursements` | Liste des décaissements (filtrable par période/catégorie) |
| `POST` | `/api/v1/finance/disbursements` | Enregistrer un décaissement |
| `DELETE` | `/api/v1/finance/disbursements/{id}` | Annuler un décaissement (suppression **logique**, règle #6) |
| `GET` | `/api/v1/finance/reports/revenue` | Consolidation des revenus sur une période (JSON) |
| `GET` | `/api/v1/finance/reports/revenue/excel` | Le même rapport au format comptable `.xlsx` (ClosedXML) |

**Règles :**
- `GET /finance/reports/revenue/excel` renvoie le fichier avec `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` et un `Content-Disposition: attachment`. Il est consommé par l'écran `/rapports/financiers`.
- Les bornes de période (`from`, `to`) de l'export `.xlsx` sont **exactement** celles du rapport affiché à l'écran : l'export est le même rapport dans un autre format, jamais un second calcul.
- Un décaissement ne modifie jamais le solde d'une inscription (règle #4) : les deux flux sont indépendants et ne se compensent pas.

---

## 15. API Fiscalité & TVA

Déclaration fiscale et sociale **mensuelle** : TVA collectée, TVA déductible, charges sociales issues des fiches de paie.

**Rôles :** `Directeur`, `Finance`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/finance/tax-declarations` | Liste des déclarations générées |
| `POST` | `/api/v1/finance/tax-declaration` | Générer la déclaration d'un mois (`month`, `year`) |
| `GET` | `/api/v1/finance/tax-declarations/{id}/pdf` | Déclaration au format PDF officiel |

**Règles :**
- La TVA n'est **pas recalculée après coup** : taux et montant sont portés **par transaction** (`Payment.VatRate/VatAmount`, `Disbursement.VatRate/VatAmount`) au moment de l'encaissement ou du décaissement. La déclaration ne fait que les sommer.
- Un paiement `Cancelled` est **exclu** de la TVA collectée ; un paiement `Partial` est **inclus** — c'est un encaissement réel.
- Une déclaration est un **instantané daté** des montants du mois.
- **Une seule déclaration par (mois, année)** : une seconde tentative est rejetée en `409` (`BusinessRuleException`), plutôt que d'écraser la première ou de créer un doublon indiscernable.

> **Limite connue — pas de circuit de rectification.** Si les données du mois changent après génération (paiement régularisé, fiche de paie corrigée), la déclaration existante ne peut être ni régénérée ni remplacée : ni annulation, ni déclaration rectificative. À arbitrer.

> **Limite connue — barèmes en dur.** Les taux et plafonds sociaux (IPRES salarié 5,6 % / employeur 8,4 %, CSS 7 %, CFCE 3 %, BRS 5 %, plafonds IPRES 360 000 et CSS 63 000 FCFA) sont des **constantes du code** (`PayrollCalculator`), et non des paramètres d'établissement. Un changement de barème par l'administration impose donc **une livraison logicielle**, et s'applique rétroactivement à tout recalcul. Les rendre paramétrables **avec date d'effet** — pour qu'un recalcul d'un mois passé conserve le barème de l'époque — est le prérequis avant un déploiement à grande échelle.

---

## 16. API Discipline & Convocations

Registre disciplinaire de la Vie scolaire et convocations des parents/tuteurs.

**Rôles :** `Directeur`, `Surveillant`, `SuperAdmin`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/discipline` | Liste des faits disciplinaires |
| `POST` | `/api/v1/discipline` | Enregistrer un fait disciplinaire |
| `GET` | `/api/v1/discipline/{id}/pv` | Procès-verbal de discipline (données) |
| `GET` | `/api/v1/discipline/{id}/pv/pdf` | Procès-verbal de discipline (PDF officiel) |
| `GET` | `/api/v1/parent-summons` | Liste des convocations |
| `POST` | `/api/v1/parent-summons` | Créer une convocation de parent/tuteur |
| `PATCH` | `/api/v1/parent-summons/{id}/outcome` | Consigner la suite de l'entretien |
| `GET` | `/api/v1/parent-summons/{id}/notice` | Avis de convocation (données) |
| `GET` | `/api/v1/parent-summons/{id}/notice/pdf` | Avis de convocation (PDF officiel) |

**Suite de l'entretien** — `PATCH /api/v1/parent-summons/{id}/outcome`

```json
{ "outcome": "Honored | Missed | Postponed", "outcomeNotes": "Le père s'est présenté." }
```

- `outcome` est obligatoire et ne peut pas valoir `Scheduled` : c'est l'état de départ, pas une suite.
- `outcomeNotes` est **obligatoire pour `Missed` et `Postponed`** (une absence ou un report appellent une suite écrite), facultatif pour `Honored` — même arbitrage que `DiscrepancyReason` à la clôture de caisse, qui n'exige un motif que lorsqu'un écart existe réellement.
- Une convocation déjà close renvoie **422**, jamais un écrasement : la correction se fait en émettant une nouvelle convocation.

**Règles :**
- Une convocation n'est **pas un portail parent** : c'est un document interne imprimé et remis en main propre ou envoyé. Elle ne crée aucun compte, n'ouvre aucun accès en consultation, et ne préfigure pas le §13 du Volume 1 (reporté en V3).
- Un fait disciplinaire enregistré n'est jamais effacé (règle #6) — une erreur de saisie se corrige par une mention rectificative tracée.
- **La suite d'une convocation obéit à la même intégrité** : elle se consigne une seule fois, depuis `Scheduled`. L'avis PDF reste le document remis **avant** l'entretien ; il ne porte donc jamais la suite, qui n'existe pas encore au moment où on l'imprime.
- **Aucun seuil n'émet de convocation automatiquement.** Le rapport d'assiduité (`GET /api/v1/reports/attendance`) propose l'action et pré-remplit le motif avec les retards et absences comptés sur la période affichée ; c'est le Directeur qui convoque.

---

## 17. API Infrastructures (Bâtiments & Salles)

Référentiel des locaux : bâtiments de l'établissement et salles qu'ils contiennent.

**Rôles :** lecture ouverte à tout utilisateur authentifié de l'école ; écriture réservée au `Directeur`/`Secretariat`/`SuperAdmin`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/buildings` | Liste des bâtiments (avec leurs salles) |
| `POST` | `/api/v1/buildings` | Créer un bâtiment |
| `PUT` | `/api/v1/buildings/{id}` | Modifier un bâtiment |
| `DELETE` | `/api/v1/buildings/{id}` | Supprimer un bâtiment (logique) |
| `POST` | `/api/v1/rooms` | Créer une salle rattachée à un bâtiment |
| `PUT` | `/api/v1/rooms/{id}` | Modifier une salle (nom, capacité) |
| `DELETE` | `/api/v1/rooms/{id}` | Supprimer une salle (logique) |

**Règles :**
- La lecture est volontairement ouverte à tous les rôles de l'établissement : un enseignant a besoin de connaître les salles pour lire son emploi du temps (§19).
- `GET /buildings` renvoie une **liste vide** (`200`), jamais une erreur `500`, sur un établissement dont la migration Infrastructures n'a pas encore été appliquée.

---

## 18. API Documents administratifs

Huit documents officiels PDF, générés côté serveur avec QuestPDF (mise en page A4/A5 à points fixes — aucun débordement de page possible, contrairement à une impression navigateur). Les documents de la famille Vie scolaire portent l'en-tête officiel M.E.N. (République / Ministère / IA / IEF) et un QR code anti-fraude ; ceux des familles Finance et Paie portent l'en-tête de l'établissement.

| # | Document | Route PDF | Famille |
|---|---|---|---|
| 1 | Certificat d'exéat | `GET /api/v1/enrollments/{id}/exeat/pdf` | Vie scolaire |
| 2 | Procès-verbal de discipline | `GET /api/v1/discipline/{id}/pv/pdf` | Vie scolaire |
| 3 | Avis de convocation parent | `GET /api/v1/parent-summons/{id}/notice/pdf` | Vie scolaire |
| 4 | Billet de sortie | `GET /api/v1/billets/early-departure/{id}/pdf` | Vie scolaire |
| 5 | Avis d'échéance / sommation pour impayés | `GET /api/v1/finance/enrollments/{enrollmentId}/dues-notice/pdf` | Finance |
| 6 | Engagement financier | `GET /api/v1/finance/commitments/{id}/pdf` | Finance |
| 7 | Attestation de travail | `GET /api/v1/finance/employee-contracts/{contractId}/work-certificate/pdf` | Paie |
| 8 | Fiche de suivi des heures | `GET /api/v1/finance/employee-contracts/{contractId}/hour-records/sheet/pdf` | Paie |

Chaque document expose en plus une route **sans** `/pdf` renvoyant les mêmes données en JSON, utilisée par l'aperçu à l'écran avant impression.

**Documents officiels antérieurs, hors de ce module mais de même facture :** reçu d'inscription (`/enrollments/{id}/receipt/pdf`), reçu de paiement (`/finance/payments/{id}/receipt/pdf`), certificat de scolarité (`/enrollments/{id}/certificate/pdf`), billet d'entrée (`/billets/late-arrival/{id}/pdf`), cartes scolaires d'une classe (`/classrooms/{id}/school-cards`), bulletin de notes et PV de délibération (Chapitre 9), journal de caisse (§13), déclaration fiscale (§15), bulletin de salaire (§12).

**Règles :**
- Le reçu d'inscription et le bulletin de notes suivent **exactement** `docs/design-references/` (règle #12) — aucune interprétation créative.
- Toute requête de document joint son établissement via une entité tenant (`Enrollment`, `EmployeeContract`, …). **Ne jamais** lire `Schools` directement pour récupérer l'en-tête : `School` n'implémente pas `ITenantEntity` (elle *définit* le tenant, elle ne lui appartient pas) et n'a donc aucun filtre automatique — le piège classique de fuite d'en-tête inter-écoles.
- Le QR code encode un identifiant de vérification, jamais des données personnelles.

---

## 19. API Emploi du temps & Pointage enseignants

Construction de l'emploi du temps hebdomadaire et pointage des présences enseignants.

**Rôles :** emploi du temps — `Directeur`, `Secretariat`, `Enseignant`, `SuperAdmin` ; pointage — `Directeur`, `Surveillant`, `SuperAdmin`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/schedules/teacher/{teacherId}` | Emploi du temps d'un enseignant |
| `GET` | `/api/v1/schedules/classroom/{classroomId}` | Emploi du temps d'une classe |
| `POST` | `/api/v1/schedules` | Créer un créneau. `422` si le jour est un jour de repos de l'établissement (`workingDays`), ou si l'enseignant, la classe ou la salle est déjà occupé(e) sur la plage |
| `PUT` | `/api/v1/schedules/{id}` | Modifier un créneau. `422` si le jour écrit est un jour de repos — y compris pour un créneau hérité d'un jour devenu repos, qui reste lisible et supprimable mais plus modifiable |
| `DELETE` | `/api/v1/schedules/{id}` | Supprimer un créneau |
| `GET` | `/api/v1/teacher-attendance?date=` | Pointage des enseignants pour une date |
| `POST` | `/api/v1/teacher-attendance` | Enregistrer un pointage. `422` si la date est un jour de repos de l'établissement (`workingDays`) |

**Règles :**
- **Contrôle de propriété (règle #10).** Un `Enseignant` ne peut créer ou modifier un créneau que pour **lui-même** : le `TeacherId` reçu dans le corps de la requête est rapproché de sa propre fiche via `Teacher.UserId`, jamais accepté sur parole. Une tentative pour un autre enseignant est rejetée (`403`). Le JWT ne porte que l'identifiant du **compte** — d'où la remontée compte → fiche. `Directeur`/`Secretariat`/`SuperAdmin` construisent l'emploi du temps de tout l'établissement et ne sont pas bornés.
- Un compte `Enseignant` non rattaché à une fiche enseignant est rejeté explicitement, avec un message qui indique l'action corrective (demander le rattachement au Directeur) plutôt qu'un `403` muet.
- **Détection de chevauchement** à la création comme à la modification : un créneau est refusé (`400`) s'il recouvre un créneau existant pour le même enseignant ou la même classe.
- Les créneaux proposés par un enseignant sont marqués `IsTeacherSubmitted` — ils restent distinguables de ceux posés par l'administration.

---

## 20. API Notifications sortantes (SMS / WhatsApp)

Communication **sortante** vers les familles. C'est ce que la V1 livre à la place du portail Parents/Élèves, reporté en V3 (Volume 1 §13, Volume 1.5 §8.1).

**Rôles :** `Directeur`, `Finance`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/sms/history` | Historique des SMS envoyés (statut, coût en segments, motif d'échec) + solde et nombre de messages en file |
| `POST` | `/api/v1/sms/dues-reminders` | Relance d'impayés par SMS, cadrable à une classe (`classroomId` optionnel) |
| `POST` | `/api/v1/webhooks/sms/{provider}` | **Public** — accusés de réception (DLR) de l'agrégateur. Signature HMAC obligatoire |

Les autres envois ne sont pas des endpoints : ils partent en **réaction à un événement métier** — absence ou retard enregistré (SMS + WhatsApp), paiement encaissé (SMS), bulletin mis à disposition (SMS d'avis + WhatsApp/e-mail pour le PDF).

**File d'attente.** Un déclencheur métier n'appelle jamais l'agrégateur lui-même : il **inscrit** le message en file (`Pending`) et rend la main. `SmsQueueHostedService` le remet ensuite hors requête, avec report exponentiel et abandon au bout de `MaxAttempts`. Trois raisons : une relance sur 400 familles ne tient plus la requête HTTP ouverte ; une panne de l'agrégateur devient un envoi différé et non une alerte perdue ; et la saisie d'un retard n'attend plus un service tiers pour se conclure.

Cycle de vie : `Pending` → `Sent` (accepté par l'agrégateur) → `Delivered` (accusé de réception) ou `Failed`. `Sent` et `Delivered` sont distincts à dessein — seul le second atteste qu'un parent a reçu l'alerte.

**Règles :**
- Point de passage unique `SmsDispatcher` : **aucun** envoi ne le contourne. Il contrôle, dans l'ordre, le numéro du tuteur, la formule de l'école (`Feature.SmsNotifications`, Premium), l'activation du type d'alerte dans les paramètres de l'école, puis le solde de crédits.
- **Débit atomique du solde, dès la mise en file** : la condition sur le solde et la décrémentation sont un seul `UPDATE` exécuté par PostgreSQL. Débiter à la remise laisserait une relance de masse accepter mille messages sur un solde de cent. Ce qui ne part finalement pas (abandon, accusé négatif) est **recrédité**.
- Le worker et le webhook DLR s'exécutent **sans tenant** : ils passent par trois fonctions PostgreSQL `SECURITY DEFINER` au périmètre étroit (`claim_pending_sms`, `settle_sms_attempt`, `apply_sms_delivery_receipt`), jamais par le rôle propriétaire — qui désactiverait la RLS de toute la base en silence (règle #2).
- La réclamation d'un lot pose un **bail** (`FOR UPDATE SKIP LOCKED` + `NextAttemptAt` repoussé) : deux instances de l'application ne peuvent pas envoyer deux fois le même SMS au parent.
- Tout message est **historisé** avec son statut (`Pending`, `Sent`, `Delivered`, `Failed`, `InsufficientCredit`) et son coût.
- `POST /sms/dues-reminders` **ne renvoie pas d'erreur** si certains SMS ne partent pas : il retourne `{ queuedCount, skippedCount, firstSkipReason }`. `queuedCount` compte les messages **acceptés en file**, jamais les messages remis — la remise est asynchrone et inconnue au retour de la requête.
- La relance ne vise que les inscriptions `Confirmed` présentant un reliquat.
- Le webhook DLR est **fermé par défaut** : sans `Sms__WebhookSecret`, toute signature est rejetée (401). Un accusé sans correspondance (rejeu, message inconnu) renvoie `200` — un statut d'échec ferait réémettre l'agrégateur en boucle.

---

## 21. API Inventaire (patrimoine, stock, prêts de matériel)

Suivi du patrimoine de l'établissement, commun aux écoles **publiques** (tables-bancs, manuels d'État, matériel pédagogique, consommables) et **privées** (parc informatique, tenues en stock, matériel de laboratoire et de sport, fournitures administratives), et traçabilité des biens confiés aux élèves et au personnel.

**Rôles :**

| Périmètre | Rôles | Pourquoi |
|---|---|---|
| Lecture (catalogue, journal, prêts) | Tout utilisateur authentifié | Un enseignant doit pouvoir vérifier ce qui lui a été confié sans passer par le secrétariat |
| Catalogue (créer/corriger/archiver une catégorie ou un bien) | `Directeur`, `Secretariat` | Administration du patrimoine — même matrice que Classes et Infrastructures |
| Mouvements et prêts | `Directeur`, `Secretariat`, `Surveillant` | C'est le surveillant qui distribue les manuels à la rentrée et les récupère en juin |
| Fiche d'inventaire global (PDF) | `Directeur`, `Secretariat` | Export du patrimoine complet — opération sensible, journalisée à l'audit (Volume 7 §7) |

Le module est **accessible à toutes les formules d'abonnement** : aucun contrôle `Feature`.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/inventory/categories` | Familles de biens de l'école, avec nombre de biens et effectif cumulé |
| `POST` | `/api/v1/inventory/categories` | Créer une famille (nomenclature libre, jamais figée) |
| `PUT` | `/api/v1/inventory/categories/{id}` | Corriger le libellé/la description. Verrou optimiste |
| `DELETE` | `/api/v1/inventory/categories/{id}` | Archiver. **409** si des biens y sont rattachés |
| `GET` | `/api/v1/inventory/items` | Catalogue paginé, filtres `categoryId`, `roomId`, `condition`, `search`, `outOfStockOnly` |
| `GET` | `/api/v1/inventory/items/{id}` | Fiche + 20 derniers mouvements + prêts en cours |
| `POST` | `/api/v1/inventory/items` | Créer un lot. `initialQuantity` devient un mouvement d'entrée |
| `PUT` | `/api/v1/inventory/items/{id}` | Corriger la fiche — **jamais** les quantités |
| `DELETE` | `/api/v1/inventory/items/{id}` | Archiver. **409** si des prêts sont en cours |
| `GET` | `/api/v1/inventory/movements` | Journal de stock, filtres `itemId`, `type`, `from`, `to` |
| `POST` | `/api/v1/inventory/movements` | Entrée, sortie, ajustement d'inventaire, mise au rebut |
| `GET` | `/api/v1/inventory/assignments` | Fiches de prêt, filtres `status`, `studentId`, `teacherId`, `overdueOnly` |
| `POST` | `/api/v1/inventory/assignments` | Prêter/attribuer des unités et produire la décharge |
| `POST` | `/api/v1/inventory/assignments/{id}/return` | Restitution, totale ou partielle |
| `DELETE` | `/api/v1/inventory/assignments/{id}` | Annuler une fiche saisie par erreur (contre-passation) |
| `GET` | `/api/v1/inventory/assignments/{id}/pdf` | **Fiche de décharge** — A5 paysage, QR de vérification |
| `GET` | `/api/v1/inventory/reports/global/pdf` | **Fiche d'inventaire global** — A4 paysage, filtrable par catégorie/salle |

**Un bien est un LOT, pas une unité.** Le suivi à l'unité se fait avec un lot de quantité 1 (un PC et son numéro de série), le suivi en masse avec un lot de quantité 200 (des tables-bancs). C'est ce choix qui permet aux deux cas d'usage — patrimoine d'État et parc informatique privé — de vivre dans le même modèle : un code-barres et un état par ligne n'auraient aucun sens sur 200 tables-bancs. Corollaire assumé : `condition` est l'état **dominant** du lot ; une école qui veut distinguer « 120 en bon état » de « 50 à réparer » crée deux lots et transfère par un ajustement.

**Règles :**
- **`quantityAvailable` n'est jamais écrit par un endpoint.** Il est maintenu uniquement dans la transaction d'un mouvement journalisé, sur l'entité chargée sous verrou `xmin` — exactement le traitement d'`Enrollment.AmountPaid` par la caisse. `PUT /inventory/items/{id}` n'expose même pas les quantités. Une contrainte `CHECK` (`0 ≤ disponible ≤ total`) est le dernier rempart en base.
- **Le journal est append-only**, comme `fee_change_history` : ni `PUT` ni `DELETE` sur un mouvement, et le rôle applicatif PostgreSQL n'a que `SELECT, INSERT` sur `stock_movements`. Une saisie erronée se corrige par un mouvement inverse — c'est ce qui rend l'inventaire opposable lors d'un contrôle de l'IEF ou de la mairie.
- **Le sens d'un mouvement vient de son type, jamais du signe** de la quantité, toujours strictement positive (`CHECK`). Chaque type correspond à une arithmétique sans ambiguïté sur le couple (total, disponible) : `Attribution` ne touche que le disponible — un bien prêté reste au patrimoine ; `PerteSurPret` ne touche que le total — ces unités étaient déjà sorties du disponible.
- **Attribution et Restitution ne sont pas saisissables** via `POST /inventory/movements` : elles sont produites exclusivement par les endpoints de prêt, pour qu'aucun appel direct ne fasse varier le disponible sans la fiche de décharge qui l'explique.
- **Ajustement** : le client envoie l'effectif **compté** lors de l'inventaire physique, pas un écart ; le serveur en déduit le sens et l'ampleur de la correction. Un comptage identique à la fiche est refusé en **422** — il n'y a pas de mouvement à écrire.
- Quantité insuffisante → **422** avec un message métier ; lecture périmée du bien ou de la fiche → **409** (règle #5), jamais un écrasement silencieux.
- Un **consommable** ne se prête pas (422) : il se distribue par une sortie de stock.
- Le **nom du bénéficiaire est figé** sur la fiche de prêt, comme `EnrollmentFeeLine` fige le barème : une décharge réimprimée trois ans plus tard doit être identique à celle qui a été signée.
- L'**emplacement** d'un lot peut être une `Room` du module Infrastructures (vérifiable) ou un libellé libre (« Réserve A »). Quand les deux sont renseignés, la salle prime à l'impression.
- Le **prix unitaire est indicatif** : aucune portée comptable, aucun amortissement, aucune écriture financière (règle #4). La valorisation ne s'imprime que si au moins un prix a été saisi.

---

## 22. API Examens officiels (CFEE/BFEM/BAC)

Constitution et suivi des dossiers de candidature aux examens officiels (CM2/CFEE, 3ème/BFEM, Terminale/BAC), de l'ouverture du dossier à la transmission à l'IEF/l'Inspection d'Académie et à la saisie des résultats de délibération.

**Rôles :**

| Périmètre | Rôles | Pourquoi |
|---|---|---|
| Lecture des dossiers (liste + fiche) | `Directeur`, `Secretariat`, `Enseignant` | Un dossier porte des données d'état civil sensibles (extrait de naissance) : l'`Enseignant` ne voit que les dossiers des classes où il a une affectation active sur l'année en cours (`TeacherAssignments`) — filtré par `ExamDossierScopeAuthorizer` (ticket JGK-J08, livré). Une URL tapée directement sur un dossier hors de ses classes renvoie 403, jamais 404. |
| Sessions, statistiques | `Directeur`, `Secretariat` | Hors du filtre par classe (JGK-J08 ne couvre que les dossiers) : une session ou une statistique ne se rattache pas à une seule classe. |
| Création/modification de dossier, contrôle d'état civil | `Directeur`, `Secretariat` | Constitution administrative du dossier — même matrice que les Inscriptions |
| Attribution centre/table, transmission, résultats | `Directeur`, `Secretariat` | Actes qui engagent l'établissement vis-à-vis de l'IEF/l'IA |
| Export ministériel, impression par lot, dispatch de convocations | `Directeur`, `Secretariat` | Opérations sensibles, journalisées à l'audit (Volume 7 §7) |

Le module est **accessible à toutes les formules d'abonnement** : aucun contrôle `Feature` — seul le dispatch de convocations par SMS/WhatsApp reste derrière `Feature.SmsNotifications` (Premium, Volume 1 §13) ; la génération PDF de la convocation, elle, reste libre.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/exams/sessions` | Sessions d'examen de l'école, filtrable `schoolYearId`, `examType` |
| `POST` | `/api/v1/exams/sessions` | Créer une session (année, type, série) |
| `PUT` | `/api/v1/exams/sessions/{id}` | Corriger centre par défaut / statut de la session. Verrou optimiste |
| `GET` | `/api/v1/exams/dossiers` | Dossiers, filtres `examSessionId`, `classroomId`, `status`, `search` |
| `GET` | `/api/v1/exams/dossiers/{id}` | Fiche complète du dossier |
| `POST` | `/api/v1/exams/dossiers` | Ouvrir un dossier pour un élève sur une session. `classroomId` figé à la création |
| `PUT` | `/api/v1/exams/dossiers/{id}` | Corriger état civil / centre déclaré. Verrou optimiste |
| `GET` | `/api/v1/exams/dossiers/audit` | Dossiers `Incomplet` d'une session, avec le détail des pièces/champs manquants |
| `POST` | `/api/v1/exams/dossiers/{id}/assign-center` | Attribuer centre et numéro de table. Numéro généré dans la transaction |
| `POST` | `/api/v1/exams/dossiers/{id}/transmit` | Marquer `Transmis` — refusé (**422**) si le dossier est `Incomplet` |
| `PUT` | `/api/v1/exams/dossiers/{id}/result` | Saisir résultat/mention/moyenne à la délibération |
| `GET` | `/api/v1/exams/statistics` | Taux de réussite par série/classe, filtrable `schoolYearId`, comparaison interannuelle |
| `GET` | `/api/v1/exams/export/ministerial` | Export Excel/CSV conforme IEF/IA, filtre `examSessionId` obligatoire |
| `GET` | `/api/v1/exams/dossiers/{id}/candidate-form/pdf` | Fiche de candidature individuelle (PDF) |
| `POST` | `/api/v1/exams/dossiers/candidate-forms/pdf` | Impression par lot (filtre `examSessionId` ou `classroomId`) |
| `GET` | `/api/v1/exams/dossiers/{id}/convocation/pdf` | Carte de convocation individuelle (PDF) |
| `POST` | `/api/v1/exams/sessions/{id}/dispatch-convocations` | Envoi en lot des convocations par SMS/WhatsApp |

**Règles :**
- **Le numéro de table n'est jamais écrit par `POST /exams/dossiers`.** Il est posé uniquement par `POST /exams/dossiers/{id}/assign-center`, dans la même transaction — exactement le traitement du matricule à l'inscription (règle #3).
- **Un dossier `Incomplet` ne peut ni être transmis, ni entrer dans un lot d'impression ou un export ministériel.** `POST .../transmit` renvoie **409** (conflit avec l'état actuel du dossier, pas une erreur de saisie) si le dossier n'est pas `Complet` — voir le détail sur `GET /exams/dossiers/audit`.
- **`classroomId` est figé à l'ouverture du dossier** : `PUT /exams/dossiers/{id}` ne permet jamais de le modifier — un transfert de classe se gère ailleurs (module Élèves) sans toucher un dossier déjà ouvert.
- **Le dispatch de convocations réutilise le canal SMS/WhatsApp existant** (Volume 1 §13) : aucune nouvelle table de notification, aucun nouveau provider. Refusé (**409**) tant que centre et numéro de table ne sont pas attribués sur un dossier du lot.
- Lecture périmée d'un dossier ou d'une session → **409** (règle #5), jamais un écrasement silencieux.
- Toute génération d'export ministériel, impression par lot ou dispatch de convocations est journalisée à l'audit (Module H du backlog).

---

## 23. API Intégration étatique (SIMEN / Planète / STATEDUC)

Base : `/api/v1/state-integration`. Spécification fonctionnelle : Volume 1 §23. Tickets : Module M.

**Matrice de droits.** `Directeur` sur tout le module. `Secretariat` en plus sur la saisie d'IEN et les
certificats de mutation — c'est lui qui ressaisit les listes d'IEN reçues de l'IEF et qui délivre les
pièces au guichet. Le **rapport STATEDUC et l'export Planète restent au seul Directeur** : ce sont des
déclarations engageant l'établissement devant le ministère, et l'export sort l'état civil de *tous* les
élèves en un fichier.

| Méthode | Route | Rôles | Effet |
|---|---|---|---|
| `GET` | `/planete/export?schoolYearId=&format=&classroomId=` | Directeur | Fichier `.csv`/`.json` — matrice Planète |
| `GET` | `/simen/status` | Directeur | État du relais API (aucune donnée d'école) |
| `GET` | `/stateduc?schoolYearId=&observationDate=` | Directeur | Rapport agrégé (JSON) |
| `GET` | `/stateduc/pdf?schoolYearId=&observationDate=` | Directeur | Formulaire officiel A4 paysage |
| `GET` | `/stateduc/excel?schoolYearId=&observationDate=` | Directeur | Classeur `.xlsx` |
| `PUT` | `/students/{studentId}/ien` | Directeur, Secrétariat | Enregistre l'IEN officiel, ou génère un provisoire |
| `POST` | `/students/{studentId}/mutation-certificate` | Directeur, Secrétariat | Délivre le certificat, renvoie le PDF |
| `GET` | `/students/{studentId}/skills-booklet?schoolYearId=` | Directeur, Secrétariat | Livret de compétences PDF (APC) |
| `GET` | `/certificates?studentId=&page=&pageSize=` | Directeur, Secrétariat | Registre des certificats de mutation délivrés |
| `POST` | `/certificates/{id}/revoke` | Directeur, Secrétariat | Révoque un certificat (motif obligatoire) |
| `GET` | `/certificates/verify/{token}` | **anonyme** | Vérification publique d'un certificat (scan du QR) |

### 23.1 Points d'attention du contrat

**`GET /planete/export` renvoie un FICHIER, y compris au format `json`.** C'est un livrable destiné à
être déposé ou envoyé, pas une réponse d'API à consommer : un navigateur qui l'afficherait à l'écran
obligerait l'utilisateur à faire un copier-coller pour le récupérer. `Content-Disposition: attachment`
dans les deux formats.

**`409 Conflict` si le code établissement national n'est pas renseigné.** L'export refuse plutôt que de
produire un fichier au code vide, que le ministère rejetterait silencieusement plusieurs jours plus
tard. Le message nomme l'écran où corriger (*Paramètres → Établissement*).

**`PUT /students/{id}/ien` — deux modes dans une seule route.** Corps `{"ienNumber": "..."}` = saisie
d'un numéro officiel. Corps `{"ienNumber": null}` ou champ absent = **demande de génération
provisoire**. Une chaîne **vide** est refusée en `422` : « champ laissé vide » ne doit jamais déclencher
une génération que l'utilisateur n'a pas demandée. La réponse porte `isProvisional` — l'écran doit
l'afficher immédiatement, sans relire la fiche.

`409` si l'IEN soumis est déjà porté par un autre élève de l'école (le message nomme lequel), ou si un
provisoire tenterait d'écraser un IEN officiel déjà enregistré. `422` si la forme est invalide.

> **Le contrôle de forme n'est pas une vérification d'authenticité.** Aucun appel au SIMEN n'est
> possible à ce jour ; le message d'erreur de l'API le dit explicitement pour que l'interface ne
> laisse pas croire à une validation auprès du ministère.

**`POST .../mutation-certificate` est un POST bien qu'il renvoie un PDF** : il écrit (numéro officiel
séquentiel, ligne en base, code de vérification), et deux appels produisent deux certificats distincts.
Le ranger en `GET` laisserait croire qu'on peut le rejouer sans conséquence. Deux en-têtes de réponse
accompagnent le corps binaire :

- `X-Certificate-Number` — le numéro délivré ;
- `X-Financially-Clear` — `true`/`false`, pour que l'écran alerte l'agent. **Un `false` ne bloque
  jamais la délivrance** (Volume 1 §23.5).

**`POST .../certificates/{id}/revoke` — la révocation est le SEUL moyen de corriger une pièce
délivrée.** Un certificat n'est jamais modifié : deux versions du même numéro se contrediraient, la
version papier faisant foi contre l'école. Motif obligatoire (`422` sinon). Une seconde révocation du
même certificat renvoie `409`. La ligne reste en base — aucune suppression physique (règle #6). Effet
immédiat sur `/certificates/verify/{token}`, qui répond alors `status: revoked`.

**`GET .../certificates/verify/{token}` ne renvoie AUCUNE donnée de l'élève** — ni nom, ni date de
naissance, ni IEN, ni classe. Seulement `status` (`valid` / `revoked` / `unknown`), le numéro, la
date, l'établissement émetteur : de quoi rapprocher le papier présenté. Le `token` est le code de
vérification (32 hex d'aléa) porté par le QR ; il n'est **jamais** exposé par `/certificates` (le
registre), pour qu'une fuite de la liste ne permette pas de forger des liens de vérification.

**Aucune route de ce module ne marque un lot « Transmis ».** Cet état ne pourra venir que d'un webhook
signé HMAC du SIMEN, vérifié avant toute écriture — par symétrie avec les paiements d'abonnement
(règle #11). Tant que le relais n'existe pas, `GET /simen/status` renvoie `isConfigured: false` et
l'interface n'affiche pas l'action de transmission.

**Audit (JGK-H01).** `GET /planete/export`, `GET /stateduc*`, `PUT .../ien` et
`POST .../mutation-certificate` sont tous journalisés. L'export et le rapport sont des *lectures*, mais
des lectures massives de données personnelles : savoir qui a extrait le fichier, et quand, est le seul
recours en cas de fuite.

---

## 24. API Cahier de texte / Journal de classe

Base : `/api/v1/class-journal`. Spécification fonctionnelle : `docs/BACKLOG_TICKETS.md` (Module P).
Ticket : JGK-P04. Dépend de JGK-C02 (Classes), JGK-C03 (Matières) et du module Emploi du temps
existant (`ScheduleSlot`).

**Matrice de droits.** Écriture (création) réservée à l'**Enseignant**, et seulement pour son propre
créneau planifié — vérifié contre `TeacherAssignment` (année active) **et** `ScheduleSlot` (jour de
la semaine). Correction (modification/suppression) ouverte au niveau du rôle à Enseignant, Directeur
et Secrétariat, mais bornée dans le Handler à « l'auteur dans les 15 jours suivant la séance, ou un
rôle élevé au-delà » (`403` sinon). **Lecture ouverte sans restriction de périmètre** à Directeur,
Secrétariat, Surveillant et Enseignant — un journal de classe est un document pédagogique partagé,
pas un carnet privé par enseignant. Aucun rôle Parent/Élève (portails hors périmètre V1, AGENTS.md).

| Méthode | Route | Rôles | Effet |
|---|---|---|---|
| `GET` | `/?page=&pageSize=&classroomId=&subjectId=&periodStart=&periodEnd=` | Directeur, Secrétariat, Surveillant, Enseignant | Liste paginée, filtrable |
| `POST` | `/` | Enseignant | Journalise une séance (409/422 selon la garde ci-dessous) |
| `PUT` | `/{id}` | Enseignant, Directeur, Secrétariat | Corrige sujet/contenu/devoirs (403 hors délai/propriété) |
| `DELETE` | `/{id}?rowVersion=` | Enseignant, Directeur, Secrétariat | Archive (soft delete), même garde que `PUT` |

### 24.1 Points d'attention du contrat

**`POST /` refuse en `409` (code `SCHEDULE_SLOT_NOT_PLANNED`) sur DEUX conditions distinctes, jamais
confondues.** `ScheduleSlot` est un créneau HEBDOMADAIRE récurrent (jour de la semaine + horaire) —
il ne porte aucune `SchoolYearId`. Un contrôle isolé matcherait donc aussi un créneau d'une année
scolaire révolue, encore présent en base. La garde vérifie donc successivement : (1) une
`TeacherAssignment` existe pour cet enseignant, cette classe, cette matière, sur l'année ACTIVE ; (2)
un `ScheduleSlot` existe pour ce même triplet, au jour de la semaine de `sessionDate`. L'absence de
l'une ou l'autre renvoie `409`, jamais `403` — c'est l'état de l'affectation/du planning qui bloque,
pas le rôle (qui est déjà correct à ce stade).

**`sessionDate` dans le futur est refusée en `422`.** On journalise ce qui a été fait, jamais un
programme prévisionnel — même esprit que `SubmitAttendanceSheetCommand` pour l'appel.

**`PUT /{id}` ne porte ni `classroomId`, ni `subjectId`, ni `sessionDate`.** Ce sont les coordonnées
de la séance journalisée, pas son compte-rendu : les changer reviendrait à créer une autre entrée. Une
erreur sur ces champs se corrige en supprimant l'entrée puis en en créant une nouvelle.

**La règle des 15 jours est symétrique entre `PUT` et `DELETE`.** Librement modifiable/archivable par
son auteur pendant 15 jours après `sessionDate` ; passé ce délai, `403` pour l'auteur — seuls
Directeur et Secrétariat peuvent encore agir, sans limite de délai. Le message distingue explicitement
« délai dépassé » de « ce n'est pas votre entrée » (règle #10, action corrective nommée).

**`GET /` renvoie `canEdit` par ligne, calculé côté serveur.** Confort d'affichage — l'écran l'utilise
pour masquer Modifier/Supprimer plutôt que de recalculer la règle des 15 jours côté client (qui n'a ni
l'heure serveur ni, pour l'Enseignant, un moyen fiable de connaître sa propre fiche). La vraie garde
reste `PUT`/`DELETE`, qui refusent indépendamment.

**Audit (JGK-H01).** `PUT /{id}` est journalisé, qu'il vienne de l'auteur dans le délai ou d'un rôle
élevé après — même esprit que la correction de notes (`UpdateGradeCommand`) et que la règle #4
Finance : une correction reste toujours tracée, jamais un écrasement silencieux de l'historique
pédagogique.

## 25. API Rapports institutionnels (Évolution N°7)

Contrôleur `InstitutionalController`, module Pédagogie requis. Lecture : Directeur, Secrétariat.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/institutional/ief-report?schoolYearId=&ageReferenceDate=` | Rapport de rentrée IEF (JSON) : classes × âges × sexe, statuts, hors norme, redoublement par niveau, corps professoral. `ageReferenceDate` facultatif (défaut 31 décembre de l'année de rentrée) |
| `GET` | `/api/v1/institutional/ief-report/pdf` · `/excel` | Même agrégat, en PDF (A4 paysage, `inline`) ou `.xlsx` |
| `GET` | `/api/v1/institutional/age-norms` | Tranche d'âge de chaque niveau : modèle national et réglage de l'école |
| `PUT` | `/api/v1/institutional/age-norms/{gradeLevel}` | `{ minAge, maxAge }` — règle un niveau (Directeur seul) ; `422` niveau inconnu ou bornes incohérentes |
| `DELETE` | `/api/v1/institutional/age-norms/{gradeLevel}` | « Revenir au modèle » : archive le réglage (Directeur seul) |
| `GET` | `/api/v1/institutional/age-check?classroomId=&birthDate=` | Contrôle d'âge à l'inscription : niveau, âge au 31/12, tranche, statut (`Early`/`Normal`/`Late`/`Unknown`), message — un avertissement, jamais un refus |

`POST /api/v1/enrollments` accepte en plus `isTransferredIn` et `previousSchoolName` (≤ 150).

## 26. API Programmes (Évolution N°7)

Contrôleur `SyllabusController`, module Pédagogie requis.

| Méthode | Route | Rôles | Description |
|---|---|---|---|
| `GET` | `/api/v1/syllabus/units?subjectId=&gradeLevel=` · `?subjectId=&classroomId=` | Directeur, Secrétariat, Enseignant, Surveillant | Programme d'une matière pour un niveau (ou pour le niveau d'une classe) : `{ gradeLevel, hasTemplate, units[] }`, chapitres dans l'ordre avec `rowVersion` |
| `POST` | `/api/v1/syllabus/units` | Directeur | `{ subjectId, gradeLevel, section?, titles[] }` — ajoute à la suite ; un intitulé existant est ignoré. `{ added }` |
| `POST` | `/api/v1/syllabus/import-template` | Directeur | `{ subjectId, gradeLevel }` — importe la trame nationale ; `422` s'il n'en existe pas. `{ added }` |
| `PUT` | `/api/v1/syllabus/units/{id}` | Directeur | `{ title, section?, order, plannedHours?, rowVersion }` — `409` si périmé |
| `DELETE` | `/api/v1/syllabus/units/{id}?rowVersion=` | Directeur | Retire un chapitre (suppression logique) — `409` si périmé |
| `GET` | `/api/v1/syllabus/coverage` | Directeur, Secrétariat | Avancement de l'année active : `rows[]` (classe × matière : enseignants, `coveredUnits`/`totalUnits`, `percent`, dernière séance), `bySubject[]`, `byTeacher[]` |

`POST /api/v1/class-journal` et `PUT /api/v1/class-journal/{id}` acceptent `syllabusUnitIds[]` (chapitres traités ;
`422` si l'un n'appartient pas au programme de la matière pour le niveau de la classe ; absent à la correction :
inchangés). `GET /api/v1/class-journal` renvoie `syllabusUnitIds` pour chaque séance.

## 27. API Volumes horaires et conformité des emplois du temps (Évolution N°7)

Contrôleur `HourVolumesController`, module Pédagogie requis. Lecture : Directeur, Secrétariat.

| Méthode | Route | Description |
|---|---|---|
| `GET` | `/api/v1/hour-volumes/norms?gradeLevel=&series=` | Volumes d'un niveau (et d'une série) : par matière, `templateHours` (grille), `gradeHours` (réglage du niveau), `schoolHours` (réglage de cette série), `effectiveHours`, `overrideId`/`rowVersion` ; lignes de grille sans matière dans l'établissement (`subjectId` null) ; `totalHours`. `422` niveau ou série inconnus |
| `PUT` | `/api/v1/hour-volumes/norms` | `{ gradeLevel, series?, subjectId, weeklyHours, rowVersion? }` — Directeur seul. Au quart d'heure, 0 à 40 h ; série réservée au lycée. `rowVersion` obligatoire pour modifier un réglage existant (`409` si périmé) |
| `DELETE` | `/api/v1/hour-volumes/norms/{id}?rowVersion=` | « Revenir à la référence » (suppression logique) — Directeur seul, `409` si périmé |
| `GET` | `/api/v1/hour-volumes/compliance?classroomId=` | Conformité d'une classe (ou de toutes) : `classes[]` (`gradeLevel`, `series`, `totals`, `subjects[]` avec `plannedHours`, `normHours`, `difference`, `status` = `Compliant`/`Under`/`Over`/`NoReference`, `conflictCount`) et `conflicts[]` (`kind` = `Teacher`/`Room`/`Classroom`, `dayOfWeek`, `resource`, `first`, `second`). `404` classe inconnue |

---

**Fin du Volume 4.**
