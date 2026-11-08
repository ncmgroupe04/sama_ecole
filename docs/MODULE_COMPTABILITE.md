# Module Comptabilité / Finance de Sama Ecole — analyse complète et guide de reproduction

> **Dépôt analysé :** `ncmgroupe04/sama_ecole` (ASP.NET Core 9, EF Core, PostgreSQL, MediatR, FluentValidation, QuestPDF, ClosedXML, Alpine.js + Tailwind).
> **Objectif de ce document :** vous permettre de réimplémenter, dans un autre projet, l'intégralité du module « comptabilité » tel qu'il existe ici — règles métier, modèle de données, code, API, écrans, documents imprimés, sécurité, tests.
> **Organisation en 3 fichiers (le volume de code ne tient pas raisonnablement dans un seul) :**
>
> | Fichier | Contenu |
> |---|---|
> | **`docs/MODULE_COMPTABILITE.md`** (ce fichier) | Analyse, règles, modèle de données, flux, API, sécurité, guide de reproduction **+ le code source intégral du back-end** (Domain, Persistence, Application, Web/contrôleurs/autorisations, Infrastructure) repris **ligne pour ligne** |
> | `docs/MODULE_COMPTABILITE_ANNEXE_A_FRONT_PDF_MIGRATIONS.md` | Code intégral des écrans Razor, du JavaScript (Alpine.js), des documents PDF (QuestPDF), des exports Excel (ClosedXML) et des migrations SQL/RLS |
> | `docs/MODULE_COMPTABILITE_ANNEXE_B_TESTS.md` | Code intégral des tests (unitaires, intégration, fonctionnels, JS) |
>
> Les blocs de code des annexes et de la partie « Code source » sont générés automatiquement depuis les fichiers du dépôt : ils sont **identiques au dépôt à la date de l'analyse** (aucune ligne n'a été omise ni réécrite). Les commentaires XML d'origine, en français, font partie de la documentation métier : ils sont conservés.

---

## Table des matières

1. [Ce qu'est (et n'est pas) ce « module de comptabilité »](#1-ce-quest-et-nest-pas-ce-module-de-comptabilité)
2. [Cartographie fonctionnelle](#2-cartographie-fonctionnelle)
3. [Architecture et conventions transverses](#3-architecture-et-conventions-transverses)
4. [Règles métier non négociables](#4-règles-métier-non-négociables)
5. [Modèle de données](#5-modèle-de-données)
6. [Flux fonctionnels détaillés](#6-flux-fonctionnels-détaillés)
7. [API REST (toutes les routes)](#7-api-rest-toutes-les-routes)
8. [Autorisations, délégations, formules d'abonnement](#8-autorisations-délégations-formules-dabonnement)
9. [Interface utilisateur](#9-interface-utilisateur)
10. [Documents imprimés et exports](#10-documents-imprimés-et-exports)
11. [Stratégie de tests](#11-stratégie-de-tests)
12. [Guide de reproduction pas à pas dans un autre projet](#12-guide-de-reproduction-pas-à-pas-dans-un-autre-projet)
13. [Limites connues, dette et améliorations recommandées](#13-limites-connues-dette-et-améliorations-recommandées)
14. [Code source intégral du back-end](#14-code-source-intégral-du-back-end)

---

## 1. Ce qu'est (et n'est pas) ce « module de comptabilité »

Dans le dépôt, il n'existe **pas** de projet ou de dossier nommé « Accounting ». La comptabilité est le **module `Finance`** (`SchoolModule.Finance` : « Caisse, Frais scolaires, Paie, Fiscalité, Trésorerie, Rapports financiers »), activable/désactivable par école (`SchoolSettings.IsFinanceEnabled`, activé par défaut). Il couvre **neuf sous-domaines livrés** :

| # | Sous-domaine | Écran | Spécification (Volume 1) |
|---|---|---|---|
| 1 | **Frais scolaires / barème** (catégories, montant par classe, montant standard, historique) | `/frais` | §7.1, §7.4 |
| 2 | **Montant dû d'une inscription** (lignes de frais figées, mensualités, frais facultatifs, pension) | `/inscriptions` | §7.2 |
| 3 | **Encaissements** (versements, ventilation par catégorie, reçu numéroté, TVA collectée, idempotence) | `/caisse` | §7.3, §7.3bis |
| 4 | **Sessions de caisse** (ouverture, fonds, clôture, comptage physique, écart, rapport de clôture PDF, journal du jour PDF) | `/caisse` | §15 |
| 5 | **Annulation / contre-passation d'un versement** (48 h, motif, destination de l'argent, registre daté) | `/caisse` | spec 2026-10-17 §2.4 |
| 6 | **Recouvrement** (échéanciers personnalisés, balance âgée, lots de relance SMS, sommation, engagement financier) | `/caisse`, `/rapports/financiers` | §7.5 |
| 7 | **Décaissements / dépenses** (catégories, TVA déductible, contre-écriture) et **Trésorerie** | `/frais` (onglet dépenses), `/tresorerie` | §7.7, §16 |
| 8 | **Paie** (contrats, heures de vacataires, fiche de paie, annulation/régénération, règlement, attestation de travail, paie sans retenues) | `/paie` | §14 |
| 9 | **Fiscalité** (déclaration mensuelle : TVA collectée/déductible, IPRES, CSS, VRS/CFCE, BRS) et **Rapports financiers** (consolidation des revenus, export Excel) | `/fiscalite`, `/rapports/financiers` | §16.3, §17 |

### 1.1 Ce que ce module **n'est pas** (point essentiel avant de le reproduire)

* **Ce n'est pas une comptabilité générale en partie double.** Il n'y a ni plan comptable (SYSCOHADA ou autre), ni journal d'écritures débit/crédit, ni grand livre, ni balance, ni bilan/compte de résultat, ni clôture d'exercice, ni rapprochement bancaire. Aucune table `accounts`, `journal_entries` ou `ledger_lines` n'existe.
* C'est une **comptabilité de gestion de trésorerie (encaissements / décaissements)** adossée à la facturation scolaire :
  * les **« écritures »** sont des lignes de registres métier immuables : `payments` (encaissements), `disbursements` (décaissements), `fiche_paies` (paie), `taxe_declarations` (déclarations) ;
  * la **règle d'or comptable** est respectée à la manière d'un registre légal : *on ne supprime jamais, on ne modifie jamais un montant déjà passé* — on **annule** (statut `Cancelled` + motif + auteur + date) ou on **contre-passe** (écriture de montant opposé, datée du jour) ;
  * le terme « registre » (`PaymentLedger`) désigne un calcul de lecture qui rend un état **daté et reproductible** (une période close ne bouge jamais, une annulation tardive est une écriture négative de la période où elle est passée).
* Le « solde » d'un élève n'est pas un compte client tenu en écritures : c'est `Enrollment.TotalDue − Enrollment.AmountPaid`, avec `AmountPaid` un **compteur** incrémenté dans la même transaction que chaque `Payment`.

> **Conséquence pour votre autre projet :** si vous avez besoin d'une vraie comptabilité en partie double (plan comptable, journaux, balance), **ce module ne la fournit pas** : il faudra ajouter une couche d'écritures générées à partir des événements décrits ici (encaissement, décaissement, paie, TVA). La section 13 propose un schéma de passage.

### 1.2 Périmètre explicitement exclu dans le dépôt

* Aucun mode hors ligne / local / LAN (100 % en ligne, PostgreSQL uniquement).
* Pas d'intégration directe aux opérateurs Mobile Money pour les **frais scolaires** : Wave / Orange Money sont saisis **manuellement** (`PaymentMethod.MobileMoney`). Les agrégateurs (PayDunya/CinetPay) ne servent qu'aux **abonnements** de la plateforme (`SubscriptionPayment`, hors périmètre de ce document).
* Pas d'export global « Exporter mes données » ; seuls des exports par domaine existent (rapport financier `.xlsx`, balance âgée `.xlsx`, PDF officiels).
* Pas de portail parents (les parents reçoivent des SMS sortants et des documents remis en main propre).

---

## 2. Cartographie fonctionnelle

```
                           ┌────────────────────────────┐
  Barème (FeeCategory +    │ Inscription (secrétariat)  │  fige TotalDue + EnrollmentFeeLine (instantané)
  ClassFee, historisé)  ──▶│ CreateEnrollmentCommand    │  + n° de reçu d'inscription (REC-AAAA-NNNN)
                           └──────────────┬─────────────┘
                                          │ Enrollment.TotalDue (jamais modifié par la Finance)
                                          ▼
 Session de caisse ──▶  RecordPaymentCommand ──▶ Payment (+ PaymentBreakdown, VatAmount, BalanceAfter,
 (OpenCashierSession)    (idempotent, xmin)        ReceiptNumber) ; Enrollment.AmountPaid += montant
        │                                                │
        │                                                ├─▶ SMS de confirmation au tuteur (hors transaction)
        │                                                ├─▶ invalidation du cache KPI
        ▼                                                ▼
 CloseCashierSession ◀── PaymentLedger (registre daté) ──▶ Trésorerie / Dashboard / Rapports / TVA collectée
 (comptage espèces,                ▲
  écart, motif)                    │ annulations (48 h) / contre-passations (Directeur)
                                   │
                         CancelPaymentCommand

 Décaissement (Disbursement, TVA déductible) ◀── PayFichePaie (catégorie « Salaires »)
        │                                          ▲
        │ contre-écriture (montant < 0)            │ GenerateFichePaie / Regenerate / Cancel
        ▼                                          │ (EmployeeContract + TeacherHourRecord)
 Trésorerie (GetTreasuryDashboard)            PayrollCalculator
        │
        ▼
 TaxDeclaration (TVA collectée − déductible + IPRES + CSS + VRS + BRS) ──▶ PDF officiel

 Recouvrement : InstallmentScheduleCalculator ─▶ GetStudentBalance / DuesNotice (sommation PDF)
                DebtorAgingHostedService (nuit) ─▶ DebtorReminderBatch (brouillon) ─▶ envoi SMS manuel
                FinancialCommitment (reconnaissance de dette PDF) ; FeeInstallmentPlan (échéancier négocié)
```

---

## 3. Architecture et conventions transverses

Le module respecte l'architecture du dépôt (règles `AGENTS.md`). Ce sont ces conventions — plus que les entités — qu'il faut reproduire pour obtenir le même niveau de sûreté.

### 3.1 Couches et projets

| Projet | Rôle pour le module |
|---|---|
| `SamaEcole.Domain` | Entités (`Payment`, `Disbursement`, `FichePaie`, …), énumérations (`PaymentMethod`, `PaymentCategory`, `DisbursementCategory`, …). Aucune logique métier dans les entités (règle #8). |
| `SamaEcole.Application` | **Toute la logique** : commandes/requêtes MediatR (CQRS), validateurs FluentValidation, services purs (`PayrollCalculator`, `TaxCalculator`, `VatCalculator`, `InstallmentScheduleCalculator`), registre `PaymentLedger`. Dossiers `Finance/` et `Features/Disbursements/`. |
| `SamaEcole.Persistence` | `ApplicationDbContext`, configurations Fluent API, migrations (RLS + GRANT), `MatriculeGenerator` (n° de reçu). |
| `SamaEcole.Infrastructure` | Documents PDF (QuestPDF), exports Excel (ClosedXML), `DebtorAgingHostedService`, SMS. |
| `SamaEcole.Web` | Contrôleurs minces, politiques d'autorisation, vues Razor, JS Alpine.js, middleware d'erreurs. |

### 3.2 CQRS avec MediatR
Toute écriture est une `Command` (`IRequest<T>`), toute lecture une `Query`. Un handler ne mélange jamais les deux. Les commandes sensibles implémentent le marqueur **`IAuditableRequest`** (journal d'audit centralisé, ticket JGK-H01) : encaissement, annulation, clôture de caisse, paie, annulation de fiche, etc.

### 3.3 Multi-tenant : `SchoolId` + Row-Level Security + Global Query Filter (les deux, toujours)
* Toute table métier porte `SchoolId` (interface `ITenantEntity`).
* **Global Query Filter EF Core** appliqué automatiquement à toute entité `ITenantEntity` : `e.SchoolId == tenantProvider.CurrentSchoolId && !e.IsDeleted` (voir `ApplicationDbContext.SetTenantFilter`, code en §14.1).
* **RLS PostgreSQL** : chaque migration qui crée une table tenant exécute `ENABLE ROW LEVEL SECURITY` + `CREATE POLICY <table>_tenant_isolation ... USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid) WITH CHECK (...)`.
* **Deux rôles PostgreSQL** : `sama_ecole_app` (utilisé par l'application, `NOSUPERUSER`, `NOBYPASSRLS`, non propriétaire des tables) et `sama_ecole` (propriétaire, réservé aux migrations). Brancher l'application sur le propriétaire désactiverait la RLS **sans erreur** ; un `RlsGuard` refuse le démarrage dans ce cas.
* **GRANT à la maille de la table** : `SELECT, INSERT, UPDATE` (jamais `DELETE`) sur toutes les tables métier ; **`SELECT, INSERT` seulement** sur les tables **append-only** (`fee_change_history`, `employee_contract_histories`, …). Une migration finale (`RevokeDeleteOnBusinessTables`) retire `DELETE` à toutes les tables métier et aux privilèges par défaut.
* Le `schoolId` vient **toujours du JWT** (`ITenantProvider.CurrentSchoolId`), jamais d'un paramètre de requête.
* Les tâches de fond (relances nocturnes) tournent école par école via `TenantProvider.RunAsSchoolAsync`, avec **scope DI et connexion neufs à chaque école** (sinon `app.current_school_id` ne serait pas repositionné).
* **FK composites `(SchoolId, X)`** : une ligne de barème ne peut pas pointer la classe/catégorie d'une autre école ; PostgreSQL rend le croisement de tenants structurellement impossible (`HasAlternateKey(c => new { c.SchoolId, c.Id })` + `HasForeignKey(f => new { f.SchoolId, f.X })`).

### 3.4 Concurrence optimiste via `xmin`
Colonne système PostgreSQL mappée par `builder.Property<uint>("xmin").IsRowVersion()`. Tables concernées : `class_fees`, `enrollments`, `employee_contracts`, `fiche_paies`, `taxe_declarations`.
* **Encaissement et annulation** : le compteur `Enrollment.AmountPaid` est écrit sous le `xmin` de l'inscription → deux encaissements simultanés sur le même solde ne peuvent pas passer ; le second reçoit `409`.
* **Modifications côté client** (barème, fiche de paie) : le client renvoie la `rowVersion` qu'il a lue ; le handler appelle `dbContext.SetOriginalConcurrencyToken(entity, request.RowVersion)` → `UPDATE … WHERE xmin = <version client>`.
* `ApplicationDbContext.SaveChangesAsync` traduit `DbUpdateConcurrencyException` en `ConcurrencyConflictException` (→ HTTP 409) et les violations d'unicité PostgreSQL (`23505`) en `DuplicateRecordException` (→ 409).

### 3.5 Suppression logique et immuabilité
Aucune suppression physique de donnée métier : `IsDeleted/DeletedAt/DeletedBy` (`AuditableEntity.SoftDelete`). Mais pour la **comptabilité** la règle est plus stricte : un paiement, un décaissement, une fiche, une déclaration **ne se « soft-delete » même pas** — ils **s'annulent** (statut, motif, auteur, date) ou se **contre-passent**.

### 3.6 Gestion transactionnelle
`IApplicationDbContext.ExecuteInTransactionAsync` : utilise l'`ExecutionStrategy` EF (rejoue toute la transaction en cas d'erreur transitoire) ; se greffe sur une transaction déjà ouverte (tests d'intégration). Les effets de bord **externes** (SMS, invalidation de cache) sont exécutés **après commit**.

### 3.7 Gestion d'erreurs
`ExceptionHandlingMiddleware` traduit : `ValidationException` → 422 ; `NotFoundException`/`KeyNotFoundException` → 404 ; `BusinessRuleException(message, code?)` → 409 avec code stable (`PAYMENT_ALREADY_CANCELLED`, `PAYMENT_CANCELLATION_WINDOW_EXPIRED`, `DISBURSEMENT_ALREADY_REVERSED`, `DISBURSEMENT_IS_REVERSAL`, `PAYSLIP_PERIOD_DECLARED`) ; `ConcurrencyConflictException` / `DuplicateRecordException` → 409 ; `UnauthorizedAccessException` → 403. Format normalisé (Volume 4 §0.4).

### 3.8 Temps, argent, cache
* Toujours `TimeProvider` (jamais `DateTime.UtcNow` en dur dans les handlers récents) → tests déterministes.
* Montants en **FCFA**, `decimal`, PostgreSQL `numeric(12,2)` (paiements, barème, caisse) ou `decimal(18,2)` (décaissements). Taux de TVA = **fraction** (0.18) en `numeric(5,4)`.
* Format monétaire affiché : entiers, séparateur de milliers par espace, sans décimale.
* **Cache KPI** (`IKpiCacheService`, clés `finance-dashboard`, `director-dashboard`), scopé tenant via `ITenantCacheKeyFactory.BuildKey` (un cache n'est pas couvert par la RLS). Invalidé **après commit** à chaque encaissement, annulation, décaissement, paiement de fiche.
* Numéro de reçu : `MatriculeGenerator.GenerateNextReceiptNumberAsync` → `REC-{YEAR}-{SEQ:4}`, séquence **gapless par école et par année scolaire**, via `INSERT … ON CONFLICT (SchoolId, Kind, Year) DO UPDATE SET LastValue = LastValue + 1 RETURNING` **dans la transaction de l'encaissement** : si la transaction échoue, le numéro est rembobiné.

---

## 4. Règles métier non négociables

Ces règles sont issues de `AGENTS.md` (règles #1–#12) et du Cahier des charges (Volume 1 §7, §14–§17). Chacune est **testée** (§11).

| # | Règle | Où elle est tenue |
|---|---|---|
| R1 | **La Finance ne fixe ni ne modifie jamais `Enrollment.TotalDue`.** Elle n'impute que des versements (`AmountPaid`). Toute correction passe par le secrétariat/Directeur et est historisée. | `RecordPaymentCommandHandler` n'écrit que `AmountPaid` ; aucun endpoint finance n'écrit `TotalDue` |
| R2 | Un versement est **strictement positif** et **≤ solde restant**. Inscription annulée ou soldée → refus. | Validator + handler + `CHECK (Amount > 0)` en base |
| R3 | **Numéro de reçu officiel gapless**, attribué **dans la transaction** d'encaissement, unique par école (`UX_payments_receipt_number`), jamais réémis (même annulé). | `MatriculeGenerator` |
| R4 | **Idempotence** : une `IdempotencyKey` (GUID client) rejoue le résultat du premier essai au lieu de créer un doublon (index unique partiel). | `RecordPaymentCommandHandler` + `UX_payments_idempotency_key` |
| R5 | **Concurrence** : verrou `xmin` sur l'inscription pour les encaissements/annulations ; conflit → 409, jamais un sur-crédit silencieux. | `Enrollment` xmin |
| R6 | **Aucun versement sans session de caisse ouverte** pour l'utilisateur. **Une seule session ouverte** par caissier et par école (index unique partiel sur `Status='Open'`). | handlers + `UX_cashier_sessions_SchoolId_CashierId_Open` |
| R7 | **Clôture de caisse = comptage physique obligatoire** (`ActualCashAmount`). Écart calculé sur les **espèces seules** (`ExpectedCashAmount = fonds + encaissements espèces`) ; si écart ≠ 0, **motif obligatoire**. Une session close ne se rouvre ni ne se re-clôture. | `CloseCashierSessionCommandHandler` |
| R8 | **Annulation d'un versement** : jamais de suppression ; statut `Cancelled` + motif + date + auteur + destination de l'argent (`KeptBySchool` / `ReusedForEnrollment` / `RefundedToParent`) + session d'annulation. **Délai de grâce 48 h** ; au-delà : **contre-passation** explicite, **réservée au Directeur**. | `CancelPaymentCommandHandler`, `PaymentCancellationPolicy` |
| R9 | **Registre daté** : un versement compte dans la période de son encaissement même s'il est annulé *après* ; l'annulation d'un versement d'une période antérieure est une écriture **négative** de la période où elle est passée. Règle unique pour caisse, trésorerie, dashboard, TVA, rapports. | `PaymentLedger` |
| R10 | **Effet espèces** : l'annulation ne retire des espèces du tiroir que si l'argent est **rendu au parent** (`RefundedToParent`). Le chiffre d'affaires, lui, est net de toutes les annulations. | `PaymentLedgerEntry.CashEffect` |
| R11 | **TVA déclarée transaction par transaction** (jamais déduite d'une catégorie). TVA « en dedans » : `TVA = round(TTC × t / (1 + t), 2, AwayFromZero)`, **figée** à la saisie (`VatRate`, `VatAmount`). | `VatCalculator` |
| R12 | **Décaissement = écriture immuable** : pas de suppression, pas de modification. Correction = **contre-écriture** (montant négatif, `ReversalOfId`), au plus **une** par original (index unique), jamais une contre-écriture de contre-écriture, datée du **jour de l'annulation**. | `DeleteDisbursementCommandHandler`, `CK_disbursements_reversal_negative`, `IX…ReversalOfId` unique |
| R13 | **Barème historisé** : tout changement de montant écrit une ligne append-only `fee_change_history` (ancien, nouveau, auteur, date). Montant ≥ 0 (0 autorisé : frais offert / école publique). | `UpdateClassFee`, `ApplyStandardFee` |
| R14 | **Instantané des frais à l'inscription** : `EnrollmentFeeLine.Designation/UnitAmount/Months/LineTotal` sont **copiés** du barème ; un changement ultérieur du barème ne réécrit jamais une inscription. | `CreateEnrollmentCommandHandler` |
| R15 | **Mensualité × nombre de mois** (`TuitionMonthsPerYear`, défaut 9, bornes 1–12) ; un frais ponctuel × 1. Frais facultatifs (`IsOptional`) facturés seulement si cochés ; pension (`IsBoardingFee`) seulement pour Interne/Demi-pensionnaire. | `BuildFeeLinesAsync`, `OptionalFeeSelection`, `BoardingFeeLineBuilder` |
| R16 | **Une catégorie de frais utilisée (facturée ou payée) ne se supprime pas** ; une catégorie archivée libère son nom (index unique partiel `IsDeleted=false`). | `FeeCategoryDeletionPolicy` |
| R17 | **Fiche de paie = instantané** : s'annule (motif ≥ 5 car.), ne se supprime/modifie jamais ; une fiche **non annulée** par (contrat, mois, année) ; **régénération atomique** (annule puis recalcule sur les termes actuels du contrat). | `RegenerateFichePaieCommandHandler`, index unique partiel |
| R18 | **Garde-fou fiscal** : une fiche ne s'annule plus dès qu'une déclaration existe pour sa période (`PAYSLIP_PERIOD_DECLARED`). | `FichePaieCancellation` |
| R19 | **Règlement du salaire** = création d'un décaissement `Salaires` sans TVA (montant = net) ; une fiche ne se paie qu'**une fois**, jamais annulée, net > 0. Annuler une fiche payée **contre-passe** le décaissement (sauf déjà contre-passé à la main). | `PayFichePaieCommandHandler`, `FichePaieCancellation` |
| R20 | **Contrat** jamais supprimé : **clôturé** (`EndDate`) ; plus de fiche possible après clôture ; un seul contrat actif par enseignant / utilisateur / nom (personnel sans compte) ; historique append-only des changements de rémunération. | `employee_contracts`, `employee_contract_histories` |
| R21 | **Paie sans retenues** (structure qui ne cotise pas) : réglage **par contrat** (`ApplyStatutoryDeductions`), **instantané par fiche** (`DeductionsApplied`) ; net = brut + transport. | `PayrollCalculator` |
| R22 | **Déclaration fiscale** : **une seule par (école, mois, année)** ; instantané daté ; TVA collectée issue du **registre** (net des annulations), TVA déductible = somme des `VatAmount` des décaissements du mois (contre-écritures négatives incluses) ; fiches **non annulées** uniquement. | `GenerateTaxDeclarationCommandHandler` |
| R23 | **Reçu** : suit exactement la maquette `docs/design-references/` (A5 paysage), avec la mention fixe *« Il est demandé aux parents de garder minutieusement leur reçu après le paiement. »*, non désactivable. Un reçu réimprimé montre le solde **de ce jour-là** (`BalanceAfter` figé). | `PaymentReceiptDocument` |
| R24 | **Relances** : jamais d'envoi de masse automatique ; la nuit on génère des **brouillons** (`Draft`) par classe ; l'envoi est un **clic humain** ; double interrupteur (SMS activés dans les paramètres + formule Premium). | `DebtorAgingHostedService`, `SendDebtorReminderBatch` |
| R25 | **Exports comptables** (Excel, rapports consolidés) réservés aux formules Standard/Premium (`Feature.AdvancedFinancialReports`) ; en Primaire le bouton est **désactivé et signalé**, jamais masqué. | `[RequireFeature]` + UI |

---

## 5. Modèle de données

Toutes les tables ci-dessous héritent de `AuditableEntity` (Id GUID, `CreatedAt`, `UpdatedAt`, `IsDeleted`, `DeletedAt`, `DeletedBy`) et portent `SchoolId` (sauf mention). RLS `…_tenant_isolation` sur chacune.

### 5.1 Vue d'ensemble des relations

```
fee_categories 1─* class_fees *─1 classrooms
class_fees 1─* fee_change_history                     (append-only)
enrollments 1─* enrollment_fee_lines *─1 fee_categories        (instantané du barème)
enrollments 1─* payments 1─* payment_breakdowns *─1 fee_categories
cashier_sessions 1─* payments (CashierSessionId)       ; payments.CancellationSessionId → cashier_sessions (référence d'audit)
enrollments 1─* fee_installment_plans 1─* fee_installments   (un seul plan Active par inscription)
enrollments 1─* financial_commitments
debtor_reminder_batches 1─* debtor_reminder_batch_items *─1 enrollments
employee_contracts 1─* fiche_paies ; employee_contracts 1─* employee_contract_histories (append-only)
employee_contracts 1─* teacher_hour_records
fiche_paies ─ DisbursementId (sans FK) → disbursements
disbursements.ReversalOfId → disbursements (auto-référence, unique)
taxe_declarations (une par école/mois/année)
matricule_sequences (Kind = Receipt, …) : compteur gapless des numéros de reçu
```

### 5.2 Détail table par table

#### `payments` — encaissements (entité `Payment`)
| Colonne | Type | Contraintes / rôle |
|---|---|---|
| `EnrollmentId` | uuid | FK composite `(SchoolId, EnrollmentId)` → `enrollments` |
| `CashierSessionId` | uuid? | FK composite → `cashier_sessions` |
| `Category` | varchar(30) (enum texte) | `Enrollment, Tuition, Exam, Canteen, Other, Transport, Uniformes, ActivitesPeriscolaires, FournituresVente` |
| `ReferencePeriod` | varchar(20)? | période de référence libre |
| `Amount` | numeric(12,2) | `CHECK (Amount > 0)` |
| `VatRate` | numeric(5,4)? | `CHECK (VatRate IS NULL OR 0 ≤ VatRate ≤ 1)` |
| `VatAmount` | numeric(12,2) | défaut 0, **figé** |
| `Method` | varchar(20) | `Cash, Cheque, Transfer, MobileMoney` |
| `Status` | varchar(20) | `Paid` (solde l'inscription), `Partial` (reste dû), `Cancelled` |
| `BalanceAfter` | numeric(12,2) | solde restant juste après ce versement (reçu fidèle dans le temps) |
| `ReceiptNumber` | varchar(50) | **unique par école** `UX_payments_receipt_number` (non filtré) |
| `IdempotencyKey` | uuid? | **unique partiel** `UX_payments_idempotency_key` (`IS NOT NULL`) |
| `ReceivedByUserId` | uuid | référence d'audit, **sans FK** (évite l'interaction FK × RLS) |
| `PaidAt` | timestamptz | |
| `CancelledAt / CancelledByUserId / CancellationReason(500) / CancellationOutcome(30) / CancellationSessionId` | | renseignés **ensemble** par l'annulation |
| Index | | `(SchoolId, EnrollmentId)`, `(SchoolId, Status, PaidAt)` (dashboard), `(SchoolId, CancellationSessionId)` partiel, `(SchoolId, CancelledAt)` partiel |

#### `payment_breakdowns` — ventilation d'un versement par catégorie
`PaymentId` (FK cascade), `FeeCategoryId` (FK restrict), `AmountAllocated numeric(12,2)`, `Label varchar(60)?` (colonne « Période / Note » du reçu, descriptive, n'entre dans aucun calcul).

#### `fee_categories` — catégories de frais (libres, par école)
`Name varchar(60)` **unique parmi les vivantes** (`IsDeleted=false`), `IsRecurring` (mensualité), `IsBoardingFee` (pension), `IsOptional` (facultatif ; incompatible avec `IsBoardingFee`). Clé alternative `(SchoolId, Id)` cible des FK composites.

#### `class_fees` — barème (catégorie × classe)
`Amount numeric(12,2)` (0 permis), **xmin**, unique `(SchoolId, FeeCategoryId, ClassroomId)` parmi les vivantes. FK composites vers `classrooms` et `fee_categories`.

#### `fee_change_history` — journal du barème (**append-only** : `GRANT SELECT, INSERT` seulement)
`ClassFeeId`, `OldAmount?` (NULL = définition initiale), `NewAmount`, `ChangedByUserId`, `ChangedAt`. Index `(ClassFeeId, ChangedAt)`.

#### `enrollment_fee_lines` — détail figé du dû d'une inscription
`FeeCategoryId`, `Designation(60)`, `IsRecurring`, `UnitAmount`, `Months`, `LineTotal = UnitAmount × Months`, `AmountCollected` (part encaissée le jour de l'inscription, défaut 0), `MonthsCollected` (défaut 0).

#### `enrollments` (colonnes financières seulement)
`TotalDue numeric`, `AmountPaid numeric` (compteur), `ReceiptNumber` (reçu d'inscription, même registre gapless), **xmin**, `Status` (`Confirmed`/`Cancelled`).

#### `cashier_sessions`
`CashierId`, `OpenedAt`, `ClosedAt?`, `OpeningBalance`, `ClosingBalance?` (théorique, toutes méthodes), `ExpectedCashAmount?` (fonds + espèces), `ActualCashAmount?`, `DiscrepancyAmount?`, `DiscrepancyReason(500)?`, `Status` (`Open/Closed/Verified`). **Unique** `(SchoolId, CashierId) WHERE Status='Open'`.

#### `disbursements` — décaissements (table `Disbursements`, nommage PascalCase hérité)
`Reason(255)`, `Category` (texte : `Salaires, Maintenance, Fournitures, ChargesFixes, Divers, LoyerCharges, EauElectricite, TelecomsInternet, Carburant, AssurancesHonoraires`), `Amount decimal(18,2)` (**négatif** pour une contre-écriture), `VatRate decimal(5,4)?`, `VatAmount decimal(18,2)` (défaut 0), `PaymentMethod`, `Date` (DateOnly), `Beneficiary(150)`, `ReceiptUrl(500)?` (justificatif), `ReversalOfId?`. Contraintes : `CK_disbursements_vat_rate_range`, `CK_disbursements_reversal_negative` (`ReversalOfId IS NULL OR Amount < 0`), index unique partiel sur `ReversalOfId`, index `(SchoolId, Date)`.

#### `fee_installment_plans` / `fee_installments` — échéancier négocié
Plan : `EnrollmentId`, `Status` (`Active/Cancelled`), `Reason(500)`, `CreatedFromClassroomTemplate`. **Un seul `Active` par inscription** (`UX_fee_installment_plans_single_active_per_enrollment`). Échéance : `SequenceNo`, `Label(120)`, `Amount`, `DueDate` (DateOnly), unique `(SchoolId, PlanId, SequenceNo)`. La somme des échéances **doit égaler** `TotalDue`.

#### `financial_commitments` — reconnaissance de dette du tuteur
`EnrollmentId`, `Amount`, `DueDate`, `Terms`. N'altère jamais `AmountPaid`.

#### `debtor_reminder_batches` / `debtor_reminder_batch_items`
Lot par classe : `ClassroomId`, `ThresholdDays`, `GeneratedAt`, `Status` (`Draft/Sent/Dismissed`), `SentByUserId?`, `SentAt?`. Ligne : `EnrollmentId`, `StudentId`, `GuardianPhone(20)`, `RemainingBalance`, `DaysOverdue`, `SmsMessageId?` (après envoi).

#### `employee_contracts` — contrats du personnel
`TeacherId? | UserId? | (ExternalFullName/ExternalJobTitle/ExternalPhone)` — **exactement un** des trois ; `Type` (`Permanent/Vacataire`), `BaseSalary`, `HourlyRate`, `TransportAllowance`, `ApplyStatutoryDeductions` (défaut true), `PayoutMethod` (`Cash/BankTransfer/Wave/OrangeMoney`), `PayoutAccountReference(50)`, `EndDate?`, **xmin**. Uniques partiels : `TeacherId` et `UserId` **où `EndDate IS NULL`**.

#### `employee_contract_histories` (**append-only**)
`ChangeType` (`Amended/Closed`), termes avant/après, `EndDate?`, `Reason(500)` obligatoire, `ChangedByUserId`, `ChangedAt`.

#### `fiche_paies`
`EmployeeContractId`, `Month`, `Year`, `HoursWorked`, `GrossSalary`, `TransportAllowance`, `IpresEmployee`, `IpresEmployer`, `CssEmployer`, `Vrs` (CFCE employeur), `Brs`, `NetSalary`, `DeductionsApplied` (instantané du régime), annulation (`CancelledAt/CancelledByUserId/CancellationReason/ReplacedByFichePaieId`), règlement (`PaidAt/PaidByUserId/PaidWith/DisbursementId`), **xmin**. **Unique** `(SchoolId, EmployeeContractId, Month, Year) WHERE CancelledAt IS NULL`.

#### `teacher_hour_records`
`EmployeeContractId`, `Date`, `Hours numeric(6,2)`, `Note(500)`. Détail jour par jour justifiant la paie d'un vacataire (alimente la fiche de suivi PDF et la suggestion d'heures ; **ne** déclenche **pas** automatiquement la paie).

#### `taxe_declarations`
`Month`, `Year`, `TotalIpres`, `TotalCss`, `TotalVrs`, `TotalBrs`, `TvaCollected`, `TvaDeductible`, `NetTva`, `TotalDueToState`. **Unique** `(SchoolId, Month, Year)`, **xmin**.

### 5.3 Paramètres d'école liés au module (`SchoolSettings`)
| Paramètre | Défaut | Effet |
|---|---|---|
| `IsFinanceEnabled` | true | active tout le module (`[RequireModule(SchoolModule.Finance)]`) |
| `TuitionMonthsPerYear` | 9 (1–12) | multiplicateur des mensualités |
| `AllowFinanceToModifyFees` | false | délègue à la Finance la création de catégories / montants standard / modification de barème |
| `AllowFinanceToDeleteFees` | false | délègue la suppression de catégories / lignes de barème |
| `AllowFinanceToCancelPayments` | true | la Finance peut annuler un versement (48 h) ; la contre-passation reste Directeur |
| `AllowFinanceToCancelPayslips` | true | la Finance peut annuler/régénérer une fiche de paie |
| `SmsOnPaymentReceipt` | = SMS activés | SMS de confirmation d'encaissement |
| `SmsOnDuesReminder` | | autorise les relances SMS de débiteurs |
| `DebtorReminderThresholdDays` | 7 (1–365) | seuil de jours de retard pour entrer dans un lot de relance |
| `CashierSignatureUrl` | null | signature imprimée sur les reçus |
| Type d'établissement (Public/Privé) | Privé | pilote l'affichage ; **aucune logique conditionnelle** dans le code finance |

### 5.4 Énumérations, constantes de barème (paie/fiscalité) et formats

| Constante (`PayrollCalculator`) | Valeur |
|---|---|
| `IpresEmployeeRate` | 5,6 % |
| `IpresEmployerRate` | 8,4 % |
| `CssEmployerRate` | 7 % |
| `CfmEmployerRate` (VRS/CFCE) | 3 % du brut |
| `BrsRate` | 5 % de (brut − IPRES salarié) |
| `IpresCeiling` | 360 000 FCFA / mois |
| `CssCeiling` | 63 000 FCFA / mois |

Formules : `Brut = HourlyRate>0 ? Heures × HourlyRate : BaseSalary` ; `IPRES sal. = min(brut, 360000) × 5,6 %` ; `IPRES empl. = min(brut, 360000) × 8,4 %` ; `CSS = min(brut, 63000) × 7 %` ; `VRS = brut × 3 %` ; `BRS = max(0, brut − IPRES sal.) × 5 %` ; `Net = brut + transport − IPRES sal. − BRS`. Sans retenues : `Net = brut + transport`, tout le reste à 0.
Déclaration : `TotalIpres = Σ(IPRES sal. + IPRES empl.)`, `TotalCss = ΣCSS`, `TotalVrs`, `TotalBrs`, `NetTva = collectée − déductible`, **`TotalDueToState = VRS + BRS + max(NetTva, 0)`** (IPRES et CSS vont aux caisses sociales, pas à l'État).

> ⚠️ Ces barèmes sont **figés dans le code** (voir §13).

### 5.5 Code source des entités, énumérations et configurations

#### Socle commun

#### `src/SamaEcole.Domain/Common/AuditableEntity.cs`

````csharp
namespace SamaEcole.Domain.Common;

/// <summary>
/// Classe de base de toute entité métier. Voir docs/Volume_3_DDS.md et AGENTS.md règle #6.
/// Identifiant en UUID v7 (ordonnable), champs d'audit obligatoires, soft delete uniquement.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }

    /// <summary>
    /// Suppression logique uniquement. Ne jamais supprimer physiquement une entité métier (AGENTS.md règle #6).
    /// </summary>
    public void SoftDelete(string deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        DeletedBy = deletedBy;
    }

    /// <summary>
    /// Restauration volontaire d'une entité supprimée logiquement (conception soft delete 2026-10-01 §3.2).
    /// L'appelant vérifie avant l'unicité active et le tenant ; rien n'est fusionné.
    /// </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
    }
}
````


#### `src/SamaEcole.Domain/Common/ITenantEntity.cs`

````csharp
namespace SamaEcole.Domain.Common;

/// <summary>
/// Marque une entité comme rattachée à un établissement (multi-tenant).
/// Toute entité qui implémente cette interface DOIT être couverte par :
///   1. un Global Query Filter EF Core sur SchoolId (SamaEcole.Persistence)
///   2. une policy Row-Level Security PostgreSQL équivalente (migration)
/// Les deux, jamais un seul. Voir AGENTS.md règle #2 et docs/Volume_3_DDS.md.
/// </summary>
public interface ITenantEntity
{
    Guid SchoolId { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/MatriculeSequence.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Compteur de matricules, par école, par type (élève/enseignant) et par année scolaire —
/// la numérotation est propre à chaque établissement (docs/Volume_1_Cahier_des_Charges.md §2.2).
///
/// Incrémenté par <c>SamaEcole.Persistence.MatriculeGenerator</c> dans la même transaction que
/// l'insertion de l'élève/enseignant (AGENTS.md règle #3) : si l'enregistrement échoue, l'incrément
/// est annulé avec lui — c'est ce qui garantit l'absence de trou dans la numérotation.
/// </summary>
public class MatriculeSequence : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public MatriculeKind Kind { get; set; }

    /// <summary>Année scolaire portée par le matricule (le « 2026 » de <c>ELEV-2026-0001</c>).</summary>
    public int Year { get; set; }

    /// <summary>Dernier numéro attribué pour ce triplet (école, type, année). Le prochain sera <c>LastValue + 1</c>.</summary>
    public int LastValue { get; set; }
}
````


#### Entités du module

#### `src/SamaEcole.Domain/Entities/FeeCategory.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Catégorie de frais paramétrable par l'établissement (ticket JGK-F01, Volume 1 §7.1) :
/// inscription, réinscription, mensualité, examens, uniforme, transport, cantine, autres.
///
/// La liste est LIBRE, comme la nomenclature des classes : chaque école facture ce qu'elle veut. On
/// ne code donc aucune énumération de catégories — l'établissement les crée lui-même.
///
/// <see cref="IsRecurring"/> distingue une mensualité (due chaque mois) d'un frais ponctuel
/// (inscription, uniforme). Ce n'est pas décoratif : le calcul du montant dû à l'inscription
/// (ticket JGK-E01) multipliera une mensualité par le nombre de mois, jamais un frais ponctuel.
/// </summary>
public class FeeCategory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    /// <summary>Vrai pour une mensualité (due chaque mois), faux pour un frais ponctuel.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// Désigne cette catégorie comme frais d'internat (module Internat) : elle peut être incluse
    /// automatiquement sur l'inscription d'un élève Interne/Demi-pensionnaire (voir
    /// BoardingFeeLineBuilder), en plus des ClassFee ordinaires appliqués à tous les élèves de la
    /// classe. Faux par défaut — un choix explicite de l'école, comme IsRecurring.
    /// </summary>
    public bool IsBoardingFee { get; set; }

    /// <summary>
    /// Frais FACULTATIF (uniforme, tenue de sport, cantine…) : la famille le choisit à l'inscription et à la
    /// réinscription au lieu de le devoir d'office. Faux par défaut — un frais est OBLIGATOIRE tant que le
    /// Directeur ne l'a pas explicitement déclaré optionnel. C'est ce défaut qui protège inscription et
    /// mensualité : la liste des catégories étant libre, aucun nom n'est codé en dur pour les reconnaître.
    /// Une pension (<see cref="IsBoardingFee"/>) ne peut pas être optionnelle : elle a son propre choix
    /// (régime d'hébergement + IncludeBoardingFee).
    /// </summary>
    public bool IsOptional { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/ClassFee.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Montant d'une catégorie de frais pour UNE classe (ticket JGK-F01) — l'intersection
/// (catégorie × classe). C'est le barème : « la mensualité en CM2 est de 15 000 F ».
///
/// Le paramétrage se fait en deux temps (Volume 1 §7.4) :
///   1. un MONTANT STANDARD appliqué en masse à toutes les classes d'une catégorie ;
///   2. des EXCEPTIONS ajustées classe par classe.
/// Les deux passent par cette même table : une exception n'est qu'une ligne dont le montant diffère
/// du standard. Chaque modification est historisée dans <see cref="FeeChangeHistory"/>.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : le montant est une donnée financière sensible. Deux
/// directeurs qui l'éditent en même temps ne doivent jamais s'écraser en silence — le second reçoit
/// un 409. Le jeton est la colonne système <c>xmin</c> de PostgreSQL, exploitée directement par
/// EF Core (docs/Volume_3_DDS.md §1.3) et configurée dans ApplicationDbContext.
/// </summary>
public class ClassFee : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid FeeCategoryId { get; set; }

    public Guid ClassroomId { get; set; }

    /// <summary>Montant en FCFA. Zéro est permis (frais offert) ; jamais négatif.</summary>
    public decimal Amount { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/FeeChangeHistory.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal des modifications de barème (ticket JGK-F01, Volume 1 §7.4 : « Tout changement de montant
/// est historisé — ancien montant, nouveau montant, date, utilisateur »).
///
/// APPEND-ONLY, exactement comme <see cref="UserStatusHistory"/> : la migration RETIRE au rôle
/// applicatif les droits UPDATE et DELETE sur cette table. Un barème facture des familles ; son
/// historique doit être opposable, donc impossible à réécrire — même par un bug ou une injection SQL.
///
/// Pas de soft delete : une ligne d'audit ne se supprime pas.
/// </summary>
public class FeeChangeHistory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Ligne de barème concernée (catégorie × classe).</summary>
    public Guid ClassFeeId { get; set; }

    /// <summary>Montant AVANT le changement. Null lors de la toute première définition (création).</summary>
    public decimal? OldAmount { get; set; }

    public decimal NewAmount { get; set; }

    /// <summary>Auteur du changement — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid ChangedByUserId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/Enrollment.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Inscription (ou réinscription) d'un élève dans une classe pour une année scolaire (ticket JGK-E01,
/// docs/Volume_3_DDS.md §5.4). C'est le point de convergence du MVP : elle relie l'élève (matricule
/// généré à l'enregistrement, JGK-D01/B02), l'année scolaire active (JGK-C01), la classe (JGK-C02) et
/// le barème (JGK-F01) en un seul acte transactionnel.
///
/// <see cref="TotalDue"/> est le montant dû, CALCULÉ par le service d'inscription à partir du barème
/// de la classe — jamais fourni par le client (AGENTS.md règle #10). Une fois posé, aucune route
/// accessible au service Finance ne le modifie : la règle #4 réserve toute correction au
/// Secrétariat/Admin, et elle doit rester historisée. Le détail ligne à ligne est figé dans
/// <see cref="EnrollmentFeeLine"/> pour que le reçu reste fidèle même si le barème change ensuite.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : <c>TotalDue</c> est une donnée financière sensible. Le
/// jeton est la colonne système xmin de PostgreSQL, configurée dans EnrollmentConfiguration.
///
/// Unicité (SchoolId, StudentId, SchoolYearId) hors inscriptions annulées : « un élève n'a qu'une
/// inscription active par année scolaire » (DDS §5.4), tenue par la BASE et pas seulement par le C#.
/// </summary>
public class Enrollment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Année scolaire de l'inscription. Résolue serveur (année ACTIVE), jamais choisie par le client.</summary>
    public Guid SchoolYearId { get; set; }

    public Guid ClassroomId { get; set; }

    /// <summary>Première inscription ou réinscription (élève déjà connu, sans nouveau matricule).</summary>
    public EnrollmentType Type { get; set; }

    /// <summary>
    /// L'élève REDOUBLE cette classe (feature F). Distinct de <see cref="Type"/> : un redoublement peut
    /// être une nouvelle inscription (élève venu d'une autre école qui reprend le niveau) comme une
    /// réinscription. Alimente la case « Classe Redoublée » du bulletin (ReportCardDocument) : cochée
    /// [X] si vrai, vide sinon. Faux par défaut.
    /// </summary>
    public bool IsRepeating { get; set; }

    /// <summary>
    /// Élève TRANSFÉRÉ d'un autre établissement pour cette année (Évolution N°7, cartographie IEF). Avec
    /// <see cref="IsRepeating"/>, il donne le statut de l'élève au rapport de rentrée : Redoublant, sinon
    /// Transféré, sinon Nouveau. Faux par défaut : toute inscription existante reste « Nouveau ».
    /// </summary>
    public bool IsTransferredIn { get; set; }

    /// <summary>Établissement d'origine d'un élève transféré, en texte libre ; null sinon.</summary>
    public string? PreviousSchoolName { get; set; }

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Confirmed;

    /// <summary>Montant total dû, en FCFA. Calculé à partir du barème de la classe, jamais négatif.</summary>
    public decimal TotalDue { get; set; }

    /// <summary>
    /// Cumul des versements encaissés sur cette inscription (ticket JGK-F02), en FCFA. Le SOLDE restant
    /// est <c>TotalDue − AmountPaid</c>. Ce champ n'est PAS le montant dû (règle #4 : la Finance ne touche
    /// jamais à <see cref="TotalDue"/>) — c'est un compteur d'encaissements, incrémenté dans la même
    /// transaction que chaque <see cref="Payment"/>. C'est cette écriture sur l'inscription qui déclenche
    /// le verrou optimiste xmin : deux encaissements concurrents sur le même solde entrent en collision,
    /// le second reçoit un 409 (règle #5), jamais un sur-crédit silencieux.
    /// </summary>
    public decimal AmountPaid { get; set; }

    /// <summary>
    /// Numéro officiel du reçu d'inscription (ticket JGK-E02, ex. « REC-2025-0002 »). Attribué UNE FOIS,
    /// dans la même transaction que l'inscription (AGENTS.md règle #3, comme le matricule) : gapless,
    /// unique par école, jamais réémis. C'est ce numéro qui identifie le reçu PDF et sa réimpression.
    /// </summary>
    public string ReceiptNumber { get; set; } = string.Empty;

    public DateTimeOffset EnrolledAt { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/EnrollmentFeeLine.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une ligne du détail des frais d'une inscription (ticket JGK-E01) — le « compte financier »
/// initialisé à l'inscription. C'est un INSTANTANÉ du barème au moment où l'élève est inscrit :
/// <see cref="Designation"/> et <see cref="UnitAmount"/> y sont COPIÉS depuis <see cref="ClassFee"/>,
/// pas référencés. Ainsi le reçu (JGK-E02) restitue exactement ce qui a été facturé, même si le
/// Directeur ajuste le barème l'année suivante (le barème est historisé de son côté, JGK-F01).
///
/// <see cref="Months"/> distingue un frais ponctuel (1) d'une mensualité (le nombre de mensualités
/// par an, réglage <c>TuitionMonthsPerYear</c>) : <see cref="LineTotal"/> = UnitAmount × Months.
/// C'est ici que se matérialise la règle « une mensualité est multipliée par le nombre de mois,
/// jamais un frais ponctuel » (voir <see cref="FeeCategory"/>).
///
/// Pas de verrou optimiste : la ligne est écrite une fois, dans la transaction d'inscription, et
/// n'est jamais rééditée. Une correction de montant passe par le Secrétariat/Admin (règle #4), pas
/// par une modification muette de cet instantané.
/// </summary>
public class EnrollmentFeeLine : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    /// <summary>Catégorie d'origine (traçabilité). Le libellé facturé reste <see cref="Designation"/>, figé.</summary>
    public Guid FeeCategoryId { get; set; }

    /// <summary>Libellé facturé, copié du nom de la catégorie à l'instant de l'inscription.</summary>
    public required string Designation { get; set; }

    /// <summary>Vrai si la ligne provient d'une mensualité — pour l'affichage « × N mois » sur le reçu.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Montant unitaire (une mensualité, ou le frais ponctuel), en FCFA.</summary>
    public decimal UnitAmount { get; set; }

    /// <summary>Nombre de mois facturés : 1 pour un frais ponctuel, TuitionMonthsPerYear pour une mensualité.</summary>
    public int Months { get; set; }

    /// <summary>Total de la ligne = <see cref="UnitAmount"/> × <see cref="Months"/>, en FCFA.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Part de cette ligne RÉELLEMENT ENCAISSÉE au guichet le jour de l'inscription, en FCFA (0 si le
    /// tuteur n'a pas réglé ce frais ce jour-là). C'est la ventilation imprimée sur le reçu : une
    /// pièce comptable n'atteste que de ce qui est entré en caisse, jamais du dû annuel.
    ///
    /// Figée comme le reste de la ligne : réimprimer le reçu six mois plus tard doit redonner le
    /// détail de CE versement, pas l'état courant du compte de l'élève. Les versements suivants
    /// passent par la Caisse (JGK-F02) et ont leurs propres reçus.
    /// </summary>
    public decimal AmountCollected { get; set; }

    /// <summary>
    /// Nombre de mois couverts par <see cref="AmountCollected"/> sur une ligne récurrente (« Mensualité
    /// (× 1 mois) » sur le reçu) ; 1 pour un frais ponctuel réglé, 0 si rien n'a été encaissé. Stocké
    /// plutôt que déduit d'AmountCollected ÷ UnitAmount : une mensualité offerte (montant 0) rendrait
    /// la division impossible, et le reçu doit rester exact dans ce cas aussi.
    /// </summary>
    public int MonthsCollected { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/Payment.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Encaissement d'un versement contre une inscription (ticket JGK-F02, docs/Volume_3_DDS.md §5.5). C'est
/// l'acte de CAISSE : le service Finance encaisse ce qu'un parent verse sur le <c>TotalDue</c> figé à
/// l'inscription. La Finance ne fixe ni ne modifie jamais ce montant dû (AGENTS.md règle #4) — elle ne
/// fait qu'y imputer des versements.
///
/// Le SOLDE ne vit pas ici mais sur l'inscription : <see cref="Enrollment.AmountPaid"/> est le cumul des
/// versements, et le verrou optimiste (xmin) de l'inscription sérialise deux encaissements concurrents
/// sur le même solde (AGENTS.md règle #5, critère obligatoire du ticket) — jamais un sur-crédit silencieux.
///
/// Aucune suppression physique (règle #6) : une correction future passe par un statut
/// <see cref="PaymentStatus.Cancelled"/>, pas par un DELETE.
/// </summary>
public class Payment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid? CashierSessionId { get; set; }

    public PaymentCategory Category { get; set; } = PaymentCategory.Tuition;

    public string? ReferencePeriod { get; set; }

    /// <summary>Montant versé, en FCFA. Strictement positif, et jamais supérieur au solde restant.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Taux de TVA appliqué à ce versement (fraction, ex. 0.18 pour 18 %), ou null si non assujetti —
    /// les frais de scolarité (PaymentCategory.Tuition) sont typiquement exonérés au Sénégal, jamais
    /// une hypothèse codée en dur ici : c'est la Finance qui déclare l'assujettissement au moment de
    /// l'encaissement, transaction par transaction (aucune règle générale par catégorie n'est fiable).
    /// </summary>
    public decimal? VatRate { get; set; }

    /// <summary>
    /// Part de TVA comprise dans <see cref="Amount"/> (TTC), figée au moment de l'encaissement — jamais
    /// recalculée depuis un taux qui pourrait changer plus tard (même logique de fidélité que
    /// <see cref="BalanceAfter"/>). Zéro quand <see cref="VatRate"/> est null.
    /// </summary>
    public decimal VatAmount { get; set; }

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    /// <summary>
    /// Solde restant dû (en FCFA) JUSTE APRÈS ce versement, figé à l'encaissement. Un reçu est une pièce
    /// à un instant T : le réimprimer des mois plus tard doit montrer le solde de CE jour-là, pas l'état
    /// courant de l'inscription (même logique de fidélité que les lignes figées du reçu d'inscription,
    /// JGK-E02). « Déjà réglé » sur le reçu se déduit alors de <c>TotalDue − BalanceAfter</c>.
    /// </summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>
    /// Numéro officiel du reçu de paiement (ex. « REC-2025-0007 »). Attribué UNE FOIS dans la transaction
    /// d'encaissement (AGENTS.md règle #3), depuis le même registre de reçus gapless par établissement que
    /// le reçu d'inscription (JGK-E02) : un seul carnet de reçus officiel par école, jamais de trou.
    /// </summary>
    public string ReceiptNumber { get; set; } = string.Empty;

    /// <summary>
    /// Clé générée côté client (ticket JGK-L01, résilience réseau) : un retry après coupure sur LE
    /// MÊME encaissement ne crée jamais un second paiement — RecordPaymentCommandHandler rejoue le
    /// résultat déjà produit. Nullable : les paiements antérieurs à ce ticket n'en portent pas.
    /// </summary>
    public Guid? IdempotencyKey { get; set; }

    /// <summary>Utilisateur (module Finance/Directeur) qui a encaissé — issu du JWT, jamais du client.</summary>
    public Guid ReceivedByUserId { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    /// <summary>
    /// Annulation d'un versement saisi par erreur (spec 2026-10-17, §2.4). Renseignés ensemble par
    /// CancelPaymentCommandHandler, jamais autrement : le paiement garde son montant, son reçu et son numéro,
    /// seul son statut change. Null tant que le versement est valide.
    /// </summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Utilisateur (Directeur) qui a annulé le versement — issu du JWT, jamais du client.</summary>
    public Guid? CancelledByUserId { get; set; }

    /// <summary>Motif obligatoire de l'annulation, affiché sur le reçu et dans l'historique financier.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Destination de l'argent (conservé, réutilisé pour l'inscription, remboursé). Null tant que le versement est valide.</summary>
    public PaymentCancellationOutcome? CancellationOutcome { get; set; }

    /// <summary>
    /// Session de caisse OUVERTE de la personne qui a annulé, au moment de l'annulation — c'est dans cette
    /// session que l'annulation d'un versement d'une session DÉJÀ CLÔTURÉE est comptée (écriture négative,
    /// contre-passation). Null si l'auteur n'avait aucune session ouverte : l'annulation figure alors dans les
    /// états par date, mais dans aucun écart de caisse. Référence d'audit sans clé étrangère (comme
    /// <see cref="ReceivedByUserId"/>).
    /// </summary>
    public Guid? CancellationSessionId { get; set; }

    public Enrollment Enrollment { get; set; } = null!;

    public CashierSession? CashierSession { get; set; }

    public ICollection<PaymentBreakdown> Breakdowns { get; set; } = new List<PaymentBreakdown>();
}
````


#### `src/SamaEcole.Domain/Entities/PaymentBreakdown.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class PaymentBreakdown : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid PaymentId { get; set; }

    public Guid FeeCategoryId { get; set; }

    public decimal AmountAllocated { get; set; }

    /// <summary>
    /// Libellé libre de la colonne « Période / Note » du reçu de caisse (« Unique », « 2 jeux »,
    /// « Rattrapage octobre »), saisi au guichet. FACULTATIF : laissé vide, le reçu retombe sur
    /// <see cref="Payment.ReferencePeriod"/>, la période du versement entier — c'est le cas courant,
    /// une caisse ne saisit un libellé par ligne que lorsqu'il diffère de cette période.
    ///
    /// Purement descriptif : aucun calcul ne s'en sert, et il n'entre dans aucun total. Figé comme le
    /// reste de la ligne — réimprimer le reçu six mois plus tard doit redonner le libellé de CE
    /// versement, pas un libellé réécrit depuis.
    /// </summary>
    public string? Label { get; set; }

    public Payment Payment { get; set; } = null!;

    public FeeCategory FeeCategory { get; set; } = null!;
}
````


#### `src/SamaEcole.Domain/Entities/CashierSession.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class CashierSession : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid CashierId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public decimal OpeningBalance { get; set; }

    /// <summary>
    /// Solde de fermeture THÉORIQUE, toutes méthodes de paiement confondues (fonds d'ouverture + somme
    /// des encaissements de la session, Volume 1 §15.2) — c'est la figure déjà affichée en direct sur
    /// /caisse pendant la journée (<see cref="Application.Finance.Queries.GetCurrentCashierSession"/>).
    /// À ne jamais confondre avec <see cref="ExpectedCashAmount"/> : un versement par virement ou mobile
    /// money entre dans CE total, mais ne sera jamais dans le tiroir-caisse physique.
    /// </summary>
    public decimal? ClosingBalance { get; set; }

    /// <summary>
    /// Ticket JGK-F09 — contrôle du comptage physique. Espèces attendues dans le tiroir-caisse au
    /// moment de la clôture : fonds d'ouverture + SEULS les encaissements en espèces de la session
    /// (jamais les virements, chèques ou mobile money, qui ne transitent jamais par le tiroir). C'est
    /// cette figure, et non <see cref="ClosingBalance"/>, qui sert de référence à l'écart de caisse.
    /// </summary>
    public decimal? ExpectedCashAmount { get; set; }

    /// <summary>Espèces réellement comptées dans le tiroir-caisse par le caissier à la clôture (JGK-F09) — obligatoire pour clôturer.</summary>
    public decimal? ActualCashAmount { get; set; }

    /// <summary>ActualCashAmount − ExpectedCashAmount. Nul quand la caisse tombe juste.</summary>
    public decimal? DiscrepancyAmount { get; set; }

    /// <summary>Obligatoire dès que DiscrepancyAmount est non nul (JGK-F09) ; jamais rempli sinon.</summary>
    public string? DiscrepancyReason { get; set; }

    public CashierSessionStatus Status { get; set; } = CashierSessionStatus.Open;

    public User Cashier { get; set; } = null!;
}
````


#### `src/SamaEcole.Domain/Entities/Disbursement.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class Disbursement : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    public DisbursementCategory Category { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// Taux de TVA payé au fournisseur sur cette dépense (fraction, ex. 0.18 pour 18 %), ou null si non
    /// assujettie (ex. Salaires — une rémunération n'est jamais une livraison taxable). Déclaré par la
    /// Finance transaction par transaction, jamais déduit automatiquement d'une catégorie.
    /// </summary>
    public decimal? VatRate { get; set; }

    /// <summary>
    /// Part de TVA (déductible) comprise dans <see cref="Amount"/>, figée à la saisie — même logique de
    /// fidélité que Payment.VatAmount. Zéro quand <see cref="VatRate"/> est null.
    /// </summary>
    public decimal VatAmount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public DateOnly Date { get; set; }
    public string Beneficiary { get; set; } = string.Empty;
    public string? ReceiptUrl { get; set; }

    /// <summary>
    /// Non null UNIQUEMENT pour une contre-écriture : identifiant du décaissement d'origine qu'elle annule
    /// (montants opposés). Un décaissement est une écriture comptable : il n'est jamais supprimé ni modifié,
    /// il se corrige par une contre-écriture (conception soft delete 2026-10-01 §3.3).
    /// </summary>
    public Guid? ReversalOfId { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/FeeInstallmentPlan.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Échéancier personnalisé d'UNE inscription (Étape 5 — recouvrement). Tant qu'aucun plan
/// <see cref="FeeInstallmentPlanStatus.Active"/> n'existe pour une inscription, <c>GetStudentBalanceQuery</c>
/// et <c>GetDuesNoticeQuery</c> continuent de synthétiser des échéances mensuelles uniformes à partir
/// d'<see cref="EnrollmentFeeLine"/> (comportement historique, inchangé) — voir
/// <c>InstallmentScheduleCalculator</c>, seul endroit qui choisit entre les deux sources.
///
/// Au plus UN plan actif par inscription (EnrollmentConfiguration porte l'index unique partiel) : un
/// nouvel accord ne modifie jamais un plan existant, il le remplace (l'ancien passe à <c>Cancelled</c>),
/// pour garder la trace de ce qui a été renégocié — même logique que FeeChangeHistory pour le barème.
/// </summary>
public class FeeInstallmentPlan : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    public FeeInstallmentPlanStatus Status { get; set; } = FeeInstallmentPlanStatus.Active;

    /// <summary>Motif libre de l'échéancier négocié (ex. « difficulté financière, accord du 12/07 »).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Vrai si ce plan a été créé par application en masse d'un modèle à toute une classe
    /// (ApplyFeeInstallmentPlanToClassroomCommand) plutôt que saisi élève par élève — traçabilité
    /// seulement, ne change aucun comportement de calcul.
    /// </summary>
    public bool CreatedFromClassroomTemplate { get; set; }

    public ICollection<FeeInstallment> Installments { get; set; } = new List<FeeInstallment>();
}
````


#### `src/SamaEcole.Domain/Entities/FeeInstallment.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une échéance d'un <see cref="FeeInstallmentPlan"/> : un montant et une date, librement négociés
/// (contrairement à la synthèse mensuelle uniforme dérivée d'<see cref="EnrollmentFeeLine"/>). La
/// somme des <see cref="Amount"/> d'un plan doit égaler <see cref="Enrollment.TotalDue"/> — vérifié
/// par le Validator de la commande de création, jamais en base (même parti pris que
/// RecordPaymentCommandValidator pour la borne du versement).
///
/// N'enregistre jamais elle-même de paiement : l'allocation cumulative de
/// <see cref="Enrollment.AmountPaid"/> à chaque échéance (Paid/Partial/Overdue/Pending) est calculée
/// à la volée par InstallmentScheduleCalculator, exactement comme pour les échéances dérivées — une
/// échéance personnalisée n'est qu'un calendrier différent, pas un mécanisme d'encaissement distinct
/// (règle #4 : seul un Payment réel modifie le solde).
/// </summary>
public class FeeInstallment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid FeeInstallmentPlanId { get; set; }

    /// <summary>Ordre d'affichage et de calcul (1, 2, 3…) — l'allocation cumulative des paiements suit cet ordre.</summary>
    public int SequenceNo { get; set; }

    public required string Label { get; set; }

    public decimal Amount { get; set; }

    public DateOnly DueDate { get; set; }

    public FeeInstallmentPlan FeeInstallmentPlan { get; set; } = null!;
}
````


#### `src/SamaEcole.Domain/Entities/FinancialCommitment.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Engagement financier (reconnaissance de dette) souscrit par le tuteur d'un élève pour régulariser
/// un solde impayé selon un échéancier convenu avec l'établissement — trace écrite de l'accord, pas un
/// mécanisme d'encaissement : il ne touche jamais <see cref="Enrollment.AmountPaid"/> (AGENTS.md règle
/// #4, seul un <c>Payment</c> réel le fait).
/// </summary>
public class FinancialCommitment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string Terms { get; set; } = null!;
}
````


#### `src/SamaEcole.Domain/Entities/DebtorReminderBatch.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Lot de relance de débiteurs, généré chaque nuit par DebtorAgingHostedService pour UNE classe
/// (Étape 5 — recouvrement semi-automatique). Toujours créé <see cref="DebtorReminderBatchStatus.Draft"/> :
/// §13.7 du cahier des charges interdit l'envoi de masse automatique, seul un clic humain
/// (SendDebtorReminderBatchCommand) fait effectivement partir les SMS des <see cref="DebtorReminderBatchItem"/>
/// qu'il contient.
/// </summary>
public class DebtorReminderBatch : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ClassroomId { get; set; }

    /// <summary>Seuil de jours de retard appliqué au moment de la génération (SchoolSettings.DebtorReminderThresholdDays).</summary>
    public int ThresholdDays { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public DebtorReminderBatchStatus Status { get; set; } = DebtorReminderBatchStatus.Draft;

    /// <summary>Utilisateur (Directeur/Finance) qui a déclenché l'envoi — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid? SentByUserId { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public ICollection<DebtorReminderBatchItem> Items { get; set; } = new List<DebtorReminderBatchItem>();
}
````


#### `src/SamaEcole.Domain/Entities/DebtorReminderBatchItem.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Un débiteur candidat au sein d'un <see cref="DebtorReminderBatch"/> — instantané pris au moment de
/// la génération du lot (montant dû, retard). <see cref="SmsMessageId"/> n'est renseigné qu'après
/// l'envoi effectif du lot (SendDebtorReminderBatchCommand), et relie cette ligne à la trace
/// SmsMessage réellement mise en file par ISmsDispatcher.
/// </summary>
public class DebtorReminderBatchItem : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid DebtorReminderBatchId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid StudentId { get; set; }

    public string? GuardianPhone { get; set; }

    /// <summary>Solde restant dû au moment de la génération du lot, en FCFA.</summary>
    public decimal RemainingBalance { get; set; }

    /// <summary>Ancienneté de la plus ancienne échéance impayée, en jours, au moment de la génération du lot.</summary>
    public int DaysOverdue { get; set; }

    /// <summary>Renseigné uniquement après envoi — trace du SMS réellement mis en file pour ce débiteur.</summary>
    public Guid? SmsMessageId { get; set; }

    public DebtorReminderBatch DebtorReminderBatch { get; set; } = null!;
}
````


#### `src/SamaEcole.Domain/Entities/EmployeeContract.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class EmployeeContract : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid? TeacherId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>
    /// Personnel SANS compte (gardien, cuisinier, agent d'entretien…, Volume 1 §14.7) : ni Teacher ni User,
    /// seulement une identité saisie sur le contrat. Renseigné UNIQUEMENT quand <see cref="TeacherId"/> et
    /// <see cref="UserId"/> sont nuls. Cette personne ne se connecte jamais à la plateforme.
    /// </summary>
    public string? ExternalFullName { get; set; }
    public string? ExternalJobTitle { get; set; }
    public string? ExternalPhone { get; set; }

    public ContractType Type { get; set; }

    public decimal BaseSalary { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TransportAllowance { get; set; }

    /// <summary>
    /// Retenues et charges légales (IPRES, CSS, VRS/CFCE, BRS) appliquées à la paie de cette personne. Vrai par
    /// défaut : c'est le calcul historique. Faux pour une structure qui ne cotise pas (ex. un Daara) : le net payé
    /// est alors le salaire brut plus la prime de transport, sans aucune retenue. Se règle par contrat, donc une
    /// même école peut avoir du personnel avec retenues et du personnel sans.
    /// </summary>
    public bool ApplyStatutoryDeductions { get; set; } = true;

    /// <summary>Moyen de règlement du salaire (ticket JGK-K02). Cash par défaut — c'est l'existant avant ce ticket.</summary>
    public PayoutMethod PayoutMethod { get; set; } = PayoutMethod.Cash;

    /// <summary>
    /// RIB/IBAN pour un virement bancaire, numéro de téléphone pour Wave/Orange Money — la forme
    /// dépend de <see cref="PayoutMethod"/>, non validée ici (texte libre). Jamais journalisée en
    /// clair dans un log applicatif (Volume 7).
    /// </summary>
    public string? PayoutAccountReference { get; set; }

    /// <summary>
    /// Date de clôture du contrat (Volume 1 §14.1 : « un contrat n'est jamais supprimé physiquement…
    /// il est clôturé à une date, et reste consultable pour l'historique de paie et les attestations »).
    /// Null tant que le contrat est actif. Une fois posée, aucune nouvelle fiche de paie ne peut plus
    /// être générée pour ce contrat (voir GenerateFichePaieCommandHandler) — la dernière fiche due doit
    /// être générée AVANT la clôture. Le contrat lui-même n'est jamais supprimé : il reste lisible pour
    /// l'historique de paie et l'attestation de travail (§14.4).
    /// </summary>
    public DateOnly? EndDate { get; set; }

    public Teacher? Teacher { get; set; }
    public User? User { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/EmployeeContractHistory.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal des changements d'un contrat (Volume 1 §14.1 : rémunération modifiée, ou contrat clôturé).
///
/// APPEND-ONLY, exactement comme <see cref="UserStatusHistory"/>/<see cref="FeeChangeHistory"/> : la
/// migration RETIRE au rôle applicatif les droits UPDATE et DELETE. Un contrat engage un salaire ; son
/// historique doit être opposable, donc impossible à réécrire — même par un bug ou une injection SQL.
///
/// Pas de soft delete ici, pour la même raison : une ligne d'audit ne se supprime pas.
/// </summary>
public class EmployeeContractHistory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public EmployeeContractChangeType ChangeType { get; set; }

    /// <summary>Termes AVANT le changement (toujours renseignés, y compris pour une clôture — les termes ne changent alors pas).</summary>
    public decimal PreviousBaseSalary { get; set; }
    public decimal PreviousHourlyRate { get; set; }
    public decimal PreviousTransportAllowance { get; set; }

    /// <summary>Termes APRÈS le changement — identiques aux précédents si <see cref="ChangeType"/> est <see cref="EmployeeContractChangeType.Closed"/>.</summary>
    public decimal NewBaseSalary { get; set; }
    public decimal NewHourlyRate { get; set; }
    public decimal NewTransportAllowance { get; set; }

    /// <summary>Renseigné uniquement pour <see cref="EmployeeContractChangeType.Closed"/>.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Motif OBLIGATOIRE, même exigence que ChangeUserStatusCommand (JGK-A05) : un changement de salaire ou une clôture sans justification n'est pas traçable.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Auteur du changement — jamais lu depuis la requête, toujours depuis le JWT.</summary>
    public Guid ChangedByUserId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/FichePaie.cs`

````csharp
using SamaEcole.Domain.Enums;
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class FichePaie : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public int Month { get; set; }
    public int Year { get; set; }

    public decimal HoursWorked { get; set; }
    
    public decimal GrossSalary { get; set; }
    public decimal TransportAllowance { get; set; }
    
    public decimal IpresEmployee { get; set; }
    public decimal IpresEmployer { get; set; }
    
    public decimal CssEmployer { get; set; }
    
    public decimal Vrs { get; set; }
    public decimal Brs { get; set; }
    
    public decimal NetSalary { get; set; }

    /// <summary>
    /// Instantané du régime de la paie à la génération : faux = fiche « sans retenues » (net = brut + transport, aucune
    /// cotisation ni retenue). Conservé sur la fiche et non relu sur le contrat : changer le régime d'un contrat ne
    /// réécrit jamais une fiche déjà éditée, et le bulletin PDF reste fidèle à ce qui a été payé.
    /// </summary>
    public bool DeductionsApplied { get; set; } = true;

    // ----- Annulation (Volume 1 §14.5) -----
    // Une fiche ne se supprime ni ne se réécrit : elle s'ANNULE (la ligne reste, avec auteur, date et motif)
    // puis, au besoin, une nouvelle fiche est régénérée pour la même période. Seules les fiches non annulées
    // comptent pour l'unicité (contrat, mois, année), pour la déclaration fiscale et pour la trésorerie.

    /// <summary>Non null = fiche annulée. Irréversible.</summary>
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>Fiche qui remplace celle-ci après une régénération, ou null.</summary>
    public Guid? ReplacedByFichePaieId { get; set; }

    // ----- Règlement (Volume 1 §14.6) -----
    // Le règlement crée un décaissement « Salaires » (trésorerie/comptabilité) ; l'annulation d'une fiche payée
    // le contre-passe. Aucune clé étrangère : la ligne de décaissement est un registre immuable que la purge
    // d'école et l'historique doivent pouvoir traiter indépendamment.

    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaidByUserId { get; set; }
    public PaymentMethod? PaidWith { get; set; }
    public Guid? DisbursementId { get; set; }

    public bool IsCancelled => CancelledAt is not null;
    public bool IsPaid => PaidAt is not null;
}
````


#### `src/SamaEcole.Domain/Entities/TeacherHourRecord.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Heures effectuées un jour donné par un enseignant Vacataire, pour justifier le calcul de son
/// bulletin de paie horaire (<see cref="EmployeeContract.HourlyRate"/>). Distinct de
/// <c>PayslipDto.HoursWorked</c> (un total agrégé saisi à la génération de la fiche) : cette table
/// est le détail jour par jour qui permet d'imprimer une fiche de suivi vérifiable, pas une
/// alimentation automatique du calcul de paie (hors périmètre de ce ticket — voir cahier des charges
/// Documents administratifs).
/// </summary>
public class TeacherHourRecord : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Note { get; set; }
}
````


#### `src/SamaEcole.Domain/Entities/TaxeDeclaration.cs`

````csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class TaxeDeclaration : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public int Month { get; set; }
    public int Year { get; set; }

    public decimal TotalIpres { get; set; }
    public decimal TotalCss { get; set; }
    public decimal TotalVrs { get; set; }
    public decimal TotalBrs { get; set; }

    public decimal TvaCollected { get; set; }
    public decimal TvaDeductible { get; set; }
    public decimal NetTva { get; set; }

    public decimal TotalDueToState { get; set; }
}
````


#### Énumérations

#### `src/SamaEcole.Domain/Enums/DisbursementCategory.cs`

````csharp
namespace SamaEcole.Domain.Enums;

public enum DisbursementCategory
{
    Salaires,
    Maintenance,
    Fournitures,
    ChargesFixes,
    Divers,
    LoyerCharges,
    EauElectricite,
    TelecomsInternet,
    Carburant,
    AssurancesHonoraires
}
````


#### `src/SamaEcole.Domain/Enums/FinanceRecoveryEnums.cs`

````csharp
namespace SamaEcole.Domain.Enums;

/// <summary>
/// Cycle de vie d'un échéancier personnalisé (Étape 5 — recouvrement). <c>Cancelled</c> ne supprime
/// jamais la ligne (règle #6) : un nouvel échéancier accepté sur la même inscription bascule l'ancien
/// à ce statut plutôt que de l'écraser, pour garder la trace de ce qui a été renégocié.
/// </summary>
public enum FeeInstallmentPlanStatus
{
    Active,
    Cancelled
}

/// <summary>
/// Cycle de vie d'un lot de relance de débiteurs (Étape 5 — recouvrement semi-automatique).
/// <c>Draft</c> : généré chaque nuit par le calcul d'ancienneté, jamais envoyé de lui-même (§13.7 du
/// cahier des charges : aucun envoi de masse automatique). <c>Sent</c> : un Directeur/Finance a
/// déclenché l'envoi à la main. <c>Dismissed</c> : écarté sans envoi (ex. classe déjà relancée
/// autrement) — jamais supprimé physiquement (règle #6).
/// </summary>
public enum DebtorReminderBatchStatus
{
    Draft,
    Sent,
    Dismissed
}
````


#### `src/SamaEcole.Domain/Enums/CommonEnums.cs (lignes 120–200 sur 483)`

````csharp
    Pending,
    Confirmed,
    Cancelled,
    DroppedOut,
    Transferred
}

/// <summary>
/// Moyens de paiement d'un élève acceptés en V1 (DDS §6, Volume 1 §7.3) : espèces, chèque, virement,
/// Mobile Money (Wave / Orange Money — saisi MANUELLEMENT en V1, sans intégration agrégateur ; celle-ci
/// ne concerne que les abonnements, règle #11). Distinct de <c>SubscriptionPaymentMethod</c>.
/// </summary>
public enum PaymentMethod
{
    Cash,
    Cheque,
    Transfer,
    MobileMoney
}

/// <summary>
/// Destination de l'argent d'un versement ANNULÉ (spec 2026-10-17, §2.4). Trace comptable uniquement : l'annulation
/// elle-même se comptabilise de la même façon dans les trois cas (écriture négative datée du jour de l'annulation).
/// <c>KeptBySchool</c> : simple correction, l'argent reste à l'école (erreur de saisie, mauvais élève, doublon…) —
/// la valeur par défaut, car les écoles remboursent très rarement. <c>ReusedForEnrollment</c> : l'argent est
/// réutilisé pour l'inscription de l'enfant (le versement sera ressaisi correctement). <c>RefundedToParent</c> :
/// l'argent est rendu au parent.
/// </summary>
public enum PaymentCancellationOutcome
{
    KeptBySchool,
    ReusedForEnrollment,
    RefundedToParent
}

/// <summary>
/// Statut d'un paiement d'élève (DDS §6). <c>Paid</c> : ce versement solde l'inscription ; <c>Partial</c> :
/// un solde reste dû après lui ; <c>Cancelled</c> : versement annulé par CancelPaymentCommand (motif, date, auteur
/// et destination de l'argent conservés sur le paiement, qui n'est jamais effacé). Le statut GLOBAL d'une inscription se déduit de son solde, pas d'ici.
/// </summary>
public enum PaymentStatus
{
    Paid,
    Partial,
    Cancelled
}

public enum PaymentCategory
{
    Enrollment,
    Tuition,
    Exam,
    Canteen,
    Other,
    Transport,
    Uniformes,
    ActivitesPeriscolaires,
    FournituresVente
}

public enum CashierSessionStatus
{
    Open,
    Closed,
    Verified
}

public enum SubscriptionPlan
{
    Primaire,
    Standard,
    Premium
}

/// <summary>
/// Statut de l'établissement demandeur : public ou privé. Détermine le mode de tarification de la grille
/// (public : par élève, sur devis ; privé : forfait annuel). Persisté en TEXTE. Valeur par défaut de la
/// colonne : <see cref="Private"/>, ce qui est le cas de toute demande antérieure à la grille.
/// </summary>
public enum SchoolOwnership
{
````


#### `src/SamaEcole.Domain/Enums/CommonEnums.cs (lignes 415–470 sur 483)`

````csharp
public enum DisciplineType
{
    Avertissement,
    Blame,
    Retenue,
    Exclusion
}

public enum ContractType
{
    Permanent,
    Vacataire
}

/// <summary>
/// Moyen de règlement du salaire d'un membre du personnel (ticket JGK-K02). Distinct de
/// <see cref="PaymentMethod"/> — celui-ci décrit un encaissement élève, jamais un versement RH ;
/// les deux domaines ne doivent jamais partager une énumération, même si Wave/Orange Money s'y
/// retrouvent conceptuellement des deux côtés.
/// </summary>
public enum PayoutMethod
{
    Cash,
    BankTransfer,
    Wave,
    OrangeMoney
}

/// <summary>
/// Nature d'un changement journalisé dans <see cref="Entities.EmployeeContractHistory"/> (Volume 1
/// §14.1). <c>Amended</c> : changement de rémunération sur un contrat resté actif (salaire de base,
/// taux horaire, prime de transport). <c>Closed</c> : clôture définitive — les montants ne changent
/// pas, seule <see cref="Entities.EmployeeContract.EndDate"/> est posée.
/// </summary>
public enum EmployeeContractChangeType
{
    Amended,
    Closed
}

/// <summary>
/// Type d'une salle physique (module Infrastructures, <see cref="Entities.Room"/>).
/// </summary>
public enum RoomType
{
    SalleDeClasse,
    Laboratoire,
    Bureau,
    Dortoir,
    Autre
}

/// <summary>
/// Suite donnée à une convocation de parent (<see cref="Entities.ParentSummons"/>).
/// <c>Scheduled</c> : convocation émise, entretien pas encore tenu — seul état depuis lequel une
/// suite peut être posée. <c>Honored</c> : le parent s'est présenté. <c>Missed</c> : il ne s'est pas
````


#### `src/SamaEcole.Domain/Enums/SmsEnums.cs`

````csharp
namespace SamaEcole.Domain.Enums;

/// <summary>
/// Événement à l'origine d'un SMS. Chaque valeur automatique correspond à un commutateur
/// d'activation dans les paramètres de l'école (SchoolSettings) : une école peut vouloir les
/// alertes d'assiduité sans les relances d'impayés, ou l'inverse.
/// </summary>
public enum SmsTrigger
{
    /// <summary>Retard ou absence saisi(e) — alerte au parent/tuteur.</summary>
    AttendanceAlert,

    /// <summary>Relance d'impayé / rappel de frais de scolarité.</summary>
    DuesReminder,

    /// <summary>Confirmation d'un encaissement, avec le lien vers le reçu.</summary>
    PaymentReceipt,

    /// <summary>
    /// Mise à disposition du bulletin de notes. Le PDF lui-même part par WhatsApp ou e-mail : un SMS
    /// ne transporte pas de pièce jointe, il annonce seulement au tuteur que le bulletin est prêt.
    /// </summary>
    ReportCard,

    /// <summary>Envoi ponctuel décidé par un utilisateur, hors automatisme.</summary>
    Manual,

    /// <summary>
    /// Dispatch d'une convocation d'examen officiel (centre, numéro de table) — geste explicite du
    /// Secrétariat/Directeur, DÉLIBÉRÉMENT sans commutateur, même raisonnement que <see cref="Manual"/>.
    /// </summary>
    ExamConvocation
}

/// <summary>
/// Cycle de vie d'un SMS. Les trois premiers statuts sont ceux d'une FILE D'ATTENTE : le déclencheur
/// métier ne fait qu'inscrire le message en <see cref="Pending"/> et rend la main ; c'est
/// SmsQueueProcessor qui le remet au fournisseur, hors de la requête HTTP.
///
///   Pending ──(worker)──▶ Sent ──(accusé de réception)──▶ Delivered
///      │                   │
///      │                   └────(accusé négatif)────────▶ Failed
///      └──(tentatives épuisées)───────────────────────────▶ Failed
///
/// Stocké en TEXTE (HasConversion&lt;string&gt;) : l'ordre de déclaration ne porte donc aucune
/// sémantique, et ajouter une valeur ne réécrit pas les lignes existantes.
/// </summary>
public enum SmsDeliveryStatus
{
    /// <summary>
    /// En file, pas encore remis au fournisseur. Le solde est DÉJÀ débité (voir SmsDispatcher) :
    /// c'est ce qui empêche une relance de masse de promettre plus de segments qu'il n'en reste.
    /// </summary>
    Pending,

    /// <summary>Accepté par le fournisseur. La remise effective au téléphone n'est pas garantie pour autant.</summary>
    Sent,

    /// <summary>
    /// Remise au téléphone CONFIRMÉE par l'accusé de réception (DLR) de l'agrégateur. Seul statut qui
    /// atteste qu'un parent a réellement reçu l'alerte — <see cref="Sent"/> n'atteste que de l'envoi.
    /// </summary>
    Delivered,

    /// <summary>Refusé par le fournisseur (numéro invalide, quota, panne), tentatives épuisées.</summary>
    Failed,

    /// <summary>Non tenté : le solde SMS de l'école était épuisé. Journalisé pour expliquer le silence.</summary>
    InsufficientCredit
}
````


#### Paramètres d'école (extrait `SchoolSettings` + valeurs par défaut)

#### `src/SamaEcole.Domain/Entities/SchoolSettings.cs`

````csharp
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Paramètres d'un établissement (ticket JGK-B02, openapi.yaml §SchoolSettings).
///
/// Table tenant à part entière (SchoolId + RLS + Global Query Filter), et non des colonnes de
/// `schools` : `schools` échappe volontairement à la RLS puisqu'elle DÉFINIT le tenant. Y loger les
/// paramètres les priverait de toute protection en base, alors qu'ils pilotent des règles métier —
/// à commencer par le format des matricules.
/// </summary>
public class SchoolSettings : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Barème de notation : 10 ou 20 (docs/Volume_1_Cahier_des_Charges.md).</summary>
    public int GradingScale { get; set; } = SchoolSettingsDefaults.GradingScale;

    /// <summary>Ex. « ELEV-{YEAR}-{SEQ:4} » — voir MatriculeFormat.</summary>
    public string StudentMatriculeFormat { get; set; } = SchoolSettingsDefaults.StudentMatriculeFormat;

    public string TeacherMatriculeFormat { get; set; } = SchoolSettingsDefaults.TeacherMatriculeFormat;

    /// <summary>Déconnexion automatique par inactivité (ticket JGK-A06, gelé — la valeur est déjà stockée).</summary>
    public int AutoLogoutMinutes { get; set; } = SchoolSettingsDefaults.AutoLogoutMinutes;

    public string DateFormat { get; set; } = SchoolSettingsDefaults.DateFormat;

    /// <summary>
    /// Nombre de mensualités facturées sur une année scolaire (ticket JGK-E01). Le calcul du montant
    /// dû à l'inscription multiplie chaque mensualité par ce nombre ; un frais ponctuel n'est jamais
    /// multiplié. Les écoles sénégalaises raisonnent en « tranches » (souvent 9), d'où un réglage
    /// explicite plutôt qu'une déduction des dates de l'année — celles-ci peuvent couvrir des mois
    /// non facturés.
    /// </summary>
    public int TuitionMonthsPerYear { get; set; } = SchoolSettingsDefaults.TuitionMonthsPerYear;

    /// <summary>
    /// Ticket JGK-G02 — au choix du Directeur de CHAQUE école (colonne, pas un rôle codé en dur) :
    /// délègue au Secrétariat la gestion du barème, des matières/coefficients et des mentions du
    /// bulletin. Lu par CanManageGradingScaleHandler (SamaEcole.Web.Authorization), jamais par une
    /// simple comparaison de rôle. Faux par défaut : la délégation est un choix explicite du
    /// Directeur, pas un acquis silencieux à l'activation du module.
    /// </summary>
    public bool AllowSecretaryToManageGrading { get; set; } = SchoolSettingsDefaults.AllowSecretaryToManageGrading;

    /// <summary>
    /// Au choix du Directeur de CHAQUE école : délègue à la Finance le droit d'ajuster un montant de
    /// barème déjà défini (PUT /finance/fees/{id}), normalement réservé au Directeur (AGENTS.md règle
    /// #4). Lu par CanModifyFeesHandler (SamaEcole.Web.Authorization). Faux par défaut : la Finance
    /// encaisse, elle ne fixe pas les montants tant que le Directeur n'a pas explicitement délégué.
    /// </summary>
    public bool AllowFinanceToModifyFees { get; set; } = SchoolSettingsDefaults.AllowFinanceToModifyFees;

    /// <summary>
    /// Au choix du Directeur de CHAQUE école : délègue à la Finance le droit de supprimer (soft
    /// delete) une catégorie de frais ou une ligne de barème. Séparé de <see cref="AllowFinanceToModifyFees"/>
    /// : supprimer une catégorie entière est un geste plus lourd de conséquences (elle disparaît de
    /// toute l'interface) qu'ajuster un montant, donc un commutateur distinct. Lu par
    /// CanDeleteFeesHandler (SamaEcole.Web.Authorization). Faux par défaut.
    /// </summary>
    public bool AllowFinanceToDeleteFees { get; set; } = SchoolSettingsDefaults.AllowFinanceToDeleteFees;

    /// <summary>
    /// Au choix du Directeur de CHAQUE école : autorise la Finance (le caissier) à ANNULER un versement saisi par erreur,
    /// dans le délai de grâce de 48 h (PaymentCancellationPolicy). Vrai par défaut : le caissier corrige sa propre erreur
    /// de caisse sans attendre la direction ; le Directeur peut retirer ce droit. La contre-passation au-delà du délai
    /// reste, elle, réservée au Directeur dans tous les cas. Lu par CanCancelPaymentsHandler (SamaEcole.Web.Authorization).
    /// </summary>
    public bool AllowFinanceToCancelPayments { get; set; } = SchoolSettingsDefaults.AllowFinanceToCancelPayments;

    /// <summary>
    /// La Finance peut-elle annuler / corriger (régénérer) une fiche de paie (Volume 1 §14.5) ? Vrai par défaut ; le
    /// Directeur peut retirer ce droit, auquel cas seul le Directeur annule une fiche. Payer une fiche, lui, reste
    /// ouvert à la Finance. Lu par CanCancelPayslipsHandler (SamaEcole.Web.Authorization).
    /// </summary>
    public bool AllowFinanceToCancelPayslips { get; set; } = SchoolSettingsDefaults.AllowFinanceToCancelPayslips;

    /// <summary>URL de l'image de la signature du directeur, injectée sur les reçus et bulletins.</summary>
    public string? DirectorSignatureUrl { get; set; }

    /// <summary>URL de l'image de la signature du secrétariat, injectée sur les certificats et bulletins.</summary>
    public string? SecretarySignatureUrl { get; set; }

    /// <summary>URL de l'image de la signature du caissier/service financier, injectée sur les reçus.</summary>
    public string? CashierSignatureUrl { get; set; }

    /// <summary>URL de l'image du cachet officiel de l'établissement, injecté sur les reçus et bulletins.</summary>
    public string? OfficialStampUrl { get; set; }

    /// <summary>URL de l'image de la signature du Surveillant Général, injectée sur les documents de Vie Scolaire (billets d'entrée/sortie, fiches de discipline).</summary>
    public string? SurveillantSignatureUrl { get; set; }

    /// <summary>
    /// Type d'établissement : Prive (défaut) ou Public. Pilote l'affichage du module Finance dans la
    /// navigation (sidebar). Rertrocompat : les écoles existantes (colonne absente) obtiennent Prive
    /// via la valeur par défaut de la migration — aucun accès financier ne leur est retiré.
    /// </summary>
    public TypeEtablissement TypeEtablissement { get; set; } = SchoolSettingsDefaults.TypeEtablissement;

    /// <summary>
    /// Solde de SMS restant, en SEGMENTS et non en messages (voir SmsMessage.SegmentCount) : un
    /// message long en consomme plusieurs, et un solde compté en messages divergerait de la facture
    /// de l'agrégateur.
    ///
    /// Décrémenté ATOMIQUEMENT en base par SmsDispatcher (UPDATE … WHERE solde ≥ coût), jamais par un
    /// lire-modifier-écrire côté application : deux alertes simultanées sur la même école feraient
    /// sinon disparaître un débit, et l'école enverrait des SMS qu'elle n'a pas payés.
    /// </summary>
    public int SmsCreditBalance { get; set; } = SchoolSettingsDefaults.SmsCreditBalance;

    /// <summary>Alerter le parent par SMS à la saisie d'un retard ou d'une absence.</summary>
    public bool SmsOnAttendanceAlert { get; set; } = SchoolSettingsDefaults.SmsAlertsEnabled;

    /// <summary>Relancer par SMS les impayés de scolarité.</summary>
    public bool SmsOnDuesReminder { get; set; } = SchoolSettingsDefaults.SmsAlertsEnabled;

    /// <summary>Confirmer par SMS chaque encaissement, avec le lien vers le reçu.</summary>
    public bool SmsOnPaymentReceipt { get; set; } = SchoolSettingsDefaults.SmsAlertsEnabled;

    /// <summary>
    /// Nombre de jours de retard au-delà duquel DebtorAgingHostedService inclut un débiteur dans un
    /// lot de relance brouillon (Étape 5 — recouvrement). N'a d'effet que si <see cref="SmsOnDuesReminder"/>
    /// est actif : ce réglage cadre le calcul, il ne l'active pas — même logique de double
    /// interrupteur que pour l'application d'un barème (portée, puis activation).
    /// </summary>
    public int DebtorReminderThresholdDays { get; set; } = SchoolSettingsDefaults.DebtorReminderThresholdDays;

    /// <summary>
    /// Modules activés/désactivés par le Directeur, indépendamment de la formule d'abonnement — un
    /// second axe, distinct de <see cref="Domain.Enums.Feature"/>/PlanFeatures (formule payante). Lus
    /// par ModuleAuthorizationHandler (SamaEcole.Web.Authorization, [RequireModule]) et par la sidebar
    /// (sidebarNav() dans auth.js). Voir <see cref="Domain.Enums.SchoolModule"/>.
    ///
    /// Pédagogie et Finance forment le socle métier livré : activés par défaut, comme le reste du
    /// produit avant ce réglage. Internat et Coran n'ont encore aucun écran ni route derrière eux
    /// (réglage anticipé) : désactivés par défaut, sans effet aujourd'hui quelle que soit leur valeur.
    /// </summary>
    public bool IsPedagogyEnabled { get; set; } = SchoolSettingsDefaults.IsPedagogyEnabled;

    public bool IsFinanceEnabled { get; set; } = SchoolSettingsDefaults.IsFinanceEnabled;

    public bool IsInternatEnabled { get; set; } = SchoolSettingsDefaults.IsInternatEnabled;

    public bool IsCoranModuleEnabled { get; set; } = SchoolSettingsDefaults.IsCoranModuleEnabled;

    /// <summary>
    /// Classification d'établissement (module Coran/Franco-Arabe, spec §3.2) — PUREMENT
    /// INFORMATIF. N'active rien : <see cref="IsCoranModuleEnabled"/> reste le seul interrupteur
    /// consommé par ModuleAuthorizationHandler. Défaut <see cref="Enums.SchoolType.Standard"/>,
    /// comme <see cref="TypeEtablissement"/> reste à <see cref="Enums.TypeEtablissement.Prive"/>
    /// pour toute école existante.
    /// </summary>
    public SchoolType SchoolType { get; set; } = SchoolSettingsDefaults.SchoolType;

    /// <summary>
    /// Profil choisi à l'Onboarding (voir <see cref="Enums.ProfileEtablissement"/> pour la distinction
    /// avec <see cref="TypeEtablissement"/> et <see cref="SchoolType"/>). PAS de valeur par défaut ici,
    /// à dessein : NULL est un état métier signifiant (« Onboarding non fait »), pas une case oubliée —
    /// c'est exactement ce que lit la garde cliente pour rediriger vers /onboarding. Les écoles
    /// antérieures à ce réglage reçoivent <see cref="Enums.ProfileEtablissement.General"/> par une
    /// UPDATE explicite de la migration (jamais par CE champ), pour ne jamais les rediriger à tort.
    /// </summary>
    public ProfileEtablissement? ProfileEtablissement { get; set; }

    /// <summary>
    /// Fenêtre de correction des notes par l'ENSEIGNANT, en jours : passé ce délai après la saisie
    /// (<c>Grade.CreatedAt</c>), il ne peut plus corriger sa note — seuls le Directeur et le Secrétariat
    /// le peuvent, sans limite de délai. Lue par GradeCorrectionAuthorizer ; réglée par le Directeur.
    /// </summary>
    public int GradeEditWindowDays { get; set; } = SchoolSettingsDefaults.GradeEditWindowDays;

    /// <summary>
    /// Découpage de l'année en périodes d'évaluation. N'agit que sur les années CRÉÉES ensuite ou
    /// explicitement rejouées (ApplyEvaluationPeriodsCommand) : jamais sur une année déjà notée.
    /// </summary>
    public EvaluationPeriodType EvaluationPeriodType { get; set; } = SchoolSettingsDefaults.EvaluationPeriodType;

    /// <summary>Nombre de périodes quand <see cref="EvaluationPeriodType"/> vaut Custom (2 à 6) ; ignoré sinon.</summary>
    public int CustomPeriodCount { get; set; } = SchoolSettingsDefaults.CustomPeriodCount;

    /// <summary>
    /// Jours OUVRÉS de l'établissement (Évolution N°3), noms de <see cref="DayOfWeek"/> séparés par des
    /// virgules, forme canonique lundi → dimanche (SchoolWeek.Serialize). Les autres jours sont des jours
    /// de repos : ni appel, ni pointage enseignant, ni créneau d'emploi du temps n'y sont saisis. Défaut
    /// lundi → samedi : la grille historique, donc aucune école existante ne perd de jour.
    /// </summary>
    public string WorkingDays { get; set; } = SchoolSettingsDefaults.WorkingDays;

    /// <summary>
    /// Cycles que l'établissement GÈRE, noms de <see cref="Enums.CycleType"/> séparés par des virgules, forme
    /// canonique Maternelle → Lycée (ManagedCycleSet.Serialize). Pilote ce que proposent les écrans de création
    /// de classe, d'inscription et d'examens — CONFORT d'affichage : le serveur ne refuse pas la création d'une
    /// classe hors de ces cycles. Il n'y a pas de cycle « Crèche » : Crèche et Maternelle sont deux niveaux du
    /// cycle Maternelle.
    ///
    /// Défaut : TOUS les cycles, donc aucune école ne perd d'écran par défaut. Seules les écoles déjà passées
    /// par le profil Élémentaire sont reprises sur « Maternelle,Primaire » (migration AddManagedCyclesToSchoolSettings),
    /// parce que c'est ce que le profil leur imposait déjà. L'Onboarding l'initialise (SelectProfileCommand) ;
    /// un changement de profil ultérieur ne le modifie JAMAIS — seul le Directeur le règle ensuite
    /// (SetManagedCyclesCommand), et il ne peut pas retirer un cycle qui contient encore des classes.
    /// </summary>
    public string ManagedCycles { get; set; } = SchoolSettingsDefaults.ManagedCycles;

    // ── Conseil de classe (Évolution N°7) : seuils sur la RÉFÉRENCE /20, transposés au barème du bulletin ──

    /// <summary>Moyenne générale minimale des Félicitations.</summary>
    public decimal CouncilFelicitationsMin { get; set; } = SchoolSettingsDefaults.CouncilFelicitationsMin;

    /// <summary>Moyenne générale minimale du Tableau d'Honneur — à condition de n'avoir AUCUNE note éliminatoire.</summary>
    public decimal CouncilHonorRollMin { get; set; } = SchoolSettingsDefaults.CouncilHonorRollMin;

    /// <summary>Moyenne générale minimale des Encouragements.</summary>
    public decimal CouncilEncouragementsMin { get; set; } = SchoolSettingsDefaults.CouncilEncouragementsMin;

    /// <summary>Une moyenne de matière STRICTEMENT inférieure à ce seuil est éliminatoire pour le Tableau d'Honneur.</summary>
    public decimal CouncilEliminatoryGrade { get; set; } = SchoolSettingsDefaults.CouncilEliminatoryGrade;

    /// <summary>Moyenne annuelle minimale proposée « Admis en classe supérieure ».</summary>
    public decimal CouncilPromotionMin { get; set; } = SchoolSettingsDefaults.CouncilPromotionMin;

    /// <summary>Moyenne annuelle minimale proposée « Autorisé à redoubler » ; en dessous : « Exclus ».</summary>
    public decimal CouncilRepeatMin { get; set; } = SchoolSettingsDefaults.CouncilRepeatMin;
}

/// <summary>
/// Valeurs par défaut d'un établissement neuf (critère du ticket JGK-B02 : « les valeurs par défaut
/// sont appliquées à la création »). Elles sont AUSSI la source de vérité de la fonction
/// provision_school_director : si l'une change ici, changer la migration correspondante.
/// </summary>
public static class SchoolSettingsDefaults
{
    public const int GradingScale = 20;
    public const string StudentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}";
    public const string TeacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}";
    public const int AutoLogoutMinutes = 10;
    public const string DateFormat = "dd/MM/yyyy";

    /// <summary>9 tranches — la convention la plus répandue au Sénégal (rentrée d'octobre, fin en juin).</summary>
    public const int TuitionMonthsPerYear = 9;

    /// <summary>Délégation au Secrétariat désactivée tant que le Directeur ne l'a pas explicitement activée.</summary>
    public const bool AllowSecretaryToManageGrading = false;

    /// <summary>Délégation à la Finance désactivée tant que le Directeur ne l'a pas explicitement activée.</summary>
    public const bool AllowFinanceToModifyFees = false;

    /// <summary>Délégation à la Finance désactivée tant que le Directeur ne l'a pas explicitement activée.</summary>
    public const bool AllowFinanceToDeleteFees = false;

    /// <summary>Annuler un versement est le geste quotidien du caissier : ouvert par défaut, retirable par le Directeur.</summary>
    public const bool AllowFinanceToCancelPayments = true;

    /// <summary>Corriger sa propre erreur de paie est un geste courant de la Finance : ouvert par défaut, retirable par le Directeur.</summary>
    public const bool AllowFinanceToCancelPayslips = true;

    public static readonly int[] AllowedGradingScales = [10, 20];
    public static readonly string[] AllowedDateFormats = ["dd/MM/yyyy", "dd MMMM yyyy"];

    /// <summary>Bornes du nombre de mensualités : au moins 1 mois, au plus l'année civile complète.</summary>
    public const int MinTuitionMonths = 1;
    public const int MaxTuitionMonths = 12;

    /// <summary>Type d'établissement par défaut : Privé, pour garantir la rétrocompatibilité des écoles existantes.</summary>
    public const TypeEtablissement TypeEtablissement = Enums.TypeEtablissement.Prive;

    /// <summary>
    /// Classification par défaut : Standard. Franco-Arabe et Daara restent un choix explicite du
    /// Directeur, jamais déduit — même philosophie que TypeEtablissement.
    /// </summary>
    public const SchoolType SchoolType = Enums.SchoolType.Standard;

    /// <summary>
    /// Aucun crédit à l'ouverture : les SMS s'achètent. Un solde initial offert serait une décision
    /// commerciale, pas une valeur par défaut technique — le Super Admin crédite explicitement.
    /// </summary>
    public const int SmsCreditBalance = 0;

    /// <summary>
    /// Alertes SMS désactivées tant que le Directeur ne les a pas explicitement activées — même
    /// principe que les délégations ci-dessus. Écrire aux parents de toute une école ne doit jamais
    /// être la conséquence silencieuse d'une montée de version.
    /// </summary>
    public const bool SmsAlertsEnabled = false;

    /// <summary>Une semaine de retard avant qu'un débiteur n'entre dans un lot de relance brouillon.</summary>
    public const int DebtorReminderThresholdDays = 7;

    /// <summary>Bornes du seuil de retard : au moins 1 jour, au plus une année scolaire complète.</summary>
    public const int MinDebtorReminderThresholdDays = 1;
    public const int MaxDebtorReminderThresholdDays = 365;

    /// <summary>Une semaine pour qu'un Enseignant corrige sa propre saisie, puis la note est verrouillée pour lui.</summary>
    public const int GradeEditWindowDays = 7;

    /// <summary>Bornes de la fenêtre : au moins 1 jour, au plus une année scolaire complète.</summary>
    public const int MinGradeEditWindowDays = 1;
    public const int MaxGradeEditWindowDays = 365;

    /// <summary>Trimestriel : le système sénégalais standard, et le comportement de toutes les écoles existantes.</summary>
    public const EvaluationPeriodType EvaluationPeriodType = Enums.EvaluationPeriodType.Trimester;

    public const int CustomPeriodCount = 3;

    /// <summary>Lundi → Samedi : la grille historique (à garder identique à SchoolWeek.DefaultStored).</summary>
    public const string WorkingDays = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday";

    /// <summary>Tous les cycles : aucune école existante ne perd d'écran (à garder identique à ManagedCycleSet.AllStored).</summary>
    public const string ManagedCycles = "Maternelle,Primaire,College,Lycee";

    /// <summary>
    /// Modules activés à la création d'une école : Pédagogie et Finance forment le socle métier
    /// existant, elles restent actives tant que le Directeur ne les désactive pas explicitement.
    /// </summary>
    public const bool IsPedagogyEnabled = true;

    public const bool IsFinanceEnabled = true;

    /// <summary>
    /// Internat et filière Coranique/Franco-Arabe : aucun écran ni route ne dépend encore de ces deux
    /// réglages (Internat/Coran n'existent pas dans le produit). Désactivés par défaut — les activer
    /// aujourd'hui n'ouvrirait rien, c'est un réglage anticipé pour le jour où ces modules existeront.
    /// </summary>
    public const bool IsInternatEnabled = false;

    public const bool IsCoranModuleEnabled = false;

    // Conseil de classe (Évolution N°7) — règles de la spécification MEN transmise le 25/09/2026.
    public const decimal CouncilFelicitationsMin = 14m;
    public const decimal CouncilHonorRollMin = 12m;
    public const decimal CouncilEncouragementsMin = 12m;
    public const decimal CouncilEliminatoryGrade = 5m;
    public const decimal CouncilPromotionMin = 10m;
    public const decimal CouncilRepeatMin = 8.5m;
}
````


#### Configurations Fluent API (index, contraintes, FK composites)

#### `src/SamaEcole.Persistence/Configurations/FeeCategoryConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFees (AGENTS.md règle #2).
/// </summary>
public class FeeCategoryConfiguration : IEntityTypeConfiguration<FeeCategory>
{
    public void Configure(EntityTypeBuilder<FeeCategory> builder)
    {
        builder.ToTable("fee_categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(60);
        builder.Property(c => c.IsRecurring).IsRequired();
        builder.Property(c => c.IsBoardingFee).IsRequired().HasDefaultValue(false);
        // Défaut false : toute catégorie existante reste OBLIGATOIRE après la migration.
        builder.Property(c => c.IsOptional).IsRequired().HasDefaultValue(false);

        // Une catégorie est unique par nom au sein de l'école. L'unicité est un
        // index PARTIEL sur les lignes vivantes : une catégorie archivée n'interdit jamais d'en recréer une de même
        // nom, même supprimée plusieurs fois (IsDeleted dans la clé n'autorisait qu'UNE ligne supprimée par nom).
        builder.HasIndex(c => new { c.SchoolId, c.Name }).IsUnique().HasFilter("\"IsDeleted\" = false");

        // Clé alternative (SchoolId, Id) : elle sert de cible à la FK COMPOSITE de class_fees, qui
        // référence une catégorie ET son école à la fois. Sans elle, une ligne de barème pourrait
        // pointer une catégorie d'une AUTRE école (la RLS masque, mais n'empêche pas d'exister).
        builder.HasAlternateKey(c => new { c.SchoolId, c.Id });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/ClassFeeConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFees (AGENTS.md règle #2).
///
/// Le verrou optimiste exploite la colonne système xmin de PostgreSQL (AGENTS.md règle #5,
/// docs/Volume_3_DDS.md §1.3). On déclare une propriété FANTÔME uint « xmin » marquée IsRowVersion
/// (jeton de concurrence + valeur régénérée à chaque écriture) : la convention Npgsql reconnaît ce
/// motif et la mappe sur la colonne système xmin — sans jamais générer d'opération de migration pour
/// elle, puisque la colonne existe déjà dans toute table PostgreSQL.
/// </summary>
public class ClassFeeConfiguration : IEntityTypeConfiguration<ClassFee>
{
    public void Configure(EntityTypeBuilder<ClassFee> builder)
    {
        builder.ToTable("class_fees");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        // numeric(12,2) : jusqu'à 9 999 999 999,99 F — largement de quoi couvrir une scolarité
        // annuelle. Le type fixe évite les dérives d'arrondi d'un float sur des montants d'argent.
        builder.Property(f => f.Amount).IsRequired().HasPrecision(12, 2);

        // Une seule ligne de barème par (catégorie, classe) dans une école. Le soft delete fait
        // partie de la clé, comme partout ailleurs.
        builder.HasIndex(f => new { f.SchoolId, f.FeeCategoryId, f.ClassroomId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(f => new { f.SchoolId, f.FeeCategoryId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(f => f.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …), et non de simples FK sur l'identifiant : sur la seule colonne,
        // rien n'empêcherait une ligne de barème de pointer la classe ou la catégorie d'une AUTRE
        // école. En incluant SchoolId des deux côtés, PostgreSQL rend le croisement de tenants
        // structurellement impossible — la même défense que pour students → classrooms (JGK-C02).
        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(f => new { f.SchoolId, f.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FeeCategory>()
            .WithMany()
            .HasForeignKey(f => new { f.SchoolId, f.FeeCategoryId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/FeeChangeHistoryConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal append-only des modifications de barème (ticket JGK-F01).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// L'interdiction d'UPDATE/DELETE est imposée par la migration AddFees (GRANT SELECT, INSERT seul).
/// </summary>
public class FeeChangeHistoryConfiguration : IEntityTypeConfiguration<FeeChangeHistory>
{
    public void Configure(EntityTypeBuilder<FeeChangeHistory> builder)
    {
        builder.ToTable("fee_change_history");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.SchoolId).IsRequired();
        builder.Property(h => h.ClassFeeId).IsRequired();
        builder.Property(h => h.OldAmount).HasPrecision(12, 2);
        builder.Property(h => h.NewAmount).IsRequired().HasPrecision(12, 2);
        builder.Property(h => h.ChangedByUserId).IsRequired();
        builder.Property(h => h.ChangedAt).IsRequired();

        // Chemin d'accès principal : « l'historique de CETTE ligne de barème, du plus récent au plus
        // ancien ».
        builder.HasIndex(h => new { h.ClassFeeId, h.ChangedAt });

        builder.HasOne<ClassFee>()
            .WithMany()
            .HasForeignKey(h => h.ClassFeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/EnrollmentConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddEnrollments, qui inscrit aussi
/// `enrollments` dans les tables protégées (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
///
/// Le verrou optimiste exploite la colonne système xmin (AGENTS.md règle #5) : TotalDue est une
/// donnée financière, deux corrections concurrentes ne doivent jamais s'écraser en silence.
/// </summary>
public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        // Classe redoublée (feature F) : défaut false en base pour que toute inscription existante
        // (créée avant cette colonne) soit non-redoublante, jamais NULL.
        builder.Property(e => e.IsRepeating).IsRequired().HasDefaultValue(false);
        // Cartographie IEF (Évolution N°7) : défaut false en base, une inscription existante reste « Nouveau ».
        builder.Property(e => e.IsTransferredIn).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.PreviousSchoolName).HasMaxLength(150);
        // Colonnes héritées Internat (lot F1) : mappées en propriétés fantômes — le schéma physique est inchangé
        // jusqu'à la migration DropLegacyBoardingColumns (lot F2). Aucun code métier ne les lit ni ne les écrit.
        builder.Property<string>("BoardingStatus")
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("Externe");
        builder.Property<Guid?>("RoomId");
        builder.Property(e => e.TotalDue).IsRequired().HasPrecision(12, 2);
        // Cumul des encaissements (JGK-F02) : par défaut 0, incrémenté sous le verrou xmin de cette table.
        builder.Property(e => e.AmountPaid).IsRequired().HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(e => e.ReceiptNumber).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EnrolledAt).IsRequired();

        // Le numéro de reçu est officiel (JGK-E02) : unique par établissement, jamais réémis. L'index
        // (non filtré) vaut même pour une inscription annulée — un numéro consommé reste consommé.
        builder.HasIndex(e => new { e.SchoolId, e.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("UX_enrollments_receipt_number");

        // « Un élève n'a qu'une inscription active par année scolaire » (DDS §5.4), tenu par la BASE.
        // Index PARTIEL excluant les inscriptions annulées et supprimées : un élève dont l'inscription
        // a été annulée peut être réinscrit, mais jamais inscrit deux fois en même temps sur une année.
        builder.HasIndex(e => new { e.SchoolId, e.StudentId, e.SchoolYearId })
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\" AND \"Status\" <> 'Cancelled'")
            .HasDatabaseName("UX_enrollments_single_active_per_year");

        builder.HasIndex(e => new { e.SchoolId, e.SchoolYearId });
        builder.HasIndex(e => new { e.SchoolId, e.ClassroomId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …) : sur la seule colonne d'identité, rien n'empêcherait une
        // inscription de pointer l'élève, la classe ou l'année d'une AUTRE école. En incluant SchoolId
        // des deux côtés, PostgreSQL rend le croisement de tenants structurellement impossible — la
        // même défense que students → classrooms (JGK-C02).
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, RoomId) → Room, même défense anti cross-tenant que Student/Classroom/
        // SchoolYear ci-dessus. Nullable : conservée pour alignement avec le schéma existant (lot F2).
        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey("SchoolId", "RoomId")
            .HasPrincipalKey(r => new { r.SchoolId, r.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex("SchoolId", "RoomId");
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/EnrollmentFeeLineConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddEnrollments (AGENTS.md règle #2).
///
/// Instantané figé des frais : écrit une seule fois dans la transaction d'inscription, jamais réédité.
/// Pas de verrou optimiste, donc — il n'y a jamais deux écritures concurrentes sur la même ligne.
/// </summary>
public class EnrollmentFeeLineConfiguration : IEntityTypeConfiguration<EnrollmentFeeLine>
{
    public void Configure(EntityTypeBuilder<EnrollmentFeeLine> builder)
    {
        builder.ToTable("enrollment_fee_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.SchoolId).IsRequired();
        builder.Property(l => l.Designation).IsRequired().HasMaxLength(60);
        builder.Property(l => l.IsRecurring).IsRequired();
        builder.Property(l => l.UnitAmount).IsRequired().HasPrecision(12, 2);
        builder.Property(l => l.Months).IsRequired();
        builder.Property(l => l.LineTotal).IsRequired().HasPrecision(12, 2);

        // Ventilation de l'encaissement d'inscription. Défaut 0 : les lignes déjà en base (inscriptions
        // antérieures à cette évolution) valent « rien d'encaissé à l'inscription », ce qui est exact —
        // leurs versements sont passés par la Caisse.
        builder.Property(l => l.AmountCollected).IsRequired().HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(l => l.MonthsCollected).IsRequired().HasDefaultValue(0);

        builder.HasIndex(l => new { l.SchoolId, l.EnrollmentId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(l => l.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …), même défense anti-croisement de tenants que partout ailleurs.
        // La ligne appartient à une inscription ET à une catégorie de la MÊME école.
        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FeeCategory>()
            .WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.FeeCategoryId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/PaymentConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Ticket JGK-F02 — encaissements (table `payments`). Le Global Query Filter (SchoolId + IsDeleted) est
/// posé automatiquement par ApplicationDbContext pour toute ITenantEntity ; la policy RLS PostgreSQL
/// équivalente est posée par la migration AddPayments, qui inscrit aussi `payments` dans les tables
/// protégées (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
///
/// Le paiement ne porte PAS de verrou optimiste : le solde vit sur l'inscription, et c'est le xmin
/// d'`enrollments` (via AmountPaid) qui sérialise deux encaissements concurrents (règle #5).
///
/// FK COMPOSITE (SchoolId, EnrollmentId) : sur la seule colonne d'inscription, rien n'empêcherait un
/// paiement de viser l'inscription d'une AUTRE école. En incluant SchoolId des deux côtés, le croisement
/// de tenants devient structurellement impossible — même défense que enrollments → students (JGK-E01).
/// </summary>
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();

        builder.Property(p => p.Amount).IsRequired().HasPrecision(12, 2);
        builder.Property(p => p.BalanceAfter).IsRequired().HasPrecision(12, 2);
        builder.Property(p => p.VatRate).HasPrecision(5, 4);
        builder.Property(p => p.VatAmount).IsRequired().HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.ReferencePeriod).HasMaxLength(20);
        builder.Property(p => p.ReceiptNumber).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ReceivedByUserId).IsRequired();
        builder.Property(p => p.PaidAt).IsRequired();
        builder.Property(p => p.CancellationReason).HasMaxLength(500);
        builder.Property(p => p.CancellationOutcome).HasConversion<string>().HasMaxLength(30);

        // Retrouver les annulations comptabilisées dans une session de caisse (clôture, état de la caisse en cours).
        builder.HasIndex(p => new { p.SchoolId, p.CancellationSessionId })
            .HasFilter("\"CancellationSessionId\" IS NOT NULL")
            .HasDatabaseName("IX_payments_SchoolId_CancellationSessionId");

        // Reconstituer les états par date : les annulations d'une période, quel que soit le jour de l'encaissement.
        builder.HasIndex(p => new { p.SchoolId, p.CancelledAt })
            .HasFilter("\"CancelledAt\" IS NOT NULL")
            .HasDatabaseName("IX_payments_SchoolId_CancelledAt");

        // Le montant versé est strictement positif : garde-fou en base, en plus de la validation applicative.
        builder.ToTable(t => t.HasCheckConstraint("CK_payments_amount_positive", "\"Amount\" > 0"));

        // Un taux de TVA (fraction) ne peut être négatif ni dépasser 100 % — garde-fou en base, en plus
        // de la validation applicative (RecordPaymentCommandValidator).
        builder.ToTable(t => t.HasCheckConstraint("CK_payments_vat_rate_range", "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)"));

        // Numéro de reçu officiel : unique par établissement, jamais réémis (même registre que le reçu
        // d'inscription, JGK-E02). L'index n'est pas filtré : un numéro consommé reste consommé.
        builder.HasIndex(p => new { p.SchoolId, p.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("UX_payments_receipt_number");

        // Idempotence côté client (ticket JGK-L01) : index unique PARTIEL, comme InventoryItem.Code —
        // la clé est absente sur tout paiement antérieur à ce ticket, et plusieurs paiements sans clé
        // doivent pouvoir coexister.
        builder.HasIndex(p => new { p.SchoolId, p.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("UX_payments_idempotency_key");

        // Lister les versements d'une inscription (relevé de compte, futur écran caisse).
        builder.HasIndex(p => new { p.SchoolId, p.EnrollmentId })
            .HasDatabaseName("IX_payments_SchoolId_EnrollmentId");

        // Dashboard Finance : 3 SumAsync par plage de date (jour/mois/année), toujours filtrés
        // Status != Cancelled — sans cet index, ces requêtes scannent toute la table par école.
        builder.HasIndex(p => new { p.SchoolId, p.Status, p.PaidAt })
            .HasDatabaseName("IX_payments_SchoolId_Status_PaidAt");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Enrollment)
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CashierSession)
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.CashierSessionId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // ReceivedByUserId reste une référence AUDIT (l'utilisateur qui a encaissé), toujours renseignée
        // depuis le JWT. Volontairement sans FK dure vers `users` : la valeur est de confiance (serveur),
        // et on évite l'interaction FK × RLS lors de l'INSERT sous le rôle applicatif NOBYPASSRLS.
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/PaymentBreakdownConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class PaymentBreakdownConfiguration : IEntityTypeConfiguration<PaymentBreakdown>
{
    public void Configure(EntityTypeBuilder<PaymentBreakdown> builder)
    {
        builder.ToTable("payment_breakdowns");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.AmountAllocated).IsRequired().HasPrecision(12, 2);

        // 60 caractères : la colonne « Période / Note » du reçu A5 fait 32 % d'une demi-largeur utile,
        // au-delà le libellé se replierait et doublerait la hauteur de ligne. Facultatif par conception.
        builder.Property(b => b.Label).HasMaxLength(60);

        builder.HasOne(b => b.Payment)
            .WithMany(p => p.Breakdowns)
            .HasForeignKey(b => b.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.FeeCategory)
            .WithMany()
            .HasForeignKey(b => b.FeeCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/CashierSessionConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class CashierSessionConfiguration : IEntityTypeConfiguration<CashierSession>
{
    public void Configure(EntityTypeBuilder<CashierSession> builder)
    {
        builder.ToTable("cashier_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();

        builder.Property(s => s.CashierId).IsRequired();
        builder.Property(s => s.OpenedAt).IsRequired();
        builder.Property(s => s.OpeningBalance).IsRequired().HasPrecision(12, 2);
        builder.Property(s => s.ClosingBalance).HasPrecision(12, 2);
        builder.Property(s => s.ExpectedCashAmount).HasPrecision(12, 2);
        builder.Property(s => s.ActualCashAmount).HasPrecision(12, 2);
        builder.Property(s => s.DiscrepancyAmount).HasPrecision(12, 2);
        builder.Property(s => s.DiscrepancyReason).HasMaxLength(500);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // One cashier can only have one open session at a time
        builder.HasIndex(s => new { s.SchoolId, s.CashierId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Open'")
            .HasDatabaseName("UX_cashier_sessions_SchoolId_CashierId_Open");

        builder.HasOne(s => s.Cashier)
            .WithMany()
            .HasForeignKey(s => s.CashierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/DisbursementConfiguration.cs`

````csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class DisbursementConfiguration : IEntityTypeConfiguration<Disbursement>
{
    public void Configure(EntityTypeBuilder<Disbursement> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Reason)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(d => d.Category)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(d => d.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(d => d.VatRate)
            .HasColumnType("decimal(5,4)");

        builder.Property(d => d.VatAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        // Un taux de TVA (fraction) ne peut être négatif ni dépasser 100 % — garde-fou en base, en plus
        // de la validation applicative.
        builder.ToTable(t => t.HasCheckConstraint("CK_disbursements_vat_rate_range", "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)"));

        builder.Property(d => d.PaymentMethod)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(d => d.Beneficiary)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(d => d.ReceiptUrl)
            .HasMaxLength(500);

        builder.HasIndex(d => new { d.SchoolId, d.Date });

        // Contre-écriture : au plus UNE par décaissement d'origine (une double annulation, même concurrente,
        // est refusée par la base), toujours en montant négatif, et l'origine ne peut jamais être effacée.
        builder.HasIndex(d => d.ReversalOfId)
            .IsUnique()
            .HasFilter("\"ReversalOfId\" IS NOT NULL");

        builder.HasOne<Disbursement>()
            .WithMany()
            .HasForeignKey(d => d.ReversalOfId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_disbursements_reversal_negative", "\"ReversalOfId\" IS NULL OR \"Amount\" < 0"));
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/FeeInstallmentPlanConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFeeInstallmentPlans (règle #2).
/// </summary>
public class FeeInstallmentPlanConfiguration : IEntityTypeConfiguration<FeeInstallmentPlan>
{
    public void Configure(EntityTypeBuilder<FeeInstallmentPlan> builder)
    {
        builder.ToTable("fee_installment_plans");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Reason).HasMaxLength(500);
        builder.Property(p => p.CreatedFromClassroomTemplate).IsRequired();

        // UN SEUL plan Active par inscription : un nouvel accord bascule l'ancien à Cancelled plutôt
        // que de l'écraser (même parti pris que EnrollmentConfiguration pour l'inscription active par
        // année). Index PARTIEL, filtré sur le statut : les plans annulés d'une même inscription
        // coexistent librement, seul l'état "actif" est contraint à l'unicité.
        builder.HasIndex(p => new { p.SchoolId, p.EnrollmentId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'")
            .HasDatabaseName("UX_fee_installment_plans_single_active_per_enrollment");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Relation vers FeeInstallment entièrement configurée côté FeeInstallmentConfiguration (FK
        // composite SchoolId+FeeInstallmentPlanId) : la déclarer aussi ici dupliquerait la même
        // relation avec deux configurations différentes.
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/FeeInstallmentConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFeeInstallmentPlans (règle #2).
/// </summary>
public class FeeInstallmentConfiguration : IEntityTypeConfiguration<FeeInstallment>
{
    public void Configure(EntityTypeBuilder<FeeInstallment> builder)
    {
        builder.ToTable("fee_installments");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();
        builder.Property(i => i.SequenceNo).IsRequired();
        builder.Property(i => i.Label).IsRequired().HasMaxLength(120);
        builder.Property(i => i.Amount).IsRequired().HasPrecision(12, 2);

        builder.HasIndex(i => new { i.SchoolId, i.FeeInstallmentPlanId, i.SequenceNo }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(i => i.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, FeeInstallmentPlanId) : même défense anti-croisement de tenants que
        // EnrollmentFeeLine → Enrollment — une échéance ne peut jamais se rattacher au plan d'une
        // autre école, même par erreur applicative.
        builder.HasOne(i => i.FeeInstallmentPlan)
            .WithMany(p => p.Installments)
            .HasForeignKey(i => new { i.SchoolId, i.FeeInstallmentPlanId })
            .HasPrincipalKey(p => new { p.SchoolId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/DebtorReminderBatchConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddDebtorReminderBatches (règle #2).
/// </summary>
public class DebtorReminderBatchConfiguration : IEntityTypeConfiguration<DebtorReminderBatch>
{
    public void Configure(EntityTypeBuilder<DebtorReminderBatch> builder)
    {
        builder.ToTable("debtor_reminder_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.SchoolId).IsRequired();
        builder.Property(b => b.ThresholdDays).IsRequired();
        builder.Property(b => b.GeneratedAt).IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // L'écran des brouillons liste du plus récent au plus ancien, filtré par école et par statut
        // (Draft en priorité) : l'index suit exactement cette lecture.
        builder.HasIndex(b => new { b.SchoolId, b.Status, b.GeneratedAt });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(b => b.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(b => new { b.SchoolId, b.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/DebtorReminderBatchItemConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddDebtorReminderBatches (règle #2).
/// </summary>
public class DebtorReminderBatchItemConfiguration : IEntityTypeConfiguration<DebtorReminderBatchItem>
{
    public void Configure(EntityTypeBuilder<DebtorReminderBatchItem> builder)
    {
        builder.ToTable("debtor_reminder_batch_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();
        builder.Property(i => i.GuardianPhone).HasMaxLength(20);
        builder.Property(i => i.RemainingBalance).IsRequired().HasPrecision(12, 2);
        builder.Property(i => i.DaysOverdue).IsRequired();

        builder.HasIndex(i => new { i.SchoolId, i.DebtorReminderBatchId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(i => i.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.DebtorReminderBatch)
            .WithMany(b => b.Items)
            .HasForeignKey(i => new { i.SchoolId, i.DebtorReminderBatchId })
            .HasPrincipalKey(b => new { b.SchoolId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(i => new { i.SchoolId, i.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/EmployeeContractConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class EmployeeContractConfiguration : IEntityTypeConfiguration<EmployeeContract>
{
    public void Configure(EntityTypeBuilder<EmployeeContract> builder)
    {
        builder.ToTable("employee_contracts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();
        
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);

        builder.Property(e => e.PayoutMethod)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(SamaEcole.Domain.Enums.PayoutMethod.Cash);

        builder.Property(e => e.PayoutAccountReference).HasMaxLength(50);

        // Défaut vrai : les contrats existants gardent le calcul historique (avec retenues).
        builder.Property(e => e.ApplyStatutoryDeductions).IsRequired().HasDefaultValue(true);

        builder.Property(e => e.ExternalFullName).HasMaxLength(150);
        builder.Property(e => e.ExternalJobTitle).HasMaxLength(100);
        builder.Property(e => e.ExternalPhone).HasMaxLength(30);

        builder.HasIndex(e => e.SchoolId);

        // Filtre étendu à "EndDate IS NULL" (Volume 1 §14.1) : un contrat CLÔTURÉ ne doit plus bloquer
        // la création d'un nouveau contrat pour la même personne (reprise après un départ) — seul un
        // contrat encore ACTIF doit être unique par enseignant/utilisateur.
        builder.HasIndex(e => e.TeacherId).IsUnique().HasFilter("\"TeacherId\" IS NOT NULL AND \"EndDate\" IS NULL");
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL AND \"EndDate\" IS NULL");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Teacher)
            .WithMany()
            .HasForeignKey(e => e.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
            
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/EmployeeContractHistoryConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal append-only des changements de contrat (Volume 1 §14.1).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// </summary>
public class EmployeeContractHistoryConfiguration : IEntityTypeConfiguration<EmployeeContractHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeContractHistory> builder)
    {
        builder.ToTable("employee_contract_histories");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.SchoolId).IsRequired();
        builder.Property(h => h.EmployeeContractId).IsRequired();
        builder.Property(h => h.ChangeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Reason).IsRequired().HasMaxLength(500);
        builder.Property(h => h.ChangedByUserId).IsRequired();
        builder.Property(h => h.ChangedAt).IsRequired();

        // Chemin d'accès principal : « l'historique de CE contrat, du plus récent au plus ancien ».
        builder.HasIndex(h => new { h.EmployeeContractId, h.ChangedAt });

        builder.HasOne(h => h.EmployeeContract)
            .WithMany()
            .HasForeignKey(h => h.EmployeeContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/FichePaieConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class FichePaieConfiguration : IEntityTypeConfiguration<FichePaie>
{
    public void Configure(EntityTypeBuilder<FichePaie> builder)
    {
        builder.ToTable("fiche_paies");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.SchoolId).IsRequired();
        
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(f => f.SchoolId);
        // Une fiche ACTIVE par employé et par mois. Une fiche annulée (Volume 1 §14.5) reste en base pour
        // l'historique mais libère la période, ce qui permet de régénérer une fiche corrigée.
        builder.HasIndex(f => new { f.SchoolId, f.EmployeeContractId, f.Month, f.Year })
            .IsUnique()
            .HasFilter("\"CancelledAt\" IS NULL");

        builder.Property(f => f.CancellationReason).HasMaxLength(500);

        // Défaut vrai : les fiches déjà éditées ont toutes été calculées avec retenues.
        builder.Property(f => f.DeductionsApplied).IsRequired().HasDefaultValue(true);
        builder.Property(f => f.PaidWith).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(f => f.IsCancelled);
        builder.Ignore(f => f.IsPaid);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(f => f.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.EmployeeContract)
            .WithMany()
            .HasForeignKey(f => f.EmployeeContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/TaxeDeclarationConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class TaxeDeclarationConfiguration : IEntityTypeConfiguration<TaxeDeclaration>
{
    public void Configure(EntityTypeBuilder<TaxeDeclaration> builder)
    {
        builder.ToTable("taxe_declarations");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.SchoolId).IsRequired();
        
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(t => t.SchoolId);
        // Ensure one declaration per school per month
        builder.HasIndex(t => new { t.SchoolId, t.Month, t.Year }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(t => t.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
````


#### `src/SamaEcole.Persistence/Configurations/DocumentsModuleConfiguration.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

// Module Documents administratifs — même convention que SurveillantConfiguration : index composite
// (SchoolId, clé étrangère) pour les lectures tenant, longueurs bornées, précision monétaire (12,2)
// comme Enrollment.TotalDue. La policy RLS de chaque table est posée dans la migration
// AddDocumentsModuleEntities (AGENTS.md règle #2 : filtre EF + RLS, jamais un seul).

public class EarlyDepartureConfiguration : IEntityTypeConfiguration<EarlyDeparture>
{
    public void Configure(EntityTypeBuilder<EarlyDeparture> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.PickedUpBy).HasMaxLength(200);
        // Date pure (saisie "JJ/MM/AAAA"), pas un instant : mappée en "date" plutôt que le "timestamp
        // with time zone" par défaut de Npgsql pour DateTime, qui rejette tout DateTime dont le Kind
        // n'est pas explicitement Utc (le JSON entrant a Kind=Unspecified) — voir LateArrivalConfiguration.
        builder.Property(e => e.Date).HasColumnType("date");
    }
}

public class ParentSummonsConfiguration : IEntityTypeConfiguration<ParentSummons>
{
    public void Configure(EntityTypeBuilder<ParentSummons> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);

        // Suite donnée (02/09/2026). Statut en TEXTE, comme CashierSession et DebtorReminderBatch :
        // un registre de vie scolaire se relit en base des années plus tard, un entier n'y dit rien.
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.OutcomeNotes).HasMaxLength(2000);

        // Le tableau de bord de la Vie scolaire ne cherche que les convocations EN ATTENTE de suite.
        // Index partiel : les convocations closes s'accumulent année après année, les indexer toutes
        // ferait grossir l'index sans jamais servir cette lecture.
        builder.HasIndex(e => new { e.SchoolId, e.ScheduledAt })
            .HasFilter("\"Status\" = 'Scheduled'")
            .HasDatabaseName("IX_parent_summons_pending");
    }
}

public class FinancialCommitmentConfiguration : IEntityTypeConfiguration<FinancialCommitment>
{
    public void Configure(EntityTypeBuilder<FinancialCommitment> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.EnrollmentId });
        builder.Property(e => e.Amount).IsRequired().HasPrecision(12, 2);
        builder.Property(e => e.Terms).HasMaxLength(2000);
    }
}

public class TeacherHourRecordConfiguration : IEntityTypeConfiguration<TeacherHourRecord>
{
    public void Configure(EntityTypeBuilder<TeacherHourRecord> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.EmployeeContractId });
        builder.Property(e => e.Hours).IsRequired().HasPrecision(6, 2);
        builder.Property(e => e.Note).HasMaxLength(500);
    }
}
````


---

## 6. Flux fonctionnels détaillés

### 6.1 Barème des frais (`/frais`)
**Objectif :** définir ce que chaque classe doit payer.
1. **Catégories** (`CreateFeeCategory`, `UpdateFeeCategory` [uniquement `IsOptional`], `DeleteFeeCategory`, `RestoreFeeCategory`) : liste libre par école, nom unique parmi les vivantes. Une pension (`IsBoardingFee`) ne peut pas être facultative.
2. **Montant standard en un clic** (`ApplyStandardFee`) : un montant pour une catégorie, sur **tout l'établissement ou un niveau** (comparaison `lower()`), dans **une seule transaction**. Crée les lignes manquantes (historique avec ancien montant `NULL`), réaligne les existantes **seulement si** `OverwriteExisting` et montant différent, sinon les préserve (« exceptions »). Retourne `{created, updated, skipped}`. Aucune classe → erreur de validation explicite.
3. **Exception classe par classe** (`UpdateClassFee`) : le client renvoie `rowVersion` ; montant inchangé = aucune écriture ; sinon écriture + ligne d'historique ; le handler renvoie le **nouveau** `xmin` pour enchaîner sans recharger.
4. **Historique** (`GetFeeHistory`), **suppression d'une ligne** (`DeleteClassFee`, soft delete avec `rowVersion`), **analyse d'impact** (`GetFeeCategoryDeletionImpact`) et **rupture explicite des liens** (`UnlinkFeeCategoryDependencies`).
5. **Suppression d'une catégorie** : `FeeCategoryDeletionPolicy` bloque (texte imposé) si la catégorie figure dans une `EnrollmentFeeLine` ou dans une ventilation de paiement non annulé ; les lignes de barème sont des dépendances « possédées » qu'il faut archiver explicitement avant. La suppression ne **rompt jamais** un lien implicitement.
6. **Droits** : lecture pour tous les rôles de l'école ; création/modif = politique `CanModifyFees` ; suppression = `CanDeleteFees` (Directeur toujours ; Finance si délégué).


### 6.2 Du barème au montant dû (inscription)
`CreateEnrollmentCommandHandler` (module Inscriptions) est **la seule source du `TotalDue`**. Dans **la même transaction** il : génère le matricule et le **numéro de reçu d'inscription**, construit les `EnrollmentFeeLine` (barème de la classe, hors pension ; mensualités × `TuitionMonthsPerYear`, frais ponctuels × 1 ; frais facultatifs seulement s'ils sont cochés ; pension ajoutée par `BoardingFeeLineBuilder` si Interne/Demi-pensionnaire et `IncludeBoardingFee`), somme `TotalDue`, enregistre la part **encaissée le jour de l'inscription** par ligne (`AmountCollected`, `MonthsCollected`) pour imprimer le reçu d'inscription, et **initialise `AmountPaid`** avec ce premier versement. Le reçu d'inscription atteste **uniquement de ce qui est entré en caisse**, jamais du dû annuel.

> Ce handler touche aussi aux modules Élèves/Internat ; il est reproduit en entier car c'est le point d'entrée du dû. Si votre autre projet a déjà un module d'inscription, il suffit d'y reprendre : copie du barème en lignes figées, calcul du total, n° de reçu dans la transaction.


#### `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommandHandler.cs`

````csharp
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Extensions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Commands.CreateEnrollment;

/// <summary>
/// Cœur du ticket JGK-E01. Suit le patron de référence du module Students : dépendances minimales, le
/// tenant vient d'<see cref="ITenantProvider"/>, le matricule est généré dans la MÊME transaction que
/// l'insertion (AGENTS.md règle #3) — jamais avant. Toute l'opération (création éventuelle de l'élève,
/// génération du matricule, inscription, lignes de frais) est atomique : le moindre échec rembobine
/// tout, matricule compris — aucun trou de numérotation, aucun compte financier orphelin.
///
/// Le montant dû est CALCULÉ ici à partir du barème de la classe (JGK-F01), figé ligne à ligne, et
/// n'est jamais lu de la requête (règle #4 et #10).
///
/// L'inscription FIGE LA DETTE et rien d'autre : elle ne crée AUCUN <see cref="Payment"/>,
/// <c>AmountPaid</c> reste à 0. Le secrétariat n'encaisse aucun fonds (AGENTS.md règle #4,
/// docs/design-references/README.md §1) ; tout règlement, y compris le premier, se fait ensuite à la
/// Caisse (RecordPaymentCommand), sur la base du reçu d'inscription.
/// </summary>
public class CreateEnrollmentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMatriculeGenerator matriculeGenerator,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache,
    ICurrentUserService currentUser,
    StudentQuotaGuard quotaGuard,
    IBoardingAssignmentService boardingAssignments)
    : IRequestHandler<CreateEnrollmentCommand, EnrollmentReceiptDto>
{
    public async Task<EnrollmentReceiptDto> Handle(CreateEnrollmentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // L'inscription porte l'année ACTIVE (mission JGK-E01). Sans année active, l'école ne peut rien
        // rattacher : on refuse en 422 plutôt que de deviner une année.
        var activeYear = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(
                    "SchoolYear",
                    "Aucune année scolaire active. Activez une année scolaire avant d'inscrire un élève.")
            ]);

        // La classe doit exister DANS CETTE ÉCOLE. Le Global Query Filter restreint déjà au tenant : une
        // classe d'une autre école est introuvable ici, et le contrôle vaut appartenance autant
        // qu'existence (même raisonnement que CreateStudentCommandHandler).
        var classroom = await dbContext.Classrooms
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(
                    nameof(request.ClassroomId),
                    "La classe indiquée n'existe pas dans votre établissement.")
            ]);

        // Garde serveur (AGENTS.md règle sur les modules) : un client qui poste un régime non-Externe
        // alors que le Directeur n'a pas activé l'Internat est rejeté en 422 — jamais accepté puis
        // silencieusement ignoré (même philosophie que la garde déjà en place pour SchoolModule.Pedagogy).
        if (request.BoardingStatus != BoardingStatus.Externe)
        {
            var settings = await dbContext.SchoolSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
            var internatEnabled = settings?.IsInternatEnabled ?? SchoolSettingsDefaults.IsInternatEnabled;

            if (!internatEnabled)
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.BoardingStatus),
                        "Le module Internat n'est pas activé pour votre établissement.")
                ]);
            }
        }

        // Quota d'élèves : seule une NOUVELLE inscription crée un élève. Une réinscription réutilise un
        // élève déjà compté dans l'effectif — elle ne change rien au quota et n'est donc jamais refusée.
        var quotaWarning = request.Type == EnrollmentType.NewEnrollment
            ? await quotaGuard.EnsureCanAddAsync(1, cancellationToken)
            : null;

        var receipt = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var (student, matricule) = request.Type == EnrollmentType.NewEnrollment
                ? await CreateStudentAsync(schoolId, request, ct)
                : await LoadStudentForReEnrollmentAsync(request, ct);

            // Un élève n'a qu'une inscription (non annulée) par année (DDS §5.4). L'index unique en base
            // en est la garantie dure ; ce pré-contrôle offre un message lisible plutôt qu'un 409 brut.
            var alreadyEnrolled = await dbContext.Enrollments.AnyAsync(
                e => e.StudentId == student.Id
                     && e.SchoolYearId == activeYear.Id
                     && e.Status != EnrollmentStatus.Cancelled,
                ct);

            if (alreadyEnrolled)
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.StudentId),
                        $"Cet élève est déjà inscrit pour l'année {activeYear.Label}.")
                ]);
            }

            var tuitionMonths = await ResolveTuitionMonthsAsync(ct);
            var lines = await BuildFeeLinesAsync(
                schoolId, request.ClassroomId, tuitionMonths, request.OptionalFeeCategoryIds, ct);

            // Pension (module Internat) : catégories IsBoardingFee, seulement pour Interne/Demi-
            // pensionnaire et seulement si IncludeBoardingFee — les élèves Externe de la même classe
            // ne voient jamais ces lignes (spec §5.1).
            if (request.BoardingStatus != BoardingStatus.Externe && request.IncludeBoardingFee)
            {
                var boardingLines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
                    dbContext, schoolId, request.ClassroomId, tuitionMonths, existingFeeCategoryIds: new HashSet<Guid>(), ct);
                lines.AddRange(boardingLines);
            }

            var totalDue = lines.Sum(l => l.LineTotal);

            // Numéro officiel du reçu (JGK-E02), attribué DANS la transaction comme le matricule : s'il y
            // a le moindre rollback ensuite, le compteur de reçus est rembobiné avec — aucun trou.
            var receiptNumber = await matriculeGenerator.GenerateNextReceiptNumberAsync(schoolId, ct);

            var enrollment = new Enrollment
            {
                SchoolId = schoolId,
                StudentId = student.Id,
                SchoolYearId = activeYear.Id,
                ClassroomId = request.ClassroomId,
                Type = request.Type,
                IsRepeating = request.IsRepeating,
                // Statut IEF (Évolution N°7) : l'établissement d'origine n'est retenu que pour un élève transféré.
                IsTransferredIn = request.IsTransferredIn,
                PreviousSchoolName = request.IsTransferredIn && !string.IsNullOrWhiteSpace(request.PreviousSchoolName)
                    ? request.PreviousSchoolName.Trim()
                    : null,
                Status = EnrollmentStatus.Confirmed,
                TotalDue = totalDue,
                // L'inscription n'encaisse rien : la dette est intégralement à régler à la Caisse.
                AmountPaid = 0m,
                ReceiptNumber = receiptNumber,
                EnrolledAt = timeProvider.GetUtcNow()
            };

            dbContext.Enrollments.Add(enrollment);

            // Hébergement (modèle Pavillon/Lit, lot C) : le séjour est créé dans la MÊME transaction que l'inscription, par la
            // règle d'affectation unique. L'ancien écran envoie une chambre, pas un lit : le plus petit lit libre est choisi.
            // Chambre pleine, lit en maintenance ou pavillon du mauvais genre → 422, et l'inscription entière est annulée.
            // Un demi-pensionnaire n'a pas de lit : la chambre éventuelle est ignorée.
            if (request.BoardingStatus != BoardingStatus.Externe)
            {
                await boardingAssignments.AssignAsync(
                    enrollment,
                    request.BoardingStatus == BoardingStatus.Interne ? BoardingRegime.Interne : BoardingRegime.DemiPensionnaire,
                    bedId: null, roomId: request.RoomId, stayRowVersion: null, requireStayRowVersion: false, ct);
            }

            foreach (var line in lines)
            {
                line.SchoolId = schoolId;
                line.EnrollmentId = enrollment.Id;
                dbContext.EnrollmentFeeLines.Add(line);
            }

            // Matières optionnelles (Évolution N°6) : dans la MÊME transaction que l'inscription — jamais un élève
            // inscrit sans ses options, ni des options sans inscription. Option invalide → 422, tout est annulé.
            await StudentOptionWriter.SetAsync(
                dbContext, schoolId, currentUser.UserId?.ToString() ?? "system", student.Id, request.ClassroomId,
                activeYear.Id, request.SubjectOptionIds, fillDefaults: true, ct, nameof(request.SubjectOptionIds));

            // Traduit la course sur le dernier lit en 409 BED_UNAVAILABLE (index unique du lot A), jamais en 500.
            await BoardingConflicts.SaveAsync(dbContext, ct);

            var school = await dbContext.Schools.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == schoolId, ct);

            return new EnrollmentReceiptDto(
                enrollment.Id,
                receiptNumber,
                school?.Name ?? string.Empty,
                school?.Address,
                school?.Phone,
                school?.Email,
                school?.Ninea,
                school?.RegistreCommerce,
                ReceiptCity.FromAddress(school?.Address),
                school?.LogoUrl,
                matricule,
                student.FullName,
                classroom.Name,
                classroom.Level,
                activeYear.Label,
                student.GuardianName,
                student.GuardianPhone,
                enrollment.Type.ToString(),
                enrollment.Status.ToString(),
                enrollment.EnrolledAt,
                lines.Select(l => new EnrollmentFeeLineDto(
                    l.Designation, l.IsRecurring, l.UnitAmount, l.Months, l.LineTotal)).ToList(),
                totalDue,
                // Rien n'est encaissé à l'inscription : ventilation vide, total à 0, aucun mode de
                // règlement. Ces champs ne sont peuplés que par la relecture d'un reçu historique
                // (GetEnrollmentReceiptQuery) et restent au DTO pour la stabilité de forme côté Finance.
                [],
                0m,
                null,
                classroom.IsAccelerated);
        }, cancellationToken);

        // Une nouvelle inscription change à la fois le dû/attendu financier et les effectifs du
        // dashboard Directeur — les deux caches doivent tomber, jamais un seul.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return receipt with { QuotaWarning = quotaWarning };
    }

    private async Task<(Student student, string matricule)> CreateStudentAsync(
        Guid schoolId, CreateEnrollmentCommand request, CancellationToken ct)
    {
        // Génération du matricule dans la transaction (règle #3), exactement comme JGK-D01.
        var matricule = await matriculeGenerator.GenerateNextStudentMatriculeAsync(schoolId, ct);

        var student = new Student
        {
            SchoolId = schoolId,
            Matricule = matricule,
            FullName = request.FullName!.ToTitleCase(),
            BirthDate = request.BirthDate!.Value,
            // Obligatoire (feature E) : garanti non vide par CreateEnrollmentCommandValidator sur la branche NewEnrollment.
            BirthPlace = request.BirthPlace!,
            Gender = request.Gender!,
            ClassroomId = request.ClassroomId,
            GuardianName = request.GuardianName.ToTitleCase(),
            GuardianPhone = request.GuardianPhone
        };

        dbContext.Students.Add(student);
        return (student, matricule);
    }

    private async Task<(Student student, string matricule)> LoadStudentForReEnrollmentAsync(
        CreateEnrollmentCommand request, CancellationToken ct)
    {
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, ct)
            ?? throw new ValidationException([
                new ValidationFailure(
                    nameof(request.StudentId),
                    "L'élève à réinscrire n'existe pas dans votre établissement.")
            ]);

        // La réinscription fait de la nouvelle classe la classe COURANTE de l'élève : sans cette mise à
        // jour, sa fiche continuerait d'afficher la classe de l'an dernier. Aucun nouveau matricule —
        // c'est le même élève (règle métier de la réinscription, Volume 1 §6.2).
        student.ClassroomId = request.ClassroomId;

        return (student, student.Matricule);
    }

    private async Task<int> ResolveTuitionMonthsAsync(CancellationToken ct)
    {
        var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var months = settings?.TuitionMonthsPerYear ?? SchoolSettingsDefaults.TuitionMonthsPerYear;

        // Filet : un réglage à 0 (jamais posé par la validation, mais possible via un import direct)
        // annulerait toute scolarité. On retombe alors sur le défaut plutôt que de facturer 0.
        return months < SchoolSettingsDefaults.MinTuitionMonths
            ? SchoolSettingsDefaults.TuitionMonthsPerYear
            : months;
    }

    /// <summary>
    /// Construit l'instantané des frais de la classe. Une mensualité (catégorie récurrente) est
    /// multipliée par le nombre de mensualités de l'année ; un frais ponctuel ne l'est jamais
    /// (règle métier portée par <see cref="FeeCategory.IsRecurring"/>). Les montants sont COPIÉS, pas
    /// référencés : le reçu reste fidèle même si le barème change ensuite (JGK-F01 l'historise).
    ///
    /// EXCLUT les catégories <see cref="FeeCategory.IsBoardingFee"/> (module Internat) : ces frais ne
    /// sont ajoutés que par <see cref="BoardingFeeLineBuilder"/>, conditionnés au régime d'hébergement
    /// et à <c>IncludeBoardingFee</c> — sinon un élève Externe de la même classe se verrait facturer la
    /// pension d'office dès lors que l'école aurait un tarif de pension sur cette classe (spec §5.1).
    ///
    /// Frais optionnels : une catégorie <see cref="FeeCategory.IsOptional"/> n'est facturée que si la famille
    /// l'a cochée (<paramref name="selectedOptionalFeeCategoryIds"/>), voir <see cref="OptionalFeeSelection"/> ;
    /// les frais obligatoires le sont toujours. Un identifiant coché qui n'est pas un frais optionnel de la
    /// classe est refusé en 422 — l'inscription entière est alors annulée (matricule compris).
    /// </summary>
    private async Task<List<EnrollmentFeeLine>> BuildFeeLinesAsync(
        Guid schoolId, Guid classroomId, int tuitionMonths,
        IReadOnlyList<Guid>? selectedOptionalFeeCategoryIds, CancellationToken ct)
    {
        var fees = await (
            from fee in dbContext.ClassFees
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            where fee.ClassroomId == classroomId && !category.IsBoardingFee
            orderby category.IsRecurring, category.Name
            select new { fee.FeeCategoryId, category.Name, category.IsRecurring, category.IsOptional, fee.Amount })
            .ToListAsync(ct);

        var invalid = OptionalFeeSelection.FindInvalid(
            selectedOptionalFeeCategoryIds,
            fees.Where(f => f.IsOptional).Select(f => f.FeeCategoryId).ToHashSet());

        if (invalid.Count > 0)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(CreateEnrollmentCommand.OptionalFeeCategoryIds),
                    "Un frais coché n'est pas un frais optionnel de cette classe.")
            ]);
        }

        return fees
            .Where(f => OptionalFeeSelection.IsBilled(f.IsOptional, f.FeeCategoryId, selectedOptionalFeeCategoryIds))
            .Select(f =>
            {
                var months = f.IsRecurring ? tuitionMonths : 1;
                return new EnrollmentFeeLine
                {
                    SchoolId = schoolId,
                    FeeCategoryId = f.FeeCategoryId,
                    Designation = f.Name,
                    IsRecurring = f.IsRecurring,
                    UnitAmount = f.Amount,
                    Months = months,
                    LineTotal = f.Amount * months
                };
            }).ToList();
    }
}
````


#### `src/SamaEcole.Application/Enrollments/OptionalFeeSelection.cs`

````csharp
namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Frais optionnels — décide quels frais d'une classe entrent dans le dû annuel d'une inscription, en
/// fonction des cases cochées (<c>CreateEnrollmentCommand.OptionalFeeCategoryIds</c>). Pure et sans base :
/// c'est elle que le Handler applique, et elle qui porte les trois invariants de la facture.
///
///   1. Un frais OBLIGATOIRE est toujours facturé, quoi que contienne la liste : le client ne peut pas
///      décocher l'inscription ou la mensualité (AGENTS.md règle #10 — le serveur décide du dû).
///   2. Un frais OPTIONNEL n'est facturé que s'il est dans la liste ; liste vide = aucun.
///   3. Liste ABSENTE (<c>null</c>) = comportement historique : tout est facturé. Un client antérieur à la
///      fonctionnalité ne change pas de facture ; c'est le formulaire qui envoie explicitement les choix.
/// </summary>
public static class OptionalFeeSelection
{
    public static bool IsBilled(bool isOptional, Guid feeCategoryId, IReadOnlyCollection<Guid>? selectedOptionalIds)
        => !isOptional || selectedOptionalIds is null || selectedOptionalIds.Contains(feeCategoryId);

    /// <summary>
    /// Identifiants de la liste qui ne sont PAS un frais optionnel de la classe : catégorie inconnue, d'une
    /// autre école, sans tarif sur cette classe — ou obligatoire. Le Handler les rejette en 422 plutôt que
    /// de les ignorer : « cocher » un frais obligatoire est une erreur ou un contournement, pas un no-op.
    /// </summary>
    public static IReadOnlyList<Guid> FindInvalid(
        IReadOnlyCollection<Guid>? selectedOptionalIds, IReadOnlySet<Guid> optionalCategoryIdsOfClass)
        => selectedOptionalIds is null
            ? []
            : selectedOptionalIds.Where(id => !optionalCategoryIdsOfClass.Contains(id)).Distinct().ToList();
}
````


#### `src/SamaEcole.Application/Enrollments/BoardingFeeLineBuilder.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Construit les lignes de frais d'internat MANQUANTES d'une inscription (module Internat) — même
/// calcul que CreateEnrollmentCommandHandler.BuildFeeLinesAsync (montants COPIÉS depuis ClassFee,
/// mensualité × tuitionMonths), restreint aux catégories FeeCategory.IsBoardingFee et EXCLUANT
/// celles déjà présentes sur l'inscription (<paramref name="existingFeeCategoryIds"/>) : c'est ce
/// filtre qui garantit qu'un second changement de lit (AssignBedCommand) ne
/// double-facture jamais la pension. Partagé par CreateEnrollmentCommandHandler (aucune ligne
/// existante — élève Externe/nouvel Interne) et l'affectation d'un lit (l'inscription
/// existe déjà, ses lignes actuelles bornent ce qui reste à ajouter).
/// </summary>
public static class BoardingFeeLineBuilder
{
    public static async Task<List<EnrollmentFeeLine>> BuildMissingBoardingLinesAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        Guid classroomId,
        int tuitionMonths,
        IReadOnlySet<Guid> existingFeeCategoryIds,
        CancellationToken cancellationToken)
    {
        var boardingFees = await (
            from fee in dbContext.ClassFees
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            where fee.ClassroomId == classroomId && category.IsBoardingFee
            select new { fee.FeeCategoryId, category.Name, category.IsRecurring, fee.Amount })
            .ToListAsync(cancellationToken);

        return boardingFees
            .Where(f => !existingFeeCategoryIds.Contains(f.FeeCategoryId))
            .Select(f =>
            {
                var months = f.IsRecurring ? tuitionMonths : 1;
                return new EnrollmentFeeLine
                {
                    SchoolId = schoolId,
                    FeeCategoryId = f.FeeCategoryId,
                    Designation = f.Name,
                    IsRecurring = f.IsRecurring,
                    UnitAmount = f.Amount,
                    Months = months,
                    LineTotal = f.Amount * months
                };
            })
            .ToList();
    }
}
````


#### `src/SamaEcole.Application/Enrollments/EnrollmentReceiptDto.cs`

````csharp
namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Charge utile du reçu d'inscription (ticket JGK-E01), renvoyée aussi bien à la création qu'à la
/// relecture GET /enrollments/{id}/receipt. Elle porte tout ce que la référence de design impose
/// (docs/design-references/README.md §1) : en-tête établissement (coordonnées + mentions légales
/// NINEA/RCCM), identité de l'élève et de son tuteur, le dû annuel figé et l'échéancier prospectif.
///
/// L'inscription N'ENCAISSE RIEN (le secrétariat n'enregistre aucun versement, AGENTS.md règle #4) :
///   * <paramref name="TotalDue"/> — le dû ANNUEL figé à l'inscription. USAGE INTERNE Finance : il
///     n'est plus imprimé sur aucune pièce remise au tuteur (refonte du 25/08/2026).
///   * <paramref name="TotalCollected"/> / <paramref name="CollectedLines"/> /
///     <paramref name="PaymentMethod"/> — TOUJOURS 0 / vide / null pour une inscription. Ces champs ne
///     sont peuplés que par la relecture d'un reçu historique dont un versement portait le même numéro
///     (GetEnrollmentReceiptQuery) ; ils restent au DTO pour la stabilité de forme côté Finance et
///     côté front. Tout règlement se fait à la Caisse (RecordPaymentCommand), qui a son propre reçu.
///
/// La mention obligatoire (AGENTS.md règle #12) n'est pas transportée ici : c'est un texte constant,
/// figé côté vue/PDF pour qu'aucun appelant ne puisse l'altérer ou l'omettre.
/// </summary>
public record EnrollmentReceiptDto(
    Guid EnrollmentId,
    string ReceiptNumber,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolCity,
    string? SchoolLogoUrl,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string ClassroomLevel,
    string SchoolYearLabel,
    string? GuardianName,
    string? GuardianPhone,
    string Type,
    string Status,
    DateTimeOffset EnrolledAt,
    IReadOnlyList<EnrollmentFeeLineDto> Lines,
    decimal TotalDue,

    // Ventilation de l'encaissement du jour — une ligne par frais réglé, vide si rien n'a été encaissé.
    IReadOnlyList<CollectedFeeLineDto> CollectedLines,
    decimal TotalCollected,

    // Mode de règlement du versement du jour ; null quand rien n'a été encaissé.
    string? PaymentMethod,

    // Classe PASSERELLE / ACCÉLÉRÉE (option) : la ligne « Classe d'affectation » du reçu porte alors la
    // mention du dispositif, l'année payée en couvrant deux niveaux. Faux pour une classe ordinaire —
    // le reçu est alors rigoureusement identique à ce qu'il a toujours été.
    bool IsAcceleratedClass = false,

    // Non nul quand cette NOUVELLE inscription fait dépasser le plafond nominal du quota d'élèves de la
    // souscription (tolérance entamée) : l'écran affiche un bandeau. Jamais rempli pour une réinscription.
    SamaEcole.Application.Subscriptions.StudentQuotaWarning? QuotaWarning = null)
{
    /// <summary>Reste dû sur l'année APRÈS le versement du jour. Jamais négatif : l'encaissement est borné au dû.</summary>
    public decimal RemainingBalance => TotalDue - TotalCollected;

    /// <summary>
    /// RÈGLE MÉTIER — engagement initial : ce que le tuteur doit régler à la comptabilité le jour de
    /// l'inscription. Pour chaque ligne, son <see cref="EnrollmentFeeLineDto.UnitAmount"/> : le frais
    /// ponctuel ENTIER (inscription, tenue) ou UNE SEULE mensualité pour une ligne récurrente.
    ///
    /// C'est le cœur de la refonte du 25/08/2026 : l'attestation n'affiche plus jamais
    /// <see cref="TotalDue"/> (le cumul annuel, « 271 000 »), qui effrayait les tuteurs sans les
    /// informer. Elle annonce ce qu'il y a à payer maintenant, puis le tarif mensuel qui suivra.
    ///
    /// HYPOTHÈSE ASSUMÉE : un mois d'avance. Une école qui en exigerait deux, ou qui ne réclamerait
    /// que les frais d'inscription à la signature, a besoin d'un réglage d'établissement — rien dans
    /// le modèle ne porte cette information aujourd'hui.
    /// </summary>
    public decimal InitialSettlementTotal => Lines.Sum(line => line.UnitAmount);

    /// <summary>
    /// Lignes de l'échéancier mensuel : les frais RÉCURRENTS seuls. Un frais ponctuel n'a pas
    /// d'échéance et le faire figurer dans un échéancier serait un faux.
    /// </summary>
    public IReadOnlyList<EnrollmentFeeLineDto> MonthlyLines =>
        Lines.Where(line => line.IsRecurring).ToList();

    /// <summary>
    /// Total à prévoir chaque mois — la somme des mensualités UNITAIRES. Jamais multiplié par le
    /// nombre de mois : c'est précisément la multiplication que la refonte supprime.
    /// </summary>
    public decimal MonthlyTotal => Lines.Where(line => line.IsRecurring).Sum(line => line.UnitAmount);
}

/// <summary>
/// Une ligne du tableau « Désignation des frais / Montant » du reçu. <paramref name="Months"/> vaut 1
/// pour un frais ponctuel et le nombre de mensualités pour une scolarité, ce qui permet à la vue
/// d'afficher « Mensualité (× 9) » sans recalculer quoi que ce soit.
/// </summary>
public record EnrollmentFeeLineDto(
    string Designation,
    bool IsRecurring,
    decimal UnitAmount,
    int Months,
    decimal LineTotal);

/// <summary>
/// Une ligne de la VENTILATION de l'encaissement : le frais réglé au guichet et son montant réel.
/// <paramref name="Months"/> porte le nombre de mois couverts pour une mensualité (« Mensualité
/// (× 1 mois) »), 1 pour un frais ponctuel.
/// </summary>
public record CollectedFeeLineDto(
    string Designation,
    bool IsRecurring,
    int Months,
    decimal Amount);

/// <summary>
/// Dérive la « ville » du bas de reçu (« Fait à … », référence de design §1.6) depuis l'adresse libre
/// de l'établissement. Le modèle ne stocke qu'une adresse (School.Address) ; par convention sénégalaise
/// elle se termine par la localité (« Rue 12, Médina, Dakar » → « Dakar »). Faute d'adresse, on renvoie
/// null et le reçu laisse la ligne à compléter — jamais une valeur inventée.
/// </summary>
public static class ReceiptCity
{
    public static string? FromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var lastSegment = address.Split(',')[^1].Trim();
        return lastSegment.Length == 0 ? null : lastSegment;
    }
}
````


### 6.3 Échéancier et solde (`GetStudentBalance`, `InstallmentScheduleCalculator`)
* **Sans plan personnalisé** : une échéance par ligne ponctuelle (due au début de l'année scolaire) et **une échéance par mois** pour chaque mensualité (`schoolYearStart.AddMonths(m-1)`).
* **Avec plan `Active`** : ses échéances remplacent la synthèse.
* **Allocation en cascade** du cumul `AmountPaid` : on solde la 1ʳᵉ échéance, puis la suivante, etc. États : `Paid` (reste ≤ 0), `Partial` (payé > 0), `Overdue` (échue et rien payé), `Pending`.
* `IsInitialScope` : le 1ᵉʳ mois et les frais ponctuels relèvent de l'engagement initial ; `IsDueNow = IsInitialScope || DueDate ≤ aujourd'hui` ; `DueNowTotal` = somme des restes dus « maintenant » (sert au bouton « Payer l'exigible » en caisse).
* **Aucun enregistrement de paiement** par échéance : le calendrier n'est qu'une **lecture** du compteur `AmountPaid`.
* Création d'un plan (`CreateFeeInstallmentPlan`) : **somme des échéances = `TotalDue`** exactement ; l'ancien plan actif passe `Cancelled` (jamais supprimé) ; échéances triées par date. `ApplyFeeInstallmentPlanToClassroom` applique un modèle à toute une classe (`CreatedFromClassroomTemplate = true`).


#### `src/SamaEcole.Application/Finance/Common/InstallmentScheduleCalculator.cs`

````csharp
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Common;

/// <summary>
/// Calcule l'état (payé/partiel/en retard/à venir) de chaque échéance d'une inscription, en allouant
/// le cumul <see cref="Enrollment.AmountPaid"/> ÉCHÉANCE PAR ÉCHÉANCE, dans l'ordre — un versement
/// solde d'abord la plus ancienne échéance, jamais une répartition proportionnelle. Extrait de
/// GetStudentBalanceQuery et GetDuesNoticeQuery, qui recopiaient la même boucle deux fois (Étape 5).
///
/// Deux sources d'échéances, mutuellement exclusives pour une inscription donnée :
///   - un <see cref="FeeInstallmentPlan"/> ACTIF (échéancier négocié, Étape 5) — priorité absolue ;
///   - à défaut, la synthèse mensuelle uniforme dérivée d'<see cref="EnrollmentFeeLine"/> ×
///     TuitionMonthsPerYear (comportement historique, INCHANGÉ pour ne régresser aucune inscription
///     existante qui n'a jamais eu d'échéancier personnalisé).
/// </summary>
public static class InstallmentScheduleCalculator
{
    public enum InstallmentState
    {
        Paid,
        Partial,
        Overdue,
        Pending
    }

    public record CalculatedInstallment(
        string Id,
        string Label,
        decimal Amount,
        decimal AmountPaid,
        decimal RemainingDue,
        DateOnly DueDate,
        InstallmentState Status,

        /// <summary>
        /// Catégorie de frais d'origine (traçabilité + ventilation du reçu de caisse). Renseignée
        /// pour la synthèse dérivée des lignes de frais ; <c>null</c> pour un échéancier personnalisé
        /// (FeeInstallment ne porte pas de catégorie) — le reçu retombe alors sur sa ligne unique.
        /// </summary>
        Guid? FeeCategoryId = null,

        /// <summary>
        /// Vrai si cette échéance fait partie de l'ENGAGEMENT INITIAL, celui que le secrétariat
        /// annonce sur l'attestation et que le tuteur règle en arrivant à la caisse : chaque frais
        /// ponctuel EN ENTIER + le PREMIER mois de chaque frais récurrent (hypothèse « un mois
        /// d'avance », alignée sur EnrollmentReceiptDto.InitialSettlementTotal et
        /// docs/design-references/README.md §1). Les mois 2..N n'en font pas partie : on n'encaisse
        /// pas d'avance une mensualité qui n'est pas échue. Pour un échéancier personnalisé, seule
        /// la première échéance (SequenceNo 1) est marquée.
        /// </summary>
        bool IsInitialScope = false,

        /// <summary>
        /// Vrai si cette échéance est À RÉGLER À LA CAISSE MAINTENANT : l'engagement initial
        /// (<see cref="IsInitialScope"/>), PLUS toute échéance dont la date est atteinte
        /// (<c>DueDate &lt;= today</c>) — typiquement les mensualités des mois 2..N d'une année déjà
        /// commencée. Sans cela, une fois l'inscription et le 1er mois réglés, la caisse affichait
        /// « frais du jour réglés » alors que des mois échus restaient impayés. Les mois à venir
        /// (<c>DueDate &gt; today</c>) n'y sont toujours jamais : on n'encaisse pas d'avance.
        /// </summary>
        bool IsDueNow = false);

    /// <param name="customInstallments">
    /// Échéances du plan ACTIF de l'inscription, DÉJÀ TRIÉES par SequenceNo — null ou vide si aucun
    /// plan personnalisé n'existe, auquel cas la synthèse dérivée des lignes de frais est utilisée.
    /// </param>
    /// <param name="feeLines">
    /// Lignes de frais figées de l'inscription (EnrollmentFeeLine), utilisées uniquement en l'absence
    /// de plan personnalisé — même ordre que GetStudentBalanceQuery/GetDuesNoticeQuery imposaient déjà
    /// (IsRecurring puis Designation), la fonction ne retrie pas elle-même.
    /// </param>
    public static IReadOnlyList<CalculatedInstallment> Calculate(
        decimal totalAmountPaid,
        DateOnly today,
        IReadOnlyList<EnrollmentFeeLine> feeLines,
        DateOnly schoolYearStart,
        IReadOnlyList<FeeInstallment>? customInstallments)
    {
        var remainingPaid = totalAmountPaid;
        var result = new List<CalculatedInstallment>();

        if (customInstallments is { Count: > 0 })
        {
            foreach (var installment in customInstallments.OrderBy(i => i.SequenceNo))
            {
                Allocate($"PLAN-{installment.Id}", installment.Label, installment.Amount, installment.DueDate,
                    feeCategoryId: null, isInitialScope: installment.SequenceNo == 1);
            }

            return result;
        }

        foreach (var line in feeLines)
        {
            if (!line.IsRecurring || line.Months <= 1)
            {
                Allocate($"LINE-{line.Id}", line.Designation, line.LineTotal, schoolYearStart,
                    feeCategoryId: line.FeeCategoryId, isInitialScope: true);
            }
            else
            {
                for (var m = 1; m <= line.Months; m++)
                {
                    Allocate(
                        $"MONTH-{line.Id}-{m}",
                        $"{line.Designation} (Mois {m})",
                        line.UnitAmount,
                        schoolYearStart.AddMonths(m - 1),
                        feeCategoryId: line.FeeCategoryId,
                        // Seul le premier mois relève de l'engagement initial (un mois d'avance).
                        isInitialScope: m == 1);
                }
            }
        }

        return result;

        void Allocate(string id, string label, decimal amount, DateOnly dueDate, Guid? feeCategoryId, bool isInitialScope)
        {
            var paidForThis = Math.Min(remainingPaid, amount);
            remainingPaid -= paidForThis;
            var remainingDue = amount - paidForThis;

            var status = remainingDue <= 0
                ? InstallmentState.Paid
                : paidForThis > 0
                    ? InstallmentState.Partial
                    : dueDate < today
                        ? InstallmentState.Overdue
                        : InstallmentState.Pending;

            result.Add(new CalculatedInstallment(
                id, label, amount, paidForThis, remainingDue, dueDate, status, feeCategoryId, isInitialScope,
                IsDueNow: isInitialScope || dueDate <= today));
        }
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetStudentBalance/GetStudentBalanceQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetStudentBalance;

/// <summary>
/// GET /finance/students/{studentId}/balance — ticket JGK-F02. Point d'entrée de la caisse : à partir
/// d'un élève trouvé par recherche, retrouve SON INSCRIPTION sur l'année scolaire ACTIVE et le solde à
/// encaisser. Aucune route n'indexait jusqu'ici les inscriptions par élève — tout se faisait par
/// EnrollmentId (reçu). Sans année active ou sans inscription de l'élève sur cette année, il n'y a rien
/// à encaisser : 404, jamais un solde inventé.
///
/// Lecture ouverte à tout rôle de l'école (comme le barème et le reçu) : composer/afficher un solde
/// n'est pas un acte d'encaissement, seul POST /finance/payments l'est (règle #4).
/// </summary>
public record GetStudentBalanceQuery(Guid StudentId) : IRequest<StudentBalanceDto>;

public record StudentBalanceDto(
    Guid EnrollmentId,
    Guid StudentId,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    string Status,
    IReadOnlyList<InstallmentDto> Installments,
    IReadOnlyList<StudentBalancePaymentDto> PaymentsHistory,

    /// <summary>
    /// Somme des <see cref="InstallmentDto.RemainingDue"/> des échéances À RÉGLER MAINTENANT
    /// (<see cref="InstallmentDto.IsDueNow"/>) : l'engagement initial (frais ponctuels + premier mois
    /// de scolarité) ET les mensualités dont la date est atteinte, jamais les mois à venir ni le
    /// cumul annuel. Montant proposé par défaut au guichet rapide (pop-up d'encaissement).
    /// </summary>
    decimal DueNowTotal);

public record InstallmentDto(
    string Id,
    string Designation,
    decimal Amount,
    decimal AmountPaid,
    decimal RemainingDue,
    DateTimeOffset DueDate,
    string Status,

    /// <summary>Voir <see cref="InstallmentScheduleCalculator.CalculatedInstallment.IsInitialScope"/> :
    /// échéance de l'engagement initial (frais ponctuel entier, ou premier mois d'un frais récurrent).</summary>
    bool IsInitialScope,

    /// <summary>Voir <see cref="InstallmentScheduleCalculator.CalculatedInstallment.IsDueNow"/> :
    /// échéance à encaisser maintenant (engagement initial OU date d'échéance atteinte).</summary>
    bool IsDueNow,

    /// <summary>Catégorie de frais d'origine — sert à ventiler le reçu de caisse quand plusieurs
    /// échéances sont réglées d'un coup. <c>null</c> pour un échéancier personnalisé.</summary>
    Guid? FeeCategoryId);

public record StudentBalancePaymentDto(
    Guid PaymentId,
    string ReceiptNumber,
    decimal Amount,
    string Method,
    DateTimeOffset PaidAt)
{
    /// <summary>Dernier instant où ce versement peut encore être annulé (délai de grâce, PaymentCancellationPolicy).</summary>
    public DateTimeOffset CancellableUntil => PaymentCancellationPolicy.DeadlineFor(PaidAt);
}

public class GetStudentBalanceQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetStudentBalanceQuery, StudentBalanceDto>
{
    public async Task<StudentBalanceDto> Handle(GetStudentBalanceQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà à l'école courante : un élève ou une inscription
        // d'une autre école est structurellement invisible ici, jamais un solde d'un autre tenant.
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where s.Id == request.StudentId && y.IsActive && e.Status != EnrollmentStatus.Cancelled
            select new
            {
                Enrollment = e,
                Student = s,
                Classroom = c,
                SchoolYear = y
            }
        ).FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException(
            $"Aucune inscription active pour l'élève {request.StudentId} sur l'année scolaire en cours.");

        var lines = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == row.Enrollment.Id)
            .OrderBy(l => l.IsRecurring)
            .ThenBy(l => l.Designation)
            .ToListAsync(cancellationToken);

        // Un plan personnalisé ACTIF (Étape 5) prime sur la synthèse mensuelle uniforme — au plus un
        // existe par inscription (index unique partiel, FeeInstallmentPlanConfiguration).
        var customInstallments = await dbContext.FeeInstallmentPlans.AsNoTracking()
            .Where(p => p.EnrollmentId == row.Enrollment.Id && p.Status == FeeInstallmentPlanStatus.Active)
            .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id))
            .OrderBy(i => i.SequenceNo)
            .ToListAsync(cancellationToken);

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.EnrollmentId == row.Enrollment.Id && p.Status != PaymentStatus.Cancelled)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new StudentBalancePaymentDto(
                p.Id,
                p.ReceiptNumber,
                p.Amount,
                p.Method.ToString(),
                p.PaidAt))
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var todayDateOnly = DateOnly.FromDateTime(now.DateTime);

        var calculated = InstallmentScheduleCalculator.Calculate(
            row.Enrollment.AmountPaid, todayDateOnly, lines, row.SchoolYear.StartDate, customInstallments);

        var installments = calculated
            .Select(c => new InstallmentDto(
                c.Id,
                c.Label,
                c.Amount,
                c.AmountPaid,
                c.RemainingDue,
                new DateTimeOffset(c.DueDate.ToDateTime(TimeOnly.MinValue), now.Offset),
                c.Status.ToString(),
                c.IsInitialScope,
                c.IsDueNow,
                c.FeeCategoryId))
            .ToList();

        var dueNowTotal = installments
            .Where(i => i.IsDueNow)
            .Sum(i => i.RemainingDue);

        return new StudentBalanceDto(
            row.Enrollment.Id,
            row.Student.Id,
            row.Student.Matricule,
            row.Student.FullName,
            row.Classroom.Name,
            row.SchoolYear.Label,
            row.Enrollment.TotalDue,
            row.Enrollment.AmountPaid,
            row.Enrollment.TotalDue - row.Enrollment.AmountPaid,
            row.Enrollment.AmountPaid >= row.Enrollment.TotalDue ? "Paid" : "Partial",
            installments,
            payments,
            dueNowTotal);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/SearchStudentsForCashier/SearchStudentsForCashierQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;

/// <summary>
/// GET /finance/students/search — recherche élève pour la caisse (ticket écran Caisse). Reprend le
/// filtre nom/matricule de GetStudentsQuery, mais y ajoute directement le solde de l'inscription
/// active : la caissière doit voir, DANS LA LISTE de résultats, qu'un élève vient d'être inscrit par
/// le secrétariat mais n'a encore rien versé — sans avoir à sélectionner chaque élève un par un pour
/// le découvrir. Un élève venant d'être inscrit (AmountPaid = 0) ne doit pas se distinguer d'un élève
/// « pareil » qu'après ouverture de sa fiche : le signal doit être visible au premier coup d'œil.
///
/// Route DÉDIÉE à la Finance plutôt qu'un champ ajouté à GetStudentsQuery (Élèves, Enseignants…) :
/// le solde de l'inscription active n'intéresse que cet écran, et l'ajouter à la liste générique
/// aurait alourdi une sous-requête pour tous ses autres usages (bulletins, effectifs...).
///
/// LECTURE ouverte à tout rôle de l'école qui tient une caisse (même garde que GetStudentBalanceQuery) —
/// composer un solde n'est pas un acte d'encaissement, seul POST /finance/payments l'est (règle #4).
/// </summary>
public record SearchStudentsForCashierQuery(string Search) : IRequest<IReadOnlyList<CashierStudentSearchResultDto>>;

public record CashierStudentSearchResultDto(
    Guid Id,
    string Matricule,
    string FullName,
    string? ClassroomName,

    /// <summary>Faux si l'élève n'a aucune inscription vivante sur l'année scolaire ACTIVE — rien à
    /// encaisser pour lui tant qu'il n'est pas (ré)inscrit ; les champs de solde valent alors 0.</summary>
    bool HasActiveEnrollment,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,

    /// <summary>
    /// MONTANT ÉCHU à ce jour, à encaisser en une fois au guichet : l'engagement initial (frais
    /// ponctuels + premier mois de scolarité) PLUS les mensualités dont la date d'échéance est
    /// atteinte, MOINS ce qui a déjà été versé — cf. InstallmentScheduleCalculator.IsDueNow, la
    /// source de vérité unique, la même que GetStudentBalanceQuery. C'est ce chiffre — et jamais
    /// <see cref="TotalDue"/>, le cumul annuel — que le badge de recherche affiche : sans les mois
    /// échus, un élève ayant réglé son inscription mais pas ses mensualités apparaissait « à jour ».
    /// </summary>
    decimal DueNowTotal);

public class SearchStudentsForCashierQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<SearchStudentsForCashierQuery, IReadOnlyList<CashierStudentSearchResultDto>>
{
    public async Task<IReadOnlyList<CashierStudentSearchResultDto>> Handle(
        SearchStudentsForCashierQuery request, CancellationToken cancellationToken)
    {
        var search = request.Search.Trim().ToLower();
        if (search.Length == 0) return [];

        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => new { y.Id, y.StartDate })
            .FirstOrDefaultAsync(cancellationToken);

        // Même remarque que GetStudentsQueryHandler : ToLower() plutôt qu'EF.Functions.ILike (Npgsql),
        // pour qu'Application reste ignorante du provider (Clean Architecture).
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.FullName.ToLower().Contains(search) || s.Matricule.ToLower().Contains(search))
            .OrderBy(s => s.FullName)
            .Take(20)
            .Select(s => new
            {
                s.Id,
                s.Matricule,
                s.FullName,
                s.ClassroomId
            })
            .ToListAsync(cancellationToken);

        if (students.Count == 0) return [];

        var studentIds = students.Select(s => s.Id).ToList();

        // Une seule inscription VIVANTE par élève et par année (contrainte métier) : au plus une ligne
        // par StudentId ici, donc un simple dictionnaire suffit — pas besoin de désambiguïser.
        var enrollmentsById = activeYear is { } year
            ? await dbContext.Enrollments.AsNoTracking()
                .Where(e => studentIds.Contains(e.StudentId) && e.SchoolYearId == year.Id && e.Status != EnrollmentStatus.Cancelled)
                .Select(e => new { e.Id, e.StudentId, e.TotalDue, e.AmountPaid })
                .ToDictionaryAsync(e => e.StudentId, cancellationToken)
            : [];

        var classroomIds = students.Select(s => s.ClassroomId).Distinct().ToList();
        var classroomNamesById = await dbContext.Classrooms.AsNoTracking()
            .Where(c => classroomIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Échéances par inscription — le MÊME calcul que GetStudentBalanceQuery (allocation du cumul versé
        // échéance par échéance, plan personnalisé ACTIF prioritaire sur la synthèse mensuelle), pour que
        // le badge de recherche et le solde ouvert ensuite ne puissent jamais diverger. Peu de lignes
        // (≤ 20 inscriptions) : le regroupement se fait en mémoire.
        var dueNowByEnrollmentId = new Dictionary<Guid, decimal>();
        var enrollmentIds = enrollmentsById.Values.Select(e => e.Id).ToList();
        if (enrollmentIds.Count > 0 && activeYear is not null)
        {
            var feeLinesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                    .Where(l => enrollmentIds.Contains(l.EnrollmentId))
                    .OrderBy(l => l.IsRecurring)
                    .ThenBy(l => l.Designation)
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.EnrollmentId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EnrollmentFeeLine>)g.ToList());

            var customInstallmentsByEnrollment = (await (
                    from p in dbContext.FeeInstallmentPlans.AsNoTracking()
                    where enrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active
                    join i in dbContext.FeeInstallments.AsNoTracking() on p.Id equals i.FeeInstallmentPlanId
                    orderby i.SequenceNo
                    select new { p.EnrollmentId, Installment = i })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.EnrollmentId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<FeeInstallment>)g.Select(x => x.Installment).ToList());

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
            foreach (var enrollment in enrollmentsById.Values)
            {
                var installments = InstallmentScheduleCalculator.Calculate(
                    enrollment.AmountPaid,
                    today,
                    feeLinesByEnrollment.GetValueOrDefault(enrollment.Id) ?? [],
                    activeYear.StartDate,
                    customInstallmentsByEnrollment.GetValueOrDefault(enrollment.Id));

                dueNowByEnrollmentId[enrollment.Id] = installments.Where(i => i.IsDueNow).Sum(i => i.RemainingDue);
            }
        }

        return students
            .Select(s =>
            {
                var enrollment = enrollmentsById.GetValueOrDefault(s.Id);

                var dueNowTotal = enrollment is null ? 0m : dueNowByEnrollmentId.GetValueOrDefault(enrollment.Id, 0m);

                return new CashierStudentSearchResultDto(
                    s.Id,
                    s.Matricule,
                    s.FullName,
                    classroomNamesById.GetValueOrDefault(s.ClassroomId, "Classe supprimée"),
                    enrollment is not null,
                    enrollment?.TotalDue ?? 0m,
                    enrollment?.AmountPaid ?? 0m,
                    (enrollment?.TotalDue ?? 0m) - (enrollment?.AmountPaid ?? 0m),
                    dueNowTotal);
            })
            .ToList();
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFeeInstallmentPlan/CreateFeeInstallmentPlanCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;

/// <summary>
/// POST /finance/enrollments/{enrollmentId}/installment-plan — échéancier personnalisé (Étape 5,
/// recouvrement). Remplace tout plan déjà ACTIF sur l'inscription (bascule à Cancelled, jamais
/// supprimé — règle #6) : un nouvel accord négocié avec la famille est une renégociation, pas une
/// correction d'un plan mal saisi.
/// </summary>
public record CreateFeeInstallmentPlanCommand(
    Guid EnrollmentId,
    string? Reason,
    IReadOnlyList<CreateFeeInstallmentLine> Installments) : IRequest<Guid>;

public record CreateFeeInstallmentLine(string Label, decimal Amount, DateOnly DueDate);
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFeeInstallmentPlan/CreateFeeInstallmentPlanCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;

public class CreateFeeInstallmentPlanCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateFeeInstallmentPlanCommand, Guid>
{
    public async Task<Guid> Handle(CreateFeeInstallmentPlanCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter borne déjà à l'école courante : une inscription d'une autre école est
        // structurellement introuvable ici.
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Id == request.EnrollmentId && e.Status != EnrollmentStatus.Cancelled)
            .Select(e => new { e.Id, e.TotalDue })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription introuvable ou annulée : {request.EnrollmentId}");

        // La somme des échéances doit égaler EXACTEMENT le montant dû : un échéancier qui laisse un
        // reliquat non couvert (ou en réclame davantage) romprait le lien entre le solde affiché
        // (Enrollment.TotalDue - AmountPaid) et le calendrier qui le détaille.
        var sum = request.Installments.Sum(i => i.Amount);
        if (sum != enrollment.TotalDue)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.Installments),
                    $"La somme des échéances ({sum:N0} FCFA) doit être égale au montant dû de l'inscription ({enrollment.TotalDue:N0} FCFA).")
            ]);
        }

        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            // Un plan négocié en remplace un autre : l'ancien reste consultable (Cancelled), jamais
            // supprimé (règle #6) — l'index unique partiel de FeeInstallmentPlanConfiguration n'accepte
            // qu'un seul plan Active par inscription.
            var existingActivePlan = await dbContext.FeeInstallmentPlans
                .Where(p => p.EnrollmentId == request.EnrollmentId && p.Status == FeeInstallmentPlanStatus.Active)
                .SingleOrDefaultAsync(ct);

            if (existingActivePlan is not null)
            {
                existingActivePlan.Status = FeeInstallmentPlanStatus.Cancelled;
            }

            var plan = new FeeInstallmentPlan
            {
                SchoolId = schoolId,
                EnrollmentId = request.EnrollmentId,
                Status = FeeInstallmentPlanStatus.Active,
                Reason = request.Reason,
                CreatedFromClassroomTemplate = false
            };
            dbContext.FeeInstallmentPlans.Add(plan);

            // Trié par date d'échéance : l'ordre de saisie n'a pas d'importance, mais l'allocation en
            // cascade des paiements (InstallmentScheduleCalculator) doit toujours solder la plus
            // ancienne échéance en premier.
            var ordered = request.Installments.OrderBy(i => i.DueDate).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                dbContext.FeeInstallments.Add(new FeeInstallment
                {
                    SchoolId = schoolId,
                    FeeInstallmentPlanId = plan.Id,
                    SequenceNo = index + 1,
                    Label = ordered[index].Label,
                    Amount = ordered[index].Amount,
                    DueDate = ordered[index].DueDate
                });
            }

            await dbContext.SaveChangesAsync(ct);
            return plan.Id;
        }, cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFeeInstallmentPlan/CreateFeeInstallmentPlanCommandValidator.cs`

````csharp
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;

public class CreateFeeInstallmentPlanCommandValidator : AbstractValidator<CreateFeeInstallmentPlanCommand>
{
    public CreateFeeInstallmentPlanCommandValidator()
    {
        RuleFor(v => v.EnrollmentId).NotEmpty();
        RuleFor(v => v.Reason).MaximumLength(500).NoHtml();

        RuleFor(v => v.Installments)
            .NotEmpty().WithMessage("L'échéancier doit comporter au moins une échéance.")
            .Must(i => i.Count <= 24).WithMessage("Un échéancier ne peut pas dépasser 24 échéances.");

        RuleForEach(v => v.Installments).ChildRules(installment =>
        {
            installment.RuleFor(i => i.Label).NotEmpty().MaximumLength(120).NoHtml();
            installment.RuleFor(i => i.Amount).GreaterThan(0);
            installment.RuleFor(i => i.DueDate).NotEmpty();
        });
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/ApplyFeeInstallmentPlanToClassroom/ApplyFeeInstallmentPlanToClassroomCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;

/// <summary>
/// POST /finance/classrooms/{classroomId}/installment-plan — applique le MÊME modèle d'échéancier
/// (répartition en pourcentages + décalages de jours depuis aujourd'hui) à chaque inscription active
/// de la classe (Étape 5, "échéancier par classe"). Chaque inscription reçoit son PROPRE
/// FeeInstallmentPlan calculé sur son propre TotalDue — pas un plan partagé — même matérialisation
/// ligne par ligne que ApplyStandardFeeCommand pour le barème.
/// </summary>
public record ApplyFeeInstallmentPlanToClassroomCommand(
    Guid ClassroomId,
    string? Reason,
    IReadOnlyList<InstallmentTemplateLine> Template) : IRequest<ApplyFeeInstallmentPlanToClassroomResult>;

/// <summary>
/// <paramref name="Percentage"/> est une fraction de <c>Enrollment.TotalDue</c> (ex. 0.4 pour 40 %) ;
/// <paramref name="OffsetDays"/> est le nombre de jours depuis la date d'application du modèle (ex. 0,
/// 30, 60 pour trois versements mensuels à partir d'aujourd'hui).
/// </summary>
public record InstallmentTemplateLine(string Label, decimal Percentage, int OffsetDays);

/// <summary>
/// <paramref name="SkippedCount"/> compte les inscriptions dont le TotalDue est nul (rien à échelonner) —
/// jamais une erreur bloquant tout le lot, comme pour ApplyStandardFeeCommand.
/// </summary>
public record ApplyFeeInstallmentPlanToClassroomResult(int AppliedCount, int SkippedCount);
````


#### `src/SamaEcole.Application/Finance/Commands/ApplyFeeInstallmentPlanToClassroom/ApplyFeeInstallmentPlanToClassroomCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;

public class ApplyFeeInstallmentPlanToClassroomCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<ApplyFeeInstallmentPlanToClassroomCommand, ApplyFeeInstallmentPlanToClassroomResult>
{
    public async Task<ApplyFeeInstallmentPlanToClassroomResult> Handle(
        ApplyFeeInstallmentPlanToClassroomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        // Seules les inscriptions CONFIRMÉES de l'année scolaire ACTIVE reçoivent un échéancier — une
        // inscription annulée, transférée ou d'une année révolue n'a plus rien à échelonner.
        var enrollments = await (
            from e in dbContext.Enrollments
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.ClassroomId == request.ClassroomId && e.Status == EnrollmentStatus.Confirmed && y.IsActive
            select e)
            .ToListAsync(cancellationToken);

        if (enrollments.Count == 0)
        {
            return new ApplyFeeInstallmentPlanToClassroomResult(0, 0);
        }

        var appliedCount = 0;
        var skippedCount = 0;

        // Trié par décalage : l'ordre de saisie n'a pas d'importance, mais SequenceNo doit refléter
        // l'ordre chronologique pour que InstallmentScheduleCalculator alloue les paiements à la bonne
        // échéance en premier (même règle que CreateFeeInstallmentPlanCommand).
        var orderedTemplate = request.Template.OrderBy(t => t.OffsetDays).ToList();

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var existingActivePlans = await dbContext.FeeInstallmentPlans
                .Where(p => enrollments.Select(e => e.Id).Contains(p.EnrollmentId)
                    && p.Status == FeeInstallmentPlanStatus.Active)
                .ToListAsync(ct);
            var existingActiveByEnrollment = existingActivePlans.ToDictionary(p => p.EnrollmentId);

            foreach (var enrollment in enrollments)
            {
                if (enrollment.TotalDue <= 0)
                {
                    // Rien à devoir : appliquer un échéancier romprait l'invariant "somme = TotalDue"
                    // avec une seule échéance à 0 FCFA, sans aucun sens pour la famille.
                    skippedCount++;
                    continue;
                }

                if (existingActiveByEnrollment.TryGetValue(enrollment.Id, out var existingPlan))
                {
                    existingPlan.Status = FeeInstallmentPlanStatus.Cancelled;
                }

                var plan = new FeeInstallmentPlan
                {
                    SchoolId = schoolId,
                    EnrollmentId = enrollment.Id,
                    Status = FeeInstallmentPlanStatus.Active,
                    Reason = request.Reason,
                    CreatedFromClassroomTemplate = true
                };
                dbContext.FeeInstallmentPlans.Add(plan);

                // La DERNIÈRE échéance absorbe l'écart d'arrondi : la somme doit égaler EXACTEMENT
                // TotalDue (même invariant que CreateFeeInstallmentPlanCommand), jamais un centime de
                // trop ou de moins à cause d'un pourcentage qui ne tombe pas rond.
                var amounts = new decimal[orderedTemplate.Count];
                var allocated = 0m;
                for (var i = 0; i < orderedTemplate.Count - 1; i++)
                {
                    amounts[i] = Math.Round(enrollment.TotalDue * orderedTemplate[i].Percentage, 0);
                    allocated += amounts[i];
                }
                amounts[^1] = enrollment.TotalDue - allocated;

                for (var i = 0; i < orderedTemplate.Count; i++)
                {
                    dbContext.FeeInstallments.Add(new FeeInstallment
                    {
                        SchoolId = schoolId,
                        FeeInstallmentPlanId = plan.Id,
                        SequenceNo = i + 1,
                        Label = orderedTemplate[i].Label,
                        Amount = amounts[i],
                        DueDate = today.AddDays(orderedTemplate[i].OffsetDays)
                    });
                }

                appliedCount++;
            }

            await dbContext.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);

        return new ApplyFeeInstallmentPlanToClassroomResult(appliedCount, skippedCount);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/ApplyFeeInstallmentPlanToClassroom/ApplyFeeInstallmentPlanToClassroomCommandValidator.cs`

````csharp
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;

public class ApplyFeeInstallmentPlanToClassroomCommandValidator
    : AbstractValidator<ApplyFeeInstallmentPlanToClassroomCommand>
{
    public ApplyFeeInstallmentPlanToClassroomCommandValidator()
    {
        RuleFor(v => v.ClassroomId).NotEmpty();
        RuleFor(v => v.Reason).MaximumLength(500).NoHtml();

        RuleFor(v => v.Template)
            .NotEmpty().WithMessage("Le modèle d'échéancier doit comporter au moins une échéance.")
            .Must(t => t.Count <= 24).WithMessage("Un modèle d'échéancier ne peut pas dépasser 24 échéances.")
            .Must(t => t.Sum(l => l.Percentage) == 1m)
            .WithMessage("La somme des pourcentages du modèle doit être exactement 100 %.");

        RuleForEach(v => v.Template).ChildRules(line =>
        {
            line.RuleFor(l => l.Label).NotEmpty().MaximumLength(120).NoHtml();
            line.RuleFor(l => l.Percentage).GreaterThan(0).LessThanOrEqualTo(1);
            line.RuleFor(l => l.OffsetDays).GreaterThanOrEqualTo(0);
        });
    }
}
````


### 6.4 Encaissement d'un versement (`POST /finance/payments`) — le cœur du module
Séquence **dans une transaction** (`ExecuteInTransactionAsync`) :
1. **Idempotence** : si `IdempotencyKey` est fournie et qu'un paiement de cette école la porte déjà → renvoyer le résultat existant (même n° de reçu, même solde), *avant* tout contrôle (même si la session est depuis fermée).
2. **Session de caisse ouverte** de l'utilisateur (sinon 422).
3. Charger l'**inscription suivie** (le `xmin` lu sert de jeton) ; 404 si absente (ou d'une autre école) ; refus si `Cancelled`.
4. `remaining = TotalDue − AmountPaid` ; refus si ≤ 0 (« déjà soldée ») ou si `Amount > remaining`.
5. **N° de reçu** gapless (`REC-AAAA-NNNN`).
6. `Status = AmountPaid+Amount ≥ TotalDue ? Paid : Partial` ; `BalanceAfter` ; `VatAmount = VatCalculator(Amount, VatRate)` ; ventilation (`PaymentBreakdown` avec `SchoolId` explicite — sinon violation RLS à l'INSERT ; libellé vide normalisé à `null`).
7. `enrollment.AmountPaid += Amount` → `UPDATE … WHERE xmin = …` : le concurrent perd (409).
8. **Après commit** : invalidation du cache `finance-dashboard` ; **SMS** au tuteur (« Paiement reçu : X FCFA pour NOM (reçu n° …). Reste dû : Y FCFA. Merci de conserver votre reçu. ») via `ISmsDispatcher` qui applique ses propres gardes (formule Premium, interrupteur d'école, solde SMS) et **ne lève jamais**.

Validation de forme (`RecordPaymentCommandValidator`) : inscription obligatoire, montant > 0, moyen valide, TVA ∈ [0;1], libellés de ligne ≤ 60.


#### `src/SamaEcole.Application/Finance/Commands/RecordPayment/RecordPaymentCommand.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// POST /finance/payments — ticket JGK-F02. Encaisse un versement contre une inscription. Le montant
/// dû (<c>TotalDue</c>) n'est jamais fourni ni modifié par le client : la Finance ne fait qu'imputer un
/// versement sur le solde figé à l'inscription (AGENTS.md règles #4 et #10). L'inscription est identifiée
/// par son Id ; l'école vient du JWT.
///
/// IAuditableRequest (JGK-H01) : « paiements » fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé.
/// </summary>
public record RecordPaymentCommand(
    Guid EnrollmentId,
    decimal Amount,
    PaymentMethod Method,
    PaymentCategory Category = PaymentCategory.Tuition,
    string? ReferencePeriod = null,
    List<PaymentBreakdownDto>? Breakdowns = null,

    /// <summary>
    /// Taux de TVA (fraction, ex. 0.18) si ce versement est assujetti, ou null (défaut) sinon — la
    /// scolarité (Category par défaut) est typiquement exonérée. Déclaré explicitement par la Finance,
    /// jamais déduit automatiquement d'une catégorie.
    /// </summary>
    decimal? VatRate = null,

    /// <summary>
    /// Généré côté client à l'ouverture du formulaire de caisse (ticket JGK-L01). Un retry après
    /// coupure réseau avec la MÊME clé ne crée jamais un second paiement — voir le Handler.
    /// </summary>
    Guid? IdempotencyKey = null)
    : IRequest<RecordPaymentResult>, IAuditableRequest;

/// <summary>
/// Une ligne d'imputation du versement. <paramref name="Label"/> est le libellé facultatif de la colonne
/// « Période / Note » du reçu (« Unique », « 2 jeux ») : laissé null, le reçu retombe sur la
/// <c>ReferencePeriod</c> du versement. Descriptif uniquement, il n'entre dans aucun calcul.
/// </summary>
public record PaymentBreakdownDto(Guid FeeCategoryId, decimal AmountAllocated, string? Label = null);


/// <summary>
/// Résultat d'un encaissement : de quoi confirmer à la caisse et imprimer le reçu (le numéro officiel),
/// et afficher le nouveau solde sans recharger la fiche.
/// </summary>
public record RecordPaymentResult(
    Guid PaymentId,
    string ReceiptNumber,
    decimal Amount,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    string Status);
````


#### `src/SamaEcole.Application/Finance/Commands/RecordPayment/RecordPaymentCommandHandler.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// Cœur du ticket JGK-F02. Encaisse un versement en respectant trois invariants :
///   • Règle #4 — <c>TotalDue</c> n'est jamais touché ; on n'incrémente que le cumul encaissé
///     (<see cref="Enrollment.AmountPaid"/>). La Finance impute, elle ne fixe pas le dû.
///   • Règle #5 — deux encaissements CONCURRENTS sur le même solde ne peuvent pas s'écraser : l'écriture
///     de <c>AmountPaid</c> sur l'inscription passe par le verrou optimiste xmin ; le perdant reçoit un
///     409 (ConcurrencyConflictException), jamais un sur-crédit silencieux. C'est le critère obligatoire.
///   • Règle #3 — le numéro de reçu officiel est attribué DANS la transaction, gapless, comme le matricule.
///
/// Tout est atomique : si quoi que ce soit échoue après l'attribution du numéro de reçu, celui-ci est
/// rembobiné avec la transaction — aucun trou dans la numérotation, aucun paiement orphelin.
/// </summary>
public class RecordPaymentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IMatriculeGenerator matriculeGenerator,
    ISmsDispatcher smsDispatcher,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache)
    : IRequestHandler<RecordPaymentCommand, RecordPaymentResult>
{
    public async Task<RecordPaymentResult> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var result = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            // Idempotence côté client (ticket JGK-L01) : un retry après coupure réseau, MÊME clé,
            // rejoue le résultat déjà produit plutôt que de recréer un paiement ou d'échouer en 409 —
            // le caissier qui n'a jamais vu la confirmation du premier essai doit quand même récupérer
            // son numéro de reçu, pas juste un message d'erreur. Vérifié avant tout le reste : même une
            // session de caisse depuis fermée ne doit pas empêcher de rejouer un paiement déjà réussi.
            if (request.IdempotencyKey is { } idempotencyKey)
            {
                var existing = await dbContext.Payments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.SchoolId == schoolId && p.IdempotencyKey == idempotencyKey, ct);

                if (existing is not null)
                {
                    var currentTotalDue = await dbContext.Enrollments.AsNoTracking()
                        .Where(e => e.Id == existing.EnrollmentId)
                        .Select(e => e.TotalDue)
                        .FirstAsync(ct);

                    return new RecordPaymentResult(
                        existing.Id,
                        existing.ReceiptNumber,
                        existing.Amount,
                        currentTotalDue,
                        currentTotalDue - existing.BalanceAfter,
                        existing.BalanceAfter,
                        existing.Status.ToString());
                }
            }

            var activeSession = await dbContext.CashierSessions
                .FirstOrDefaultAsync(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open, ct);

            if (activeSession == null)
            {
                throw new ValidationException([
                    new ValidationFailure("CashierSession", "Aucune session de caisse ouverte pour cet utilisateur.")
                ]);
            }

            // Inscription chargée SUIVIE (pas AsNoTracking) : c'est le xmin lu ici qui sert de jeton
            // optimiste au SaveChanges. Le Global Query Filter + la RLS bornent à l'école courante :
            // encaisser sur une inscription d'une autre école renvoie 404, jamais un versement silencieux.
            var enrollment = await dbContext.Enrollments
                .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, ct)
                ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

            if (enrollment.Status == EnrollmentStatus.Cancelled)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.EnrollmentId),
                        "Impossible d'encaisser sur une inscription annulée.")
                ]);
            }

            var remaining = enrollment.TotalDue - enrollment.AmountPaid;

            if (remaining <= 0)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.Amount), "Cette inscription est déjà soldée.")
                ]);
            }

            if (request.Amount > remaining)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.Amount),
                        $"Le montant dépasse le solde restant ({FormatMoney(remaining)} FCFA).")
                ]);
            }

            // Numéro de reçu officiel, attribué DANS la transaction (règle #3), depuis le même registre
            // gapless par établissement que le reçu d'inscription (JGK-E02).
            var receiptNumber = await matriculeGenerator.GenerateNextReceiptNumberAsync(schoolId, ct);

            var newAmountPaid = enrollment.AmountPaid + request.Amount;
            var status = newAmountPaid >= enrollment.TotalDue ? PaymentStatus.Paid : PaymentStatus.Partial;

            var payment = new Payment
            {
                SchoolId = schoolId,
                EnrollmentId = enrollment.Id,
                CashierSessionId = activeSession.Id,
                Category = request.Category,
                ReferencePeriod = request.ReferencePeriod,
                Amount = request.Amount,
                VatRate = request.VatRate,
                VatAmount = VatCalculator.ComputeVatAmount(request.Amount, request.VatRate),
                Method = request.Method,
                Status = status,
                BalanceAfter = enrollment.TotalDue - newAmountPaid,
                ReceiptNumber = receiptNumber,
                IdempotencyKey = request.IdempotencyKey,
                ReceivedByUserId = actorId,
                PaidAt = timeProvider.GetUtcNow()
            };

            if (request.Breakdowns != null && request.Breakdowns.Any())
            {
                foreach (var breakdown in request.Breakdowns)
                {
                    payment.Breakdowns.Add(new PaymentBreakdown
                    {
                        // SchoolId explicite, comme sur le Payment lui-même : payment_breakdowns est une
                        // table tenant sous RLS PostgreSQL (AGENTS.md règle #2). Sans lui, l'INSERT de
                        // la ligne enfant viole la policy (« new row violates row-level security
                        // policy ») — le SchoolId du parent n'est pas propagé par EF Core.
                        SchoolId = schoolId,
                        FeeCategoryId = breakdown.FeeCategoryId,
                        AmountAllocated = breakdown.AmountAllocated,
                        // Vide normalisé à null : une chaîne blanche ferait échouer le repli sur
                        // ReferencePeriod côté reçu et imprimerait une cellule vide sans raison.
                        Label = string.IsNullOrWhiteSpace(breakdown.Label) ? null : breakdown.Label.Trim()
                    });
                }
            }

            dbContext.Payments.Add(payment);

            // L'écriture qui compte pour la concurrence : UPDATE enrollments ... WHERE xmin = <valeur lue>.
            // Deux encaissements concurrents ont lu le même xmin ; le second UPDATE ne touchera aucune
            // ligne et SaveChanges lèvera une ConcurrencyConflictException -> 409 (règle #5).
            enrollment.AmountPaid = newAmountPaid;

            await dbContext.SaveChangesAsync(ct);

            return new RecordPaymentResult(
                payment.Id,
                receiptNumber,
                request.Amount,
                enrollment.TotalDue,
                newAmountPaid,
                enrollment.TotalDue - newAmountPaid,
                status.ToString());
        }, cancellationToken);

        // Après commit seulement : un versement qui roll back ne doit pas invalider un cache qui restait
        // pourtant correct. Un cache miss superflu est sans conséquence, contrairement à l'inverse.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        await NotifyGuardianAsync(schoolId, request.EnrollmentId, result, cancellationToken);

        return result;
    }

    /// <summary>
    /// Confirmation du versement au tuteur, APRÈS le commit — un SMS annonçant un encaissement que la
    /// transaction annulerait ensuite serait un faux reçu. Même raisonnement que l'e-mail
    /// d'activation d'abonnement, envoyé lui aussi hors transaction.
    ///
    /// N'échoue jamais l'encaissement : SmsDispatcher applique toutes les gardes (formule,
    /// commutateur de l'école, solde, historique) et ne lève pas. Une caisse ne doit pas se bloquer
    /// parce qu'un opérateur télécom est injoignable.
    /// </summary>
    private async Task NotifyGuardianAsync(
        Guid schoolId, Guid enrollmentId, RecordPaymentResult result, CancellationToken cancellationToken)
    {
        var guardian = await (
            from enrollment in dbContext.Enrollments.AsNoTracking()
            where enrollment.Id == enrollmentId
            join student in dbContext.Students.AsNoTracking() on enrollment.StudentId equals student.Id
            select new { student.Id, student.FullName, student.GuardianPhone })
            .FirstOrDefaultAsync(cancellationToken);

        if (guardian is null)
        {
            return;
        }

        var body =
            $"Paiement reçu : {FormatMoney(result.Amount)} FCFA pour {guardian.FullName} "
            + $"(reçu n° {result.ReceiptNumber}). Reste dû : {FormatMoney(result.RemainingBalance)} FCFA. "
            + "Merci de conserver votre reçu.";

        await smsDispatcher.DispatchAsync(
            new SmsDispatchRequest(schoolId, guardian.GuardianPhone, body, SmsTrigger.PaymentReceipt, guardian.Id),
            cancellationToken);
    }

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — comme sur le reçu.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");
}
````


#### `src/SamaEcole.Application/Finance/Commands/RecordPayment/RecordPaymentCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// Validation de forme du versement (le contrôle de SOLDE — ne pas dépasser le restant dû — est une règle
/// métier tenue dans le Handler, à l'intérieur de la transaction, car il dépend de l'état en base).
/// </summary>
public class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(c => c.EnrollmentId)
            .NotEmpty().WithMessage("L'inscription à encaisser est obligatoire.");

        RuleFor(c => c.Amount)
            .GreaterThan(0).WithMessage("Le montant du versement doit être strictement positif.");

        RuleFor(c => c.Method)
            .IsInEnum().WithMessage("Moyen de paiement invalide.");

        RuleFor(c => c.VatRate)
            .InclusiveBetween(0m, 1m).WithMessage("Le taux de TVA doit être compris entre 0 et 1 (ex. 0.18 pour 18 %).")
            .When(c => c.VatRate.HasValue);

        // Libellé de ligne du reçu : borné à la largeur de la colonne (payment_breakdowns.Label,
        // 60 caractères). Sans cette règle, une saisie trop longue remonterait en erreur SQL
        // plutôt qu'en 400 explicite — et la caisse ne saurait pas quoi corriger.
        RuleForEach(c => c.Breakdowns)
            .ChildRules(breakdown => breakdown.RuleFor(b => b.Label)
                .MaximumLength(60)
                .WithMessage("Le libellé d'une ligne de règlement ne peut pas dépasser 60 caractères."))
            .When(c => c.Breakdowns != null);
    }
}
````


#### `src/SamaEcole.Application/Finance/Services/VatCalculator.cs`

````csharp
namespace SamaEcole.Application.Finance.Services;

/// <summary>
/// Calculateur PUR (aucun accès base, aucun effet de bord) de la TVA comprise dans un montant TTC — la
/// SEULE formule officielle du domaine, partagée par l'encaissement (<c>RecordPaymentCommandHandler</c>,
/// TVA collectée) et la dépense (<c>CreateDisbursementCommandHandler</c>, TVA déductible), pour qu'une
/// évolution de la formule ne se fasse jamais qu'à UN seul endroit.
/// </summary>
public static class VatCalculator
{
    /// <summary>
    /// TVA « en dedans » : <paramref name="amountIncludingVat"/> est un montant TTC (de l'argent qui a
    /// réellement changé de main — un encaissement ou une dépense ne peuvent être qu'un total TTC), le
    /// taux en est extrait plutôt qu'ajouté par-dessus. <paramref name="vatRate"/> null (non assujetti,
    /// ex. scolarité exonérée ou salaires) renvoie 0, jamais une valeur inventée.
    /// </summary>
    public static decimal ComputeVatAmount(decimal amountIncludingVat, decimal? vatRate)
    {
        if (vatRate is null || vatRate <= 0)
        {
            return 0m;
        }

        return Math.Round(amountIncludingVat * vatRate.Value / (1 + vatRate.Value), 2, MidpointRounding.AwayFromZero);
    }
}
````


#### `src/SamaEcole.Application/Finance/PaymentReceiptDto.cs`

````csharp
namespace SamaEcole.Application.Finance;

/// <summary>
/// Charge utile du reçu de PAIEMENT (ticket JGK-F02). Suit la même référence de design que le reçu
/// d'inscription (docs/design-references/README.md §1, Volume 1 §7.3bis) : en-tête établissement, titre
/// numéroté, bloc d'informations, tableau des montants, mention obligatoire, signatures.
///
/// Les montants sont FIGÉS à l'instant du versement (via <c>Payment.BalanceAfter</c>) : réimprimer ce
/// reçu affiche toujours le solde de ce jour-là, jamais l'état courant de l'inscription. La mention
/// obligatoire n'est pas transportée ici — c'est une constante posée côté PDF (AGENTS.md règle #12).
/// </summary>
public record PaymentReceiptDto(
    string ReceiptNumber,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolEmail,
    string? SchoolPhone,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolLogoUrl,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    string Method,
    decimal Amount,
    decimal TotalDue,
    decimal AlreadyPaid,
    decimal RemainingBalance,
    DateTimeOffset PaidAt,

    // Ventilation du versement (Payment.Breakdowns) : une ligne par poste imputé. VIDE quand la caisse
    // n'a rien ventilé — le reçu retombe alors sur sa ligne unique « Versement reçu », comme avant.
    IReadOnlyList<PaymentReceiptLineDto> Lines,

    // Période couverte par le versement entier (« Septembre 2026 »), null si non renseignée. Sert de
    // repli au libellé de chaque ligne, et de sous-titre au tableau.
    string? ReferencePeriod,

    // Classe PASSERELLE / ACCÉLÉRÉE (option) : même mention que sur le reçu d'inscription, pour que le
    // reçu de caisse d'un élève de « CI-CP » dise la même chose que la pièce d'inscription qu'il complète.
    bool IsAcceleratedClass = false,

    // Renseigné UNIQUEMENT pour un versement annulé (« REÇU ANNULÉ le … — motif »). Le reçu reste consultable et
    // réimprimable — la trace est conservée — mais il porte alors un bandeau et un filigrane « ANNULÉ » : il ne doit
    // jamais pouvoir passer pour un reçu valable.
    string? CancellationNotice = null)
{
    /// <summary>
    /// Libellé de la colonne « Période / Note » pour une ligne : son libellé propre (« Unique »,
    /// « 2 jeux ») s'il est renseigné, sinon la période du versement entier (« Septembre 2026 »),
    /// sinon <c>null</c> — la cellule reste alors VIDE. Jamais un tiret ni une valeur devinée, même
    /// convention que les coordonnées d'établissement absentes de l'en-tête.
    ///
    /// Résolu ici, dans Application, et non dans le générateur PDF : c'est une règle d'affichage
    /// métier, testable sans instancier QuestPDF (AGENTS.md règle #8).
    /// </summary>
    public string? ResolveLineLabel(PaymentReceiptLineDto line) =>
        !string.IsNullOrWhiteSpace(line.Label) ? line.Label
        : !string.IsNullOrWhiteSpace(ReferencePeriod) ? ReferencePeriod
        : null;

    /// <summary>
    /// Somme des lignes ventilées. Sert de garde-fou au reçu : si elle diffère de
    /// <see cref="Amount"/>, la ventilation est incomplète et le document imprime sa ligne unique
    /// plutôt qu'un tableau qui ne balance pas — un reçu dont le détail ne fait pas le total est
    /// comptablement invalide.
    /// </summary>
    public decimal LinesTotal => Lines.Sum(l => l.Amount);

    /// <summary>Vrai quand la ventilation est exploitable : au moins une ligne, et un total exact.</summary>
    public bool HasBalancedLines => Lines.Count > 0 && LinesTotal == Amount;
}

/// <summary>
/// Une ligne de la ventilation imprimée sur le reçu de caisse : le poste imputé
/// (<paramref name="Designation"/>, le nom de la catégorie de frais), son libellé facultatif de période
/// ou de note, et le montant réellement affecté à ce poste.
/// </summary>
public record PaymentReceiptLineDto(
    string Designation,
    string? Label,
    decimal Amount);
````


#### `src/SamaEcole.Application/Finance/Queries/GetPaymentReceipt/GetPaymentReceiptQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Finance;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPaymentReceipt;

/// <summary>
/// GET /finance/payments/{id}/receipt — ticket JGK-F02. Relit un reçu de paiement déjà émis (pour la
/// réimpression comme pour le rendu PDF). Les montants viennent de la ligne de paiement FIGÉE
/// (Amount, BalanceAfter) : le reçu reste identique même si l'élève verse à nouveau ensuite.
///
/// Lecture ouverte à tout utilisateur de l'école : le tenant vient du JWT (RLS + Global Query Filter),
/// un paiement d'une autre école est introuvable ici (404), jamais servi.
/// </summary>
public record GetPaymentReceiptQuery(Guid PaymentId) : IRequest<PaymentReceiptDto>;

public class GetPaymentReceiptQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetPaymentReceiptQuery, PaymentReceiptDto>
{
    public async Task<PaymentReceiptDto> Handle(GetPaymentReceiptQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le paiement est lu sous le filtre de tenant (filtre global + RLS) : celui d'une autre école est introuvable.
        // L'inscription, l'élève, la classe et l'année peuvent avoir été ARCHIVÉS depuis (élève supprimé après
        // l'annulation de ses paiements) : le reçu, pièce comptable, reste lisible — ils sont donc lus sans filtre
        // d'archivage, avec le SchoolId rétabli à la main (IgnoreQueryFilters retire aussi le filtre de tenant).
        var row = await (
            from p in dbContext.Payments.AsNoTracking()
            join e in dbContext.Enrollments.IgnoreQueryFilters().AsNoTracking() on p.EnrollmentId equals e.Id
            join s in dbContext.Students.IgnoreQueryFilters().AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.IgnoreQueryFilters().AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.IgnoreQueryFilters().AsNoTracking() on e.SchoolYearId equals y.Id
            where p.Id == request.PaymentId
                  && e.SchoolId == schoolId && s.SchoolId == schoolId && c.SchoolId == schoolId && y.SchoolId == schoolId
            select new
            {
                p.ReceiptNumber,
                p.Amount,
                p.BalanceAfter,
                p.Method,
                p.PaidAt,
                p.Status,
                p.CancelledAt,
                p.CancellationReason,
                p.CancellationOutcome,
                p.ReferencePeriod,
                e.TotalDue,
                s.Matricule,
                s.FullName,
                ClassroomName = c.Name,
                c.IsAccelerated,
                YearLabel = y.Label
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Paiement {request.PaymentId} introuvable.");

        // Ventilation du versement. Ordonnée par Id : l'identifiant est un UUID v7, donc ordonnable —
        // les lignes ressortent dans l'ordre où la caisse les a saisies, sans colonne de rang à stocker.
        // Liste vide si la caisse n'a rien ventilé : le reçu retombe alors sur sa ligne unique.
        var lines = await (
            from b in dbContext.PaymentBreakdowns.AsNoTracking()
            join fc in dbContext.FeeCategories.AsNoTracking() on b.FeeCategoryId equals fc.Id
            where b.PaymentId == request.PaymentId
            orderby b.Id
            select new PaymentReceiptLineDto(fc.Name, b.Label, b.AmountAllocated))
            .ToListAsync(cancellationToken);

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == schoolId, cancellationToken);

        return new PaymentReceiptDto(
            row.ReceiptNumber,
            school?.Name ?? string.Empty,
            school?.Address,
            school?.Email,
            school?.Phone,
            ReceiptCity.FromAddress(school?.Address),
            school?.Ninea,
            school?.RegistreCommerce,
            school?.LogoUrl,
            row.Matricule,
            row.FullName,
            row.ClassroomName,
            row.YearLabel,
            row.Method.ToString(),
            row.Amount,
            row.TotalDue,
            row.TotalDue - row.BalanceAfter,
            row.BalanceAfter,
            row.PaidAt,
            lines,
            row.ReferencePeriod,
            row.IsAccelerated,
            row.Status == PaymentStatus.Cancelled
                ? "REÇU ANNULÉ — " + PaymentCancellationOutcomeLabels.CancelledNote(
                    row.CancelledAt, row.CancellationReason, row.CancellationOutcome)
                : null);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetPaymentReceiptPdf/GetPaymentReceiptPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetPaymentReceiptPdf;

/// <summary>
/// GET /finance/payments/{id}/receipt/pdf — ticket JGK-F02. Le reçu de paiement officiel en PDF. On NE
/// recharge PAS les données ici : on réutilise <see cref="GetPaymentReceiptQuery"/> (isolation tenant,
/// montants figés, 404 hors établissement), puis on compose. Le logo est récupéré ici (E/S réseau avec
/// garde SSRF) avant la composition — un logo absent/injoignable rend le reçu sans logo, jamais en erreur.
///
/// IAuditableRequest (JGK-H01) : « impressions » — c'est la seule action liée au reçu que le SERVEUR
/// observe réellement (le bouton Imprimer natif du navigateur, lui, ne fait aucun aller-retour serveur).
/// </summary>
public record GetPaymentReceiptPdfQuery(Guid PaymentId) : IRequest<PaymentReceiptPdfResult>, IAuditableRequest;

/// <summary>Le PDF rendu et le numéro officiel, ce dernier servant à nommer le fichier téléchargé.</summary>
public record PaymentReceiptPdfResult(byte[] Content, string ReceiptNumber);

public class GetPaymentReceiptPdfQueryHandler(
    ISender mediator,
    IPaymentReceiptPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetPaymentReceiptPdfQuery, PaymentReceiptPdfResult>
{
    public async Task<PaymentReceiptPdfResult> Handle(GetPaymentReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(new GetPaymentReceiptQuery(request.PaymentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(receipt.SchoolLogoUrl, cancellationToken);

        return new PaymentReceiptPdfResult(pdfGenerator.Generate(receipt, logo), receipt.ReceiptNumber);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetPayments/GetPaymentsQuery.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetPayments;

/// <summary>
/// GET /api/v1/finance/payments?page=&amp;pageSize=&amp;search= — Historique paginé des encaissements de l'établissement.
/// </summary>
public record GetPaymentsQuery : IRequest<PaginatedPayments>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public string? Method { get; init; }
    public Guid? ClassroomId { get; init; }
}

public record PaymentListItem(
    Guid Id,
    Guid PaymentId,
    string ReceiptNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    decimal Amount,
    string Method,
    string Status,
    DateTimeOffset PaidAt);

public record PaginatedPayments(IReadOnlyList<PaymentListItem> Items, int TotalCount, int Page, int PageSize);
````


#### `src/SamaEcole.Application/Finance/Queries/GetPayments/GetPaymentsQueryHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPayments;

public class GetPaymentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPaymentsQuery, PaginatedPayments>
{
    public async Task<PaginatedPayments> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = from p in dbContext.Payments.AsNoTracking()
                    join e in dbContext.Enrollments.AsNoTracking() on p.EnrollmentId equals e.Id
                    join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
                    join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
                    where p.Status != PaymentStatus.Cancelled
                    select new { Payment = p, Enrollment = e, Student = s, Classroom = c };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.Payment.ReceiptNumber.ToLower().Contains(search) ||
                x.Student.Matricule.ToLower().Contains(search) ||
                x.Student.FullName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Method) &&
            Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
        {
            query = query.Where(x => x.Payment.Method == method);
        }

        if (request.ClassroomId.HasValue && request.ClassroomId.Value != Guid.Empty)
        {
            query = query.Where(x => x.Enrollment.ClassroomId == request.ClassroomId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return new PaginatedPayments([], 0, request.Page, request.PageSize);
        }

        var items = await query
            .OrderByDescending(x => x.Payment.PaidAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new PaymentListItem(
                x.Payment.Id,
                x.Payment.Id,
                x.Payment.ReceiptNumber,
                x.Student.Matricule,
                x.Student.FullName,
                x.Classroom.Name,
                x.Payment.Amount,
                x.Payment.Method.ToString(),
                x.Payment.Status.ToString(),
                x.Payment.PaidAt))
            .ToListAsync(cancellationToken);

        return new PaginatedPayments(items, totalCount, request.Page, request.PageSize);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetPayments/GetPaymentsQueryValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Queries.GetPayments;

public sealed class GetPaymentsQueryValidator : AbstractValidator<GetPaymentsQuery>
{
    public const int MaxPageSize = 100;

    public GetPaymentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
    }
}
````


### 6.5 Sessions de caisse
* **Ouverture** (`OpenCashierSession`) : fonds de caisse initial ; refus si l'utilisateur a déjà une session ouverte (en plus de l'index unique partiel qui tranche une course).
* **Session en cours** (`GetCurrentCashierSession`) : total encaissé en direct, solde théorique, détail par méthode.
* **Clôture** (`CloseCashierSession`) :
  * le périmètre vient du **registre** (`PaymentLedger.SessionEntriesAsync`) : ses propres versements non annulés (ou annulés *après* la clôture), moins les annulations passées dans cette session pour des versements de sessions déjà closes ;
  * `ClosingBalance = fonds + Σ montants` (théorique, toutes méthodes) ;
  * `ExpectedCashAmount = fonds + Σ CashEffect` (espèces seulement ; une annulation ne retire des espèces que si `RefundedToParent`) ;
  * `DiscrepancyAmount = ActualCashAmount − ExpectedCashAmount` ; **motif obligatoire si ≠ 0** (sinon `422`) ;
  * `Status = Closed`, irréversible. Ces colonnes **sont** l'écriture d'ajustement historisée (pas de table séparée).
* **Rapport de clôture PDF** (`GetDailyClosingReportPdf`) : fonds, total encaissé, ventilation par méthode et par catégorie, détail des transactions (heure, n° de reçu, élève, catégorie, mode, montant), écart ; **Journal de caisse du jour PDF** (`GetDailyCashRegisterPdf`) : liste chronologique et totaux par méthode, recalculés à la demande (reproductibles).


#### `src/SamaEcole.Application/Finance/Commands/OpenCashierSession/OpenCashierSessionCommand.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.OpenCashierSession;

public record OpenCashierSessionCommand(decimal OpeningBalance) : IRequest<Guid>, IAuditableRequest;

public class OpenCashierSessionCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<OpenCashierSessionCommand, Guid>
{
    public async Task<Guid> Handle(OpenCashierSessionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var activeSession = await dbContext.CashierSessions
            .FirstOrDefaultAsync(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open, cancellationToken);

        if (activeSession != null)
        {
            throw new ValidationException([
                new ValidationFailure("CashierId", "Cet utilisateur a déjà une session de caisse ouverte.")
            ]);
        }

        var session = new CashierSession
        {
            SchoolId = schoolId,
            CashierId = actorId,
            OpenedAt = timeProvider.GetUtcNow(),
            OpeningBalance = request.OpeningBalance,
            Status = CashierSessionStatus.Open
        };

        dbContext.CashierSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return session.Id;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/OpenCashierSession/OpenCashierSessionCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.OpenCashierSession;

public class OpenCashierSessionCommandValidator : AbstractValidator<OpenCashierSessionCommand>
{
    public OpenCashierSessionCommandValidator()
    {
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CloseCashierSession/CloseCashierSessionCommand.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CloseCashierSession;

/// <summary>
/// Ticket JGK-F09 — le comptage physique du numéraire devient une étape obligatoire de la clôture,
/// pas une vérification annexe : <see cref="ActualCashAmount"/> est un paramètre requis de la commande
/// elle-même, jamais une valeur qu'on pourrait omettre. <see cref="DiscrepancyReason"/> reste facultatif
/// AU NIVEAU DU TYPE — c'est le Handler qui le rend obligatoire, mais seulement quand un écart existe
/// réellement (voir plus bas) : imposer le motif inconditionnellement forcerait une phrase creuse
/// (« RAS ») sur la majorité des clôtures, qui tombent juste.
/// </summary>
public record CloseCashierSessionCommand(
    Guid SessionId,
    decimal ActualCashAmount,
    string? DiscrepancyReason = null) : IRequest<CloseCashierSessionResult>, IAuditableRequest;

public record CloseCashierSessionResult(
    Guid SessionId,
    decimal OpeningBalance,
    decimal TotalCollected,
    decimal ExpectedClosingBalance,
    decimal ExpectedCashAmount,
    decimal ActualCashAmount,
    decimal DiscrepancyAmount,
    string? DiscrepancyReason);

public class CloseCashierSessionCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<CloseCashierSessionCommand, CloseCashierSessionResult>
{
    public async Task<CloseCashierSessionResult> Handle(CloseCashierSessionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var session = await dbContext.CashierSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session {request.SessionId} introuvable.");

        if (session.Status != CashierSessionStatus.Open)
        {
            throw new ValidationException([
                new ValidationFailure("SessionId", "Cette session est déjà fermée ou vérifiée.")
            ]);
        }

        // Registre de la session (PaymentLedger) : ses versements non annulés, MOINS les annulations comptées dans cette
        // session pour des versements d'une session déjà clôturée (écriture négative). Une annulation faite avant
        // la clôture, sur un versement de cette même session, l'en retire simplement.
        var payments = await PaymentLedger.SessionEntriesAsync(dbContext, session, cancellationToken);

        var totalCollected = payments.Sum(p => p.Amount);
        var expectedClosingBalance = session.OpeningBalance + totalCollected;

        // Écart de caisse (JGK-F09) : comparé aux ESPÈCES seulement — un virement, un chèque ou un
        // versement mobile money n'a jamais transité par le tiroir-caisse physique, donc ne peut
        // jamais participer à un manquant ou un surplus constaté au comptage. Comparer ActualCashAmount
        // à expectedClosingBalance (toutes méthodes) produirait un écart systématique et sans rapport
        // avec la réalité dès qu'une école accepte Wave/Orange Money/virement en plus des espèces —
        // même distinction que report.TotalCashInRegister sur le rapport PDF existant.
        var cashCollected = payments.Sum(p => p.CashEffect);
        var expectedCashAmount = session.OpeningBalance + cashCollected;
        var discrepancy = request.ActualCashAmount - expectedCashAmount;

        string? discrepancyReason = null;
        if (discrepancy != 0)
        {
            if (string.IsNullOrWhiteSpace(request.DiscrepancyReason))
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.DiscrepancyReason),
                        "Un écart de caisse a été constaté : justifiez-le avant de pouvoir clôturer la session.")
                ]);
            }

            discrepancyReason = request.DiscrepancyReason.Trim();
        }

        session.ClosedAt = timeProvider.GetUtcNow();
        session.Status = CashierSessionStatus.Closed;
        session.ClosingBalance = expectedClosingBalance;
        session.ExpectedCashAmount = expectedCashAmount;
        session.ActualCashAmount = request.ActualCashAmount;
        session.DiscrepancyAmount = discrepancy;
        session.DiscrepancyReason = discrepancyReason;

        // Rien d'autre à écrire : la session elle-même, une fois close, ne se rouvre ni ne se
        // re-clôture jamais (garde ci-dessus) — ces colonnes SONT l'écriture d'ajustement historisée
        // qu'exige le ticket, pas une table séparée. IAuditableRequest journalise en plus qui a clôturé,
        // quand, et avec quelles valeurs (AGENTS.md, JGK-H01).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseCashierSessionResult(
            session.Id, session.OpeningBalance, totalCollected, expectedClosingBalance,
            expectedCashAmount, request.ActualCashAmount, discrepancy, discrepancyReason);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CloseCashierSession/CloseCashierSessionCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CloseCashierSession;

public class CloseCashierSessionCommandValidator : AbstractValidator<CloseCashierSessionCommand>
{
    public CloseCashierSessionCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.ActualCashAmount).GreaterThanOrEqualTo(0);

        // Le caractère OBLIGATOIRE du motif quand l'écart est non nul se vérifie dans le Handler
        // (Handle), pas ici : l'écart dépend d'ExpectedCashAmount, lui-même calculé depuis les
        // paiements déjà enregistrés en base — un Validator stateless ne peut pas le connaître.
        RuleFor(x => x.DiscrepancyReason).MaximumLength(500);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetCurrentCashierSession/GetCurrentCashierSessionQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetCurrentCashierSession;

/// <summary>
/// GET /finance/sessions/current — la session de caisse OUVERTE de l'utilisateur courant, ou <c>null</c>
/// s'il n'en a aucune. Volume 1 §15.1 : la journée d'encaissement s'ouvre par une session de caisse ;
/// <see cref="Application.Finance.Commands.RecordPayment.RecordPaymentCommandHandler"/> refuse déjà tout
/// encaissement hors d'une session ouverte (422 « Aucune session de caisse ouverte pour cet
/// utilisateur. ») — cette requête est ce qui permet à l'écran /caisse de savoir, AVANT toute tentative
/// d'encaissement, s'il doit proposer d'ouvrir une session ou le statut de celle déjà ouverte.
/// </summary>
public record GetCurrentCashierSessionQuery : IRequest<CurrentCashierSessionDto?>;

public record CurrentCashierSessionDto(
    Guid SessionId,
    decimal OpeningBalance,
    DateTimeOffset OpenedAt,
    decimal TotalCollected,
    int PaymentsCount);

public class GetCurrentCashierSessionQueryHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<GetCurrentCashierSessionQuery, CurrentCashierSessionDto?>
{
    public async Task<CurrentCashierSessionDto?> Handle(
        GetCurrentCashierSessionQuery request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Global Query Filter + RLS bornent déjà à l'école courante (AGENTS.md règle #2) : pas de
        // filtre SchoolId explicite ici.
        var session = await dbContext.CashierSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open, cancellationToken);

        if (session is null)
        {
            return null;
        }

        // Même agrégat que CloseCashierSessionCommandHandler (registre PaymentLedger : paiements annulés exclus,
        // annulations de versements d'une session clôturée en négatif — Volume 1 §15.2) : afficher un total
        // différent de celui que la clôture calculerait romprait la confiance dans le chiffre montré.
        var entries = await PaymentLedger.SessionEntriesAsync(dbContext, session, cancellationToken);

        return new CurrentCashierSessionDto(
            session.Id,
            session.OpeningBalance,
            session.OpenedAt,
            entries.Sum(e => e.Amount),
            entries.Count(e => !e.IsReversal));
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDailyCashRegisterPdf/GetDailyCashRegisterPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;

public record GetDailyCashRegisterPdfQuery(DateOnly Date) : IRequest<DailyCashRegisterPdfResult>, IAuditableRequest;

public record DailyCashRegisterPdfResult(byte[] Content, DateOnly Date);

public record DailyCashRegisterDto(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolEmail,
    string? SchoolPhone,
    string? SchoolLogoUrl,
    DateOnly Date,
    decimal TotalCollected,
    IReadOnlyList<DailyCashRegisterMethodTotalDto> TotalsByMethod,
    IReadOnlyList<DailyCashRegisterPaymentDto> Payments
);

public record DailyCashRegisterMethodTotalDto(
    string Method,
    decimal Total
);

public record DailyCashRegisterPaymentDto(
    DateTimeOffset PaidAt,
    string StudentFullName,
    string Matricule,
    string ReceiptNumber,
    string Method,
    decimal Amount,
    // Annulation d'un versement d'un jour ANTÉRIEUR : montant négatif, daté de l'annulation (PaymentLedger).
    bool IsReversal = false,
    // Mention imprimée sous la ligne : « Annulé le … » sur un versement annulé depuis, ou le détail d'une annulation.
    string? Note = null
);

public interface IDailyCashRegisterPdfGenerator
{
    byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo);
}

public class GetDailyCashRegisterPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IDailyCashRegisterPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetDailyCashRegisterPdfQuery, DailyCashRegisterPdfResult>
{
    public async Task<DailyCashRegisterPdfResult> Handle(GetDailyCashRegisterPdfQuery request, CancellationToken cancellationToken)
    {
        var currentSchoolId = tenantProvider.CurrentSchoolId;

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == currentSchoolId, cancellationToken)
            ?? throw new KeyNotFoundException("L'école courante n'a pas été trouvée.");

        // Using UTC bounds since the database stores dates in UTC.
        // It's a simplification. A better approach would be to use TimeProvider's Local timezone offset.
        // Let's use UtcNow for bound alignment, but Wait, we just want the day.
        var targetStart = new DateTimeOffset(request.Date.Year, request.Date.Month, request.Date.Day, 0, 0, 0, TimeSpan.Zero);
        var targetEnd = targetStart.AddDays(1).AddTicks(-1);

        // Registre daté (PaymentLedger) : les versements du jour — y compris ceux annulés un autre jour, signalés « annulé
        // le … » sans être retirés (le journal d'hier reste celui d'hier) — et, en négatif, les annulations faites CE
        // jour pour des versements d'un jour antérieur. Un versement annulé le jour même disparaît (l'erreur et sa
        // correction se compensent).
        var entries = await PaymentLedger.EntriesAsync(dbContext, targetStart, targetStart.AddDays(1), cancellationToken);

        // L'élève d'un versement annulé peut avoir été supprimé depuis : son nom reste imprimé, suivi de
        // « (élève supprimé) » (PaymentOwnerResolver).
        var owners = await PaymentOwnerResolver.ResolveAsync(
            dbContext,
            currentSchoolId ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant."),
            entries.Select(e => e.EnrollmentId).Distinct().ToList(),
            cancellationToken);

        var validPayments = entries
            .OrderByDescending(e => e.At)
            .Select(e =>
            {
                var owner = owners.GetValueOrDefault(e.EnrollmentId);
                var note = e.IsReversal
                    ? $"Annulation du reçu encaissé le {e.PaidAt:dd/MM/yyyy} — "
                      + $"{(string.IsNullOrWhiteSpace(e.CancellationReason) ? "sans motif" : e.CancellationReason)} "
                      + $"({PaymentCancellationOutcomeLabels.Label(e.Outcome)})"
                    : e.CancelledLater
                        ? PaymentCancellationOutcomeLabels.CancelledNote(e.CancelledAt, e.CancellationReason, e.Outcome)
                        : null;

                return new DailyCashRegisterPaymentDto(
                    e.At, owner?.DisplayName ?? "Inconnu", owner?.Matricule ?? "—",
                    e.ReceiptNumber, e.Method.ToString(), e.Amount, e.IsReversal, note);
            })
            .ToList();

        // Montants SIGNÉS : le total et le récapitulatif par mode sont NETS des annulations.
        var totalCollected = validPayments.Sum(p => p.Amount);

        var totalsByMethod = validPayments
            .GroupBy(p => p.Method)
            .Select(g => new DailyCashRegisterMethodTotalDto(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Total)
            .ToList();

        var data = new DailyCashRegisterDto(
            school.Name,
            school.Address,
            school.Email,
            school.Phone,
            school.LogoUrl,
            request.Date,
            totalCollected,
            totalsByMethod,
            validPayments
        );

        var logo = await logoProvider.TryFetchAsync(data.SchoolLogoUrl, cancellationToken);

        var content = pdfGenerator.Generate(data, logo);

        return new DailyCashRegisterPdfResult(content, request.Date);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDailyClosingReportPdf/DailyClosingReportDto.cs`

````csharp
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;

public record DailyClosingReportDto(
    string SchoolName,
    string SchoolAddress,
    string SchoolLogoUrl,
    DateTime Date,
    string SessionId,
    string CashierName,
    string TimeRange,
    decimal OpeningBalance,
    decimal TotalCollected,
    decimal TotalCashInRegister,
    List<PaymentMethodBreakdownDto> MethodBreakdowns,
    List<FeeCategoryBreakdownDto> CategoryBreakdowns,
    List<TransactionRowDto> Transactions,
    // Ticket JGK-F09 — nuls tant que la session n'est pas clôturée (rapport consultable, en théorie,
    // sur une session encore ouverte) ; toujours renseignés dès CloseCashierSessionCommandHandler passé.
    decimal? ActualCashAmount = null,
    decimal? DiscrepancyAmount = null,
    string? DiscrepancyReason = null,
    // Annulations comptées dans CETTE session pour des versements d'une session déjà clôturée (écritures négatives,
    // PaymentLedger). TotalCollected est déjà NET de ces lignes ; TotalReversed en est la part, en valeur positive.
    List<ReversalRowDto>? Reversals = null,
    decimal TotalReversed = 0m
);

public record PaymentMethodBreakdownDto(PaymentMethod Method, decimal Amount);
public record FeeCategoryBreakdownDto(string CategoryName, decimal Amount);
// CancelledNote : renseigné quand le versement a été annulé APRÈS la clôture — il reste compté dans cette session
// (les chiffres signés ce jour-là ne bougent pas) mais le journal le dit.
public record TransactionRowDto(string Time, string ReceiptNumber, string StudentName, string Category, string Method, decimal Amount, string? CancelledNote = null);

// Une annulation comptée dans cette session : montant NÉGATIF, daté du moment de l'annulation.
public record ReversalRowDto(string Time, string ReceiptNumber, string StudentName, string Method, decimal Amount, string Note);
````


#### `src/SamaEcole.Application/Finance/Queries/GetDailyClosingReportPdf/GetDailyClosingReportPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;

public record GetDailyClosingReportPdfQuery(Guid SessionId) : IRequest<ReceiptPdfResult>, IAuditableRequest;

public record ReceiptPdfResult(byte[] Content, string FileName);

public class GetDailyClosingReportPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISchoolLogoProvider logoProvider,
    IDailyClosingReportPdfGenerator pdfGenerator)
    : IRequestHandler<GetDailyClosingReportPdfQuery, ReceiptPdfResult>
{
    public async Task<ReceiptPdfResult> Handle(GetDailyClosingReportPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Ecole {schoolId} introuvable.");

        var session = await dbContext.CashierSessions.AsNoTracking()
            .Include(s => s.Cashier)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session {request.SessionId} introuvable.");

        // Caissier désactivé/supprimé depuis la clôture : la nav peut être nulle. Le rapport reste
        // émissible — un « — » vaut mieux qu'un NullReferenceException transformé en 500 muet.
        var cashierName = session.Cashier?.FullName ?? "—";

        // Registre de la session (PaymentLedger) : ses versements — y compris ceux annulés APRÈS la clôture, que le journal
        // signale sans les retirer (les chiffres signés ce jour-là ne bougent pas) — et les annulations comptées dans
        // cette session pour des versements d'une session antérieure, en négatif.
        var entries = await PaymentLedger.SessionEntriesAsync(dbContext, session, cancellationToken);

        var paymentIds = entries.Select(e => e.PaymentId).Distinct().ToList();
        var payments = await dbContext.Payments.AsNoTracking()
            .Include(p => p.Breakdowns)
            .ThenInclude(b => b.FeeCategory)
            .Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // L'élève d'un versement annulé peut avoir été supprimé depuis : son nom reste imprimé, suivi de
        // « (élève supprimé) » (PaymentOwnerResolver) — jamais une inscription nulle qui ferait planter le rapport.
        var owners = await PaymentOwnerResolver.ResolveAsync(
            dbContext, schoolId, payments.Values.Select(p => p.EnrollmentId).Distinct().ToList(), cancellationToken);

        string StudentOf(Guid paymentId) =>
            owners.TryGetValue(payments[paymentId].EnrollmentId, out var owner) ? owner.DisplayName : "Inconnu";

        // Montants SIGNÉS : le total, la répartition par mode et par catégorie sont NETS des annulations, donc
        // identiques à ce que la clôture a enregistré (CloseCashierSessionCommandHandler utilise le même registre).
        var totalCollected = entries.Sum(e => e.Amount);
        var totalReversed = -entries.Where(e => e.IsReversal).Sum(e => e.Amount);

        var methodBreakdowns = entries
            .GroupBy(e => e.Method)
            .Select(g => new PaymentMethodBreakdownDto(g.Key, g.Sum(e => e.Amount)))
            .ToList();

        // Répartition par catégorie : les lignes de ventilation du versement si elles existent, sinon sa catégorie
        // principale ; une annulation reprend les mêmes lignes avec le signe opposé.
        var categoryAmounts = new Dictionary<string, decimal>();
        foreach (var entry in entries)
        {
            var payment = payments[entry.PaymentId];
            var sign = entry.IsReversal ? -1m : 1m;

            if (payment.Breakdowns.Any())
            {
                foreach (var b in payment.Breakdowns)
                {
                    var catName = b.FeeCategory.Name;
                    categoryAmounts[catName] = categoryAmounts.GetValueOrDefault(catName) + sign * b.AmountAllocated;
                }
            }
            else
            {
                var catName = payment.Category.ToString();
                categoryAmounts[catName] = categoryAmounts.GetValueOrDefault(catName) + sign * payment.Amount;
            }
        }

        var categoryBreakdowns = categoryAmounts
            .Select(kvp => new FeeCategoryBreakdownDto(kvp.Key, kvp.Value))
            .ToList();

        var transactions = entries.Where(e => !e.IsReversal).Select(e => new TransactionRowDto(
            Time: e.At.ToString("HH:mm"),
            ReceiptNumber: e.ReceiptNumber,
            StudentName: StudentOf(e.PaymentId),
            Category: e.Category.ToString(),
            Method: e.Method.ToString(),
            Amount: e.Amount,
            CancelledNote: e.CancelledLater
                ? PaymentCancellationOutcomeLabels.CancelledNote(e.CancelledAt, e.CancellationReason, e.Outcome)
                : null
        )).ToList();

        var reversals = entries.Where(e => e.IsReversal).Select(e => new ReversalRowDto(
            Time: e.At.ToString("dd/MM HH:mm"),
            ReceiptNumber: e.ReceiptNumber,
            StudentName: StudentOf(e.PaymentId),
            Method: e.Method.ToString(),
            Amount: e.Amount,
            Note: $"Annulation du reçu encaissé le {e.PaidAt:dd/MM/yyyy} — "
                  + $"{(string.IsNullOrWhiteSpace(e.CancellationReason) ? "sans motif" : e.CancellationReason)} "
                  + $"({PaymentCancellationOutcomeLabels.Label(e.Outcome)})"
        )).ToList();

        var timeRange = $"{session.OpenedAt:HH:mm} - {(session.ClosedAt.HasValue ? session.ClosedAt.Value.ToString("HH:mm") : "En cours")}";

        var dto = new DailyClosingReportDto(
            SchoolName: school.Name,
            SchoolAddress: school.Address ?? string.Empty,
            SchoolLogoUrl: school.LogoUrl ?? string.Empty,
            Date: session.OpenedAt.Date,
            SessionId: $"SES-{session.OpenedAt:yyyy-MMdd}-{session.Id.ToString().Substring(0, 4).ToUpper()}",
            CashierName: cashierName,
            TimeRange: timeRange,
            OpeningBalance: session.OpeningBalance,
            TotalCollected: totalCollected,
            // Espèces attendues : seule une annulation REMBOURSÉE au parent en retire (PaymentLedgerEntry.CashEffect).
            TotalCashInRegister: session.OpeningBalance + entries.Sum(e => e.CashEffect),
            MethodBreakdowns: methodBreakdowns,
            CategoryBreakdowns: categoryBreakdowns,
            Transactions: transactions,
            ActualCashAmount: session.ActualCashAmount,
            DiscrepancyAmount: session.DiscrepancyAmount,
            DiscrepancyReason: session.DiscrepancyReason,
            Reversals: reversals,
            TotalReversed: totalReversed
        );

        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);
        var pdfBytes = pdfGenerator.Generate(dto, logo);

        var fileName = $"Rapport_Caisse_{dto.Date:yyyyMMdd}_{dto.SessionId}.pdf";

        return new ReceiptPdfResult(pdfBytes, fileName);
    }
}
````


### 6.6 Annulation / contre-passation d'un versement et registre daté
**Politique** (`PaymentCancellationPolicy`) : délai de grâce = **48 h** après `PaidAt`. Source unique : le handler fait foi et les écrans reçoivent `CancellableUntil` calculé côté serveur.

| Cas | Condition | Résultat |
|---|---|---|
| Annulation ordinaire | ≤ 48 h, rôle autorisé (Directeur, ou Finance si `AllowFinanceToCancelPayments`) | OK |
| Hors délai sans `Reversal` | > 48 h | `409 PAYMENT_CANCELLATION_WINDOW_EXPIRED` |
| Contre-passation | > 48 h **et** `Reversal=true` **et** rôle Directeur | OK ; écriture inverse datée du jour |
| Déjà annulé | | `409 PAYMENT_ALREADY_CANCELLED` |

Effets : `Status = Cancelled`, motif (trim), date, auteur, `CancellationOutcome`, `CancellationSessionId` (session ouverte de l'auteur, s'il en a une) ; `enrollment.AmountPaid = max(0, AmountPaid − Amount)` sous `xmin` ; invalidation des deux caches dashboard. Le paiement **garde son montant, son reçu et son numéro**.

**Registre `PaymentLedger`** — règle unique pour tous les états :
* période [début, fin) : on compte les versements **encaissés** dans la période, **y compris** ceux annulés *après* la fin (signalés `CancelledLater`) ; ceux annulés *dans* la période en sortent ;
* l'annulation, passée dans la période, d'un versement **antérieur** au début est une ligne **négative** (`IsReversal`) datée de l'annulation ;
* trois accès : `EntriesAsync` (liste), `NetAsync` (somme SQL, pour les dashboards ; `selector` choisit montant ou TVA), `SessionEntriesAsync` (une session de caisse).


#### `src/SamaEcole.Application/Finance/PaymentCancellationPolicy.cs`

````csharp
namespace SamaEcole.Application.Finance;

/// <summary>
/// Délai de grâce pendant lequel un versement saisi par erreur peut encore être annulé (spec
/// 2026-10-17-deletion-dependency-guard-design.md §2.4). Une erreur de caisse se remarque souvent le
/// lendemain : l'annulation reste donc possible 48 heures après l'encaissement, même si la session de
/// caisse a été clôturée entre-temps. Passé ce délai, l'annulation ordinaire est BLOQUÉE (409) ; la correction
/// passe alors par une CONTRE-PASSATION : la même opération, demandée explicitement (`Reversal`) et réservée au
/// Directeur. Le registre daté (<see cref="PaymentLedger"/>) en fait une écriture inverse datée du jour : les périodes
/// déjà arrêtées ne sont jamais réécrites, le versement d'origine y reste compté et signalé « annulé le … ».
/// Aucune colonne dédiée : une annulation faite après <see cref="DeadlineFor"/> EST une contre-passation.
///
/// Source unique : le handler d'annulation fait foi, et les écrans reçoivent <c>CancellableUntil</c>
/// calculé ici — jamais une durée recopiée côté JavaScript.
/// </summary>
public static class PaymentCancellationPolicy
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(48);

    /// <summary>Dernier instant où le versement encaissé à <paramref name="paidAt"/> peut être annulé.</summary>
    public static DateTimeOffset DeadlineFor(DateTimeOffset paidAt) => paidAt + GracePeriod;

    /// <summary>Vrai quand le versement encaissé à <paramref name="paidAt"/> ne peut plus être annulé qu'en contre-passation.</summary>
    public static bool IsPastGracePeriod(DateTimeOffset paidAt, DateTimeOffset now) => now > DeadlineFor(paidAt);
}
````


#### `src/SamaEcole.Application/Finance/PaymentCancellationOutcomeLabels.cs`

````csharp
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance;

/// <summary>Libellés français de la destination de l'argent d'un versement annulé, pour les états imprimés.</summary>
public static class PaymentCancellationOutcomeLabels
{
    public static string Label(PaymentCancellationOutcome? outcome) => outcome switch
    {
        PaymentCancellationOutcome.ReusedForEnrollment => "argent réutilisé pour l'inscription de l'enfant",
        PaymentCancellationOutcome.RefundedToParent => "argent remboursé au parent",
        _ => "simple correction, l'argent reste à l'école"
    };

    /// <summary>« Annulé le 13/10/2026 — motif (destination) », la mention portée sur le versement d'origine.</summary>
    public static string CancelledNote(DateTimeOffset? cancelledAt, string? reason, PaymentCancellationOutcome? outcome) =>
        $"Annulé le {cancelledAt:dd/MM/yyyy} — {(string.IsNullOrWhiteSpace(reason) ? "sans motif" : reason)} ({Label(outcome)})";
}
````


#### `src/SamaEcole.Application/Finance/PaymentLedger.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance;

/// <summary>
/// UNE ligne du registre des encaissements : soit un versement (montant positif), soit l'annulation d'un versement
/// antérieur (montant NÉGATIF, daté du jour de l'annulation).
/// </summary>
/// <param name="At">Date de l'écriture : <c>PaidAt</c> pour un versement, <c>CancelledAt</c> pour une annulation.</param>
/// <param name="PaidAt">Date d'encaissement du versement d'origine (identique à <paramref name="At"/> pour un versement).</param>
/// <param name="Amount">Signé : négatif pour une annulation.</param>
/// <param name="VatAmount">Signé comme <paramref name="Amount"/>.</param>
/// <param name="CancelledLater">Versement annulé APRÈS la fin de la période : il reste compté, mais signalé.</param>
public record PaymentLedgerEntry(
    Guid PaymentId,
    Guid EnrollmentId,
    Guid? CashierSessionId,
    string ReceiptNumber,
    PaymentMethod Method,
    PaymentCategory Category,
    decimal Amount,
    decimal VatAmount,
    DateTimeOffset At,
    DateTimeOffset PaidAt,
    bool IsReversal,
    bool CancelledLater,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    PaymentCancellationOutcome? Outcome)
{
    public bool IsCash => Method == PaymentMethod.Cash;

    /// <summary>
    /// Effet de cette écriture sur les ESPÈCES du tiroir-caisse (contrôle de caisse). Un versement en espèces en
    /// ajoute ; l'annulation d'un versement n'en retire QUE si l'argent est rendu au parent (<c>RefundedToParent</c>)
    /// — une simple correction ou une réutilisation pour l'inscription ne déplace aucune espèce (et un doublon de
    /// saisie n'a jamais représenté d'argent : l'écart du jour d'origine l'a déjà absorbé). Le total encaissé
    /// (chiffre d'affaires), lui, est net de TOUTES les annulations.
    /// </summary>
    public decimal CashEffect =>
        !IsCash ? 0m
        : !IsReversal ? Amount
        : Outcome == PaymentCancellationOutcome.RefundedToParent ? Amount
        : 0m;
}

/// <summary>
/// Registre daté des encaissements (spec 2026-10-17-deletion-dependency-guard-design.md §2.4) : ce qui permet de
/// corriger une erreur de caisse SANS réécrire une journée ou une période déjà arrêtée ni faire disparaître la trace.
///
/// Règle unique, appliquée à TOUS les états (journaux de caisse, trésorerie, TVA, tableaux de bord) :
///   • un versement compte dans la période de son encaissement ; s'il est annulé PLUS TARD, il y reste compté
///     (les chiffres d'hier restent ceux d'hier) et il est signalé « annulé le … » ;
///   • s'il est annulé dans la période même, il en sort (l'erreur et sa correction s'annulent) ;
///   • l'annulation d'un versement d'une période ANTÉRIEURE est une écriture négative, comptée dans la période où
///     l'annulation est faite.
/// Rien n'est supprimé : le paiement, son reçu, le motif, l'auteur et la destination de l'argent restent en base.
/// </summary>
public static class PaymentLedger
{
    /// <summary>Versements de la période [start, endExclusive) à compter (annulés plus tard inclus) et annulations négatives.</summary>
    public static async Task<List<PaymentLedgerEntry>> EntriesAsync(
        IApplicationDbContext dbContext, DateTimeOffset start, DateTimeOffset endExclusive, CancellationToken cancellationToken)
    {
        var counted = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < endExclusive
                        && (p.Status != PaymentStatus.Cancelled || p.CancelledAt >= endExclusive))
            .ToListAsync(cancellationToken);

        var reversals = await dbContext.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Cancelled
                        && p.CancelledAt >= start && p.CancelledAt < endExclusive
                        && p.PaidAt < start)
            .ToListAsync(cancellationToken);

        return counted.Select(p => Positive(p, endExclusive))
            .Concat(reversals.Select(Reversal))
            .OrderBy(e => e.At)
            .ToList();
    }

    /// <summary>
    /// Total net de la période, en SQL (tableaux de bord) : versements comptés − annulations de versements antérieurs.
    /// <paramref name="selector"/> choisit la colonne sommée (montant ou TVA).
    /// </summary>
    public static async Task<decimal> NetAsync(
        IApplicationDbContext dbContext, DateTimeOffset start, DateTimeOffset endExclusive,
        System.Linq.Expressions.Expression<Func<Payment, decimal>> selector, CancellationToken cancellationToken)
    {
        var counted = await dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < endExclusive
                        && (p.Status != PaymentStatus.Cancelled || p.CancelledAt >= endExclusive))
            .SumAsync(selector, cancellationToken);

        var reversed = await dbContext.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Cancelled
                        && p.CancelledAt >= start && p.CancelledAt < endExclusive
                        && p.PaidAt < start)
            .SumAsync(selector, cancellationToken);

        return counted - reversed;
    }

    /// <summary>
    /// Écritures d'une SESSION DE CAISSE : ses propres versements (ceux annulés avant sa clôture en sortent, ceux
    /// annulés après y restent, signalés) et les annulations comptées dans cette session pour des versements
    /// d'une session déjà clôturée au moment de l'annulation.
    /// </summary>
    public static async Task<List<PaymentLedgerEntry>> SessionEntriesAsync(
        IApplicationDbContext dbContext, CashierSession session, CancellationToken cancellationToken)
    {
        var closedAt = session.ClosedAt;

        var own = await dbContext.Payments.AsNoTracking()
            .Where(p => p.CashierSessionId == session.Id
                        && (p.Status != PaymentStatus.Cancelled
                            || (closedAt != null && p.CancelledAt > closedAt)))
            .ToListAsync(cancellationToken);

        var reversals = await (
            from p in dbContext.Payments.AsNoTracking()
            join origin in dbContext.CashierSessions.AsNoTracking() on p.CashierSessionId equals origin.Id
            where p.Status == PaymentStatus.Cancelled
                  && p.CancellationSessionId == session.Id
                  && origin.Id != session.Id
                  && origin.ClosedAt != null && origin.ClosedAt < p.CancelledAt
            select p)
            .ToListAsync(cancellationToken);

        return own.Select(p => PositiveInSession(p, closedAt))
            .Concat(reversals.Select(Reversal))
            .OrderBy(e => e.At)
            .ToList();
    }

    private static PaymentLedgerEntry Positive(Payment p, DateTimeOffset endExclusive) => Build(
        p, p.Amount, p.VatAmount, p.PaidAt, isReversal: false,
        cancelledLater: p.Status == PaymentStatus.Cancelled && p.CancelledAt >= endExclusive);

    private static PaymentLedgerEntry PositiveInSession(Payment p, DateTimeOffset? closedAt) => Build(
        p, p.Amount, p.VatAmount, p.PaidAt, isReversal: false,
        cancelledLater: p.Status == PaymentStatus.Cancelled && closedAt != null && p.CancelledAt > closedAt);

    private static PaymentLedgerEntry Reversal(Payment p) => Build(
        p, -p.Amount, -p.VatAmount, p.CancelledAt!.Value, isReversal: true, cancelledLater: false);

    private static PaymentLedgerEntry Build(
        Payment p, decimal amount, decimal vat, DateTimeOffset at, bool isReversal, bool cancelledLater) => new(
        p.Id, p.EnrollmentId, p.CashierSessionId, p.ReceiptNumber, p.Method, p.Category,
        amount, vat, at, p.PaidAt, isReversal, cancelledLater, p.CancelledAt, p.CancellationReason, p.CancellationOutcome);
}
````


#### `src/SamaEcole.Application/Finance/PaymentOwnerResolver.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance;

/// <summary>L'élève auquel appartient un versement, même si sa fiche ou son inscription ont été supprimées depuis.</summary>
public sealed record PaymentOwner(Guid EnrollmentId, Guid StudentId, string FullName, string Matricule, bool StudentDeleted)
{
    /// <summary>Nom à imprimer dans un état : « Awa Fall (élève supprimé) » quand la fiche est archivée.</summary>
    public string DisplayName => StudentDeleted ? $"{FullName} (élève supprimé)" : FullName;
}

/// <summary>
/// Retrouve l'élève d'un versement pour les états financiers (journaux de caisse, reçu). Un élève supprimé laisse des
/// paiements ANNULÉS conservés en base (preuve comptable) : leur historique doit rester lisible. L'inscription et
/// l'élève, filtrés une fois archivés, sont donc lus avec <c>IgnoreQueryFilters()</c> — qui retire AUSSI le filtre de
/// tenant : le <c>SchoolId</c> est rétabli à la main (la RLS PostgreSQL protège en dernier recours).
/// </summary>
public static class PaymentOwnerResolver
{
    public static async Task<Dictionary<Guid, PaymentOwner>> ResolveAsync(
        IApplicationDbContext dbContext, Guid schoolId, IReadOnlyCollection<Guid> enrollmentIds,
        CancellationToken cancellationToken) =>
        await (
            from e in dbContext.Enrollments.IgnoreQueryFilters().AsNoTracking()
            join s in dbContext.Students.IgnoreQueryFilters().AsNoTracking() on e.StudentId equals s.Id
            where e.SchoolId == schoolId && s.SchoolId == schoolId && enrollmentIds.Contains(e.Id)
            select new PaymentOwner(e.Id, s.Id, s.FullName, s.Matricule, s.IsDeleted))
            .ToDictionaryAsync(o => o.EnrollmentId, cancellationToken);
}
````


#### `src/SamaEcole.Application/Finance/Commands/CancelPayment/CancelPaymentCommand.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CancelPayment;

/// <summary>
/// POST /finance/payments/{id}/cancel — annule un versement saisi par erreur (spec
/// 2026-10-17-deletion-dependency-guard-design.md §2.4). Le paiement n'est jamais supprimé ni modifié dans
/// son montant : il passe au statut <c>Cancelled</c> avec un motif, et son numéro de reçu reste consommé.
///
/// <paramref name="Reversal"/> : contre-passation — annulation d'un versement APRÈS le délai de grâce de 48 h. Doit être
/// demandée explicitement et réservée au Directeur ; sans ce drapeau, un versement hors délai reste refusé (409).
///
/// IAuditableRequest : une annulation de paiement est une écriture financière sensible, au même titre que
/// l'encaissement lui-même (JGK-H01).
/// </summary>
public record CancelPaymentCommand(
    Guid PaymentId,
    string Reason,
    PaymentCancellationOutcome Outcome = PaymentCancellationOutcome.KeptBySchool,
    bool Reversal = false)
    : IRequest<CancelPaymentResult>, IAuditableRequest;

/// <summary>De quoi afficher le nouveau solde de l'inscription sans recharger la fiche.</summary>
public record CancelPaymentResult(
    Guid PaymentId,
    string ReceiptNumber,
    decimal CancelledAmount,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    DateTimeOffset CancelledAt,
    bool IsReversal = false);
````


#### `src/SamaEcole.Application/Finance/Commands/CancelPayment/CancelPaymentCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CancelPayment;

/// <summary>
/// Annule un versement saisi par erreur. Trois invariants :
///   • Règle #6 / #3 — le paiement n'est jamais supprimé ni réécrit : même montant, même reçu, même numéro.
///     Seuls son statut, le motif, la date et l'auteur de l'annulation sont posés.
///   • Règle #5 — le montant est retiré de <see cref="Domain.Entities.Enrollment.AmountPaid"/> par une écriture
///     sur l'inscription, donc sous son verrou optimiste xmin, exactement comme l'encaissement : une annulation
///     concurrente de la même ligne, ou un encaissement simultané, fait perdre le second et lui renvoie un 409
///     — jamais un solde faux. Le paiement lui-même ne porte pas de jeton, c'est l'inscription qui sérialise.
///   • Délai de grâce de 48 h (<see cref="PaymentCancellationPolicy"/>) : en deçà, l'annulation reste possible même
///     si la session de caisse est clôturée ; les états de caisse se recalculent alors sans ce versement. Au-delà,
///     l'annulation ordinaire est refusée (409) : seule la CONTRE-PASSATION, demandée explicitement et réservée au
///     Directeur, passe — le registre daté la comptabilise comme une écriture inverse datée du jour, sans réécrire
///     les périodes déjà arrêtées.
/// </summary>
public class CancelPaymentCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache)
    : IRequestHandler<CancelPaymentCommand, CancelPaymentResult>
{
    public async Task<CancelPaymentResult> Handle(CancelPaymentCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser le
        // paiement d'une autre école renvoie 404, jamais une annulation silencieuse.
        var payment = await dbContext.Payments
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Paiement {request.PaymentId} introuvable.");

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new BusinessRuleException(
                $"Le paiement {payment.ReceiptNumber} est déjà annulé.", "PAYMENT_ALREADY_CANCELLED");
        }

        // Délai de grâce (48 h après l'encaissement) : une erreur remarquée le lendemain reste corrigeable,
        // même si la session de caisse est déjà clôturée. Passé ce délai, seule la contre-passation (écriture
        // inverse datée du jour, voir PaymentLedger) est possible : explicite et réservée au Directeur.
        var now = timeProvider.GetUtcNow();
        var isReversal = PaymentCancellationPolicy.IsPastGracePeriod(payment.PaidAt, now);

        if (isReversal)
        {
            var hours = (int)PaymentCancellationPolicy.GracePeriod.TotalHours;

            if (!request.Reversal)
            {
                throw new BusinessRuleException(
                    $"Impossible d'annuler le paiement {payment.ReceiptNumber} : le délai d'annulation de {hours} heures "
                    + "après l'encaissement est dépassé. Le Directeur peut passer une contre-passation (écriture inverse datée d'aujourd'hui).",
                    "PAYMENT_CANCELLATION_WINDOW_EXPIRED");
            }

            if (currentUser.Role != Role.Directeur)
            {
                throw new UnauthorizedAccessException("Seul le Directeur peut passer une contre-passation.");
            }
        }

        // Inscription chargée SUIVIE : c'est son xmin qui sert de jeton optimiste au SaveChanges.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == payment.EnrollmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {payment.EnrollmentId} introuvable.");

        // Session de caisse ouverte de l'auteur, s'il en a une : c'est là que l'annulation d'un versement d'une session
        // déjà clôturée sera comptée (écriture négative). Le Directeur n'en a pas toujours : dans ce cas l'annulation
        // figure dans les états par date, mais dans aucun écart de caisse.
        var cancellerSessionId = await dbContext.CashierSessions
            .Where(s => s.CashierId == actorId && s.Status == CashierSessionStatus.Open)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        payment.Status = PaymentStatus.Cancelled;
        payment.CancelledAt = now;
        payment.CancelledByUserId = actorId;
        payment.CancellationReason = request.Reason.Trim();
        payment.CancellationOutcome = request.Outcome;
        payment.CancellationSessionId = cancellerSessionId;

        // Jamais négatif : un cumul déjà incohérent ne doit pas être aggravé par une annulation.
        enrollment.AmountPaid = Math.Max(0m, enrollment.AmountPaid - payment.Amount);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Après commit seulement, comme l'encaissement : le solde dû, le recouvrement et les effectifs
        // des deux dashboards ont changé.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return new CancelPaymentResult(
            payment.Id,
            payment.ReceiptNumber,
            payment.Amount,
            enrollment.TotalDue,
            enrollment.AmountPaid,
            enrollment.TotalDue - enrollment.AmountPaid,
            now,
            isReversal);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CancelPayment/CancelPaymentCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CancelPayment;

/// <summary>
/// Validation de forme. Le motif est obligatoire : il est affiché sur le reçu annulé et dans l'historique
/// financier, c'est ce qui permet de comprendre une correction des mois plus tard. Borné à 500 caractères,
/// la largeur de <c>payments.CancellationReason</c>.
/// </summary>
public class CancelPaymentCommandValidator : AbstractValidator<CancelPaymentCommand>
{
    public CancelPaymentCommandValidator()
    {
        RuleFor(c => c.PaymentId)
            .NotEmpty().WithMessage("Le paiement à annuler est obligatoire.");

        RuleFor(c => c.Outcome)
            .IsInEnum().WithMessage("Destination de l'argent invalide.");

        RuleFor(c => c.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
                .WithMessage("Le motif de l'annulation est obligatoire.")
            .Must(reason => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length >= 3)
                .WithMessage("Le motif de l'annulation est trop court (3 caractères minimum).")
            .MaximumLength(500).WithMessage("Le motif de l'annulation ne peut pas dépasser 500 caractères.");
    }
}
````


### 6.7 Recouvrement
* **Balance âgée** (`GetDebtorAgingReport` + export Excel) : lecture seule, toujours actuelle, mêmes chiffres à l'écran et dans le fichier.
* **Lots de relance** : `DebtorAgingHostedService` (toutes les 24 h par défaut — `Finance:DebtorAging:Enabled/Interval`) calcule, **pour chaque école** (scope et connexion neufs), des **brouillons** par classe : débiteurs dont une échéance est en retard au-delà de `DebtorReminderThresholdDays`. Rien n'est envoyé. L'envoi (`SendDebtorReminderBatch`) est un clic humain qui met en file les SMS (`SmsTrigger.DuesReminder`) et relie `SmsMessageId` à chaque ligne ; `DismissDebtorReminderBatch` écarte un lot ; `recompute` force le calcul.
* **Sommation pour impayés** (`GetDuesNotice` + PDF) : lettre de mise en demeure au tuteur ; **refusée** si l'inscription est à jour.
* **Engagement financier** (`CreateFinancialCommitment`, `GetFinancialCommitment` + PDF) : reconnaissance de dette ; ne touche jamais `AmountPaid`.


#### `src/SamaEcole.Application/Finance/Common/DebtorAgingSettings.cs`

````csharp
namespace SamaEcole.Application.Finance.Common;

/// <summary>
/// Réglages de DebtorAgingHostedService (Étape 5 — recouvrement semi-automatique). Section
/// « Finance:DebtorAging » (voir .env.example et AddInfrastructure), même idiome que SmsQueueSettings.
/// </summary>
public class DebtorAgingSettings
{
    /// <summary>
    /// Coupé par défaut dans les tests fonctionnels (Finance__DebtorAging__Enabled=false, voir
    /// AuthApiFactory) — même raison que SmsQueueSettings.Enabled : un lot brouillon créé pendant
    /// l'exécution d'un test rendrait son résultat non déterministe.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cadence entre deux calculs. 24h suffit largement (un retard ne varie pas d'heure en heure) —
    /// aucun alignement sur minuit local : un lot calculé à un autre moment de la journée n'a aucune
    /// conséquence produit, et l'alignement horaire ajouterait une gestion de fuseau sans bénéfice.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDebtorAgingReport/GetDebtorAgingReportQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;

/// <summary>
/// Rapport comptable des débiteurs de l'établissement (Étape 5), en LECTURE SEULE et TOUJOURS actuel
/// (pas limité aux classes déjà couvertes par un lot de relance brouillon, contrairement à
/// GenerateDebtorReminderBatchesCommand) — répond au Volume 1 §7.5. Réutilisé tel quel par l'export
/// Excel (GetDebtorAgingExportQuery), pour que l'écran et le fichier téléchargé portent exactement
/// les mêmes chiffres (même principe que GetRevenueConsolidation/GetRevenueConsolidationExcel).
/// </summary>
public record GetDebtorAgingReportQuery : IRequest<DebtorAgingReportDto>;

public record DebtorAgingReportDto(DateOnly GeneratedOn, IReadOnlyList<DebtorAgingRow> Debtors);

public record DebtorAgingRow(
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    decimal RemainingBalance,
    int DaysOverdue,
    string? GuardianPhone);

public class GetDebtorAgingReportQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetDebtorAgingReportQuery, DebtorAgingReportDto>
{
    public async Task<DebtorAgingReportDto> Handle(
        GetDebtorAgingReportQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        var debtors = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.Status == EnrollmentStatus.Confirmed && e.AmountPaid < e.TotalDue && y.IsActive
            select new
            {
                Enrollment = e,
                Student = s,
                ClassroomName = c.Name,
                SchoolYearStart = y.StartDate
            })
            .ToListAsync(cancellationToken);

        // Lignes de frais et échéanciers actifs chargés EN LOT pour tous les débiteurs (deux requêtes au
        // total) plutôt que deux requêtes PAR débiteur : ToLookup conserve l'ordre SQL au sein de chaque
        // inscription (IsRecurring puis Designation ; SequenceNo), celui qu'attend le calculateur.
        var enrollmentIds = debtors.Select(d => d.Enrollment.Id).ToList();

        var linesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => enrollmentIds.Contains(l.EnrollmentId))
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.EnrollmentId);

        var installmentsByEnrollment = (await (
                    from p in dbContext.FeeInstallmentPlans.AsNoTracking()
                    where enrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active
                    join i in dbContext.FeeInstallments.AsNoTracking() on p.Id equals i.FeeInstallmentPlanId
                    orderby i.SequenceNo
                    select new { p.EnrollmentId, Installment = i })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.EnrollmentId, x => x.Installment);

        var rows = new List<DebtorAgingRow>();

        foreach (var debtor in debtors)
        {
            var lines = linesByEnrollment[debtor.Enrollment.Id].ToList();
            var customInstallments = installmentsByEnrollment[debtor.Enrollment.Id].ToList();

            var overdue = InstallmentScheduleCalculator
                .Calculate(debtor.Enrollment.AmountPaid, today, lines, debtor.SchoolYearStart, customInstallments)
                .Where(c => c.RemainingDue > 0 && c.DueDate < today)
                .ToList();

            if (overdue.Count == 0)
            {
                // Solde dû mais aucune échéance encore dépassée : pas un retard, hors du rapport.
                continue;
            }

            var oldestDueDate = overdue.Min(c => c.DueDate);
            var daysOverdue = today.DayNumber - oldestDueDate.DayNumber;

            rows.Add(new DebtorAgingRow(
                debtor.Student.Matricule,
                debtor.Student.FullName,
                debtor.ClassroomName,
                debtor.Enrollment.TotalDue - debtor.Enrollment.AmountPaid,
                daysOverdue,
                debtor.Student.GuardianPhone));
        }

        return new DebtorAgingReportDto(today, rows.OrderByDescending(r => r.DaysOverdue).ToList());
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDebtorAgingExport/GetDebtorAgingExportQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDebtorAgingExport;

/// <summary>
/// GET /finance/reports/debtor-aging/excel — le MÊME rapport que GetDebtorAgingReportQuery, au format
/// comptable (.xlsx). Réutilise la requête au lieu de recalculer, même principe que
/// GetRevenueConsolidationExcelQuery.
/// </summary>
public record GetDebtorAgingExportQuery : IRequest<DebtorAgingExcelFile>;

public record DebtorAgingExcelFile(byte[] Content, string FileName);

public class GetDebtorAgingExportQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IDebtorAgingExcelGenerator excelGenerator)
    : IRequestHandler<GetDebtorAgingExportQuery, DebtorAgingExcelFile>
{
    public async Task<DebtorAgingExcelFile> Handle(
        GetDebtorAgingExportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var report = await mediator.Send(new GetDebtorAgingReportQuery(), cancellationToken);

        // `schools` n'est pas une table tenant : le filtre SchoolId est explicite.
        var schoolName = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        var content = excelGenerator.Generate(report, schoolName);
        var fileName = $"debiteurs_{report.GeneratedOn:yyyyMMdd}.xlsx";

        return new DebtorAgingExcelFile(content, fileName);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDebtorReminderBatches/GetDebtorReminderBatchesQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDebtorReminderBatches;

/// <summary>
/// GET /finance/dues-reminder-batches — liste des lots de relance de débiteurs (Étape 5), calculés
/// chaque nuit par classe. Répond au Volume 1 §7.5 : « Liste automatique des élèves débiteurs :
/// montant dû, nombre de jours de retard » — jamais implémentée avant cette étape en dehors du calcul
/// interne de SendDuesReminderSmsCommand.
///
/// Lecture ouverte à Directeur/Finance uniquement (comme le tableau de bord financier) : la liste des
/// débiteurs est une donnée financière sensible, pas un simple solde d'élève.
/// </summary>
public record GetDebtorReminderBatchesQuery(DebtorReminderBatchStatus? Status) : IRequest<IReadOnlyList<DebtorReminderBatchDto>>;

public record DebtorReminderBatchDto(
    Guid Id,
    Guid ClassroomId,
    string ClassroomName,
    int ThresholdDays,
    DateTimeOffset GeneratedAt,
    string Status,
    DateTimeOffset? SentAt,
    IReadOnlyList<DebtorReminderBatchItemDto> Items);

public record DebtorReminderBatchItemDto(
    Guid EnrollmentId,
    Guid StudentId,
    string StudentFullName,
    string Matricule,
    string? GuardianPhone,
    decimal RemainingBalance,
    int DaysOverdue);

public class GetDebtorReminderBatchesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetDebtorReminderBatchesQuery, IReadOnlyList<DebtorReminderBatchDto>>
{
    public async Task<IReadOnlyList<DebtorReminderBatchDto>> Handle(
        GetDebtorReminderBatchesQuery request, CancellationToken cancellationToken)
    {
        var batchesQuery = dbContext.DebtorReminderBatches.AsNoTracking().AsQueryable();
        if (request.Status is not null)
        {
            batchesQuery = batchesQuery.Where(b => b.Status == request.Status);
        }

        var batches = await (
            from b in batchesQuery
            join c in dbContext.Classrooms.AsNoTracking() on b.ClassroomId equals c.Id
            orderby b.GeneratedAt descending
            select new { Batch = b, ClassroomName = c.Name })
            .ToListAsync(cancellationToken);

        if (batches.Count == 0)
        {
            return [];
        }

        var batchIds = batches.Select(b => b.Batch.Id).ToList();

        var items = await (
            from i in dbContext.DebtorReminderBatchItems.AsNoTracking()
            where batchIds.Contains(i.DebtorReminderBatchId)
            join s in dbContext.Students.AsNoTracking() on i.StudentId equals s.Id
            select new { i.DebtorReminderBatchId, Item = i, s.FullName, s.Matricule })
            .ToListAsync(cancellationToken);

        var itemsByBatch = items
            .GroupBy(x => x.DebtorReminderBatchId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DebtorReminderBatchItemDto>)g
                    .OrderByDescending(x => x.Item.DaysOverdue)
                    .Select(x => new DebtorReminderBatchItemDto(
                        x.Item.EnrollmentId,
                        x.Item.StudentId,
                        x.FullName,
                        x.Matricule,
                        x.Item.GuardianPhone,
                        x.Item.RemainingBalance,
                        x.Item.DaysOverdue))
                    .ToList());

        return batches
            .Select(b => new DebtorReminderBatchDto(
                b.Batch.Id,
                b.Batch.ClassroomId,
                b.ClassroomName,
                b.Batch.ThresholdDays,
                b.Batch.GeneratedAt,
                b.Batch.Status.ToString(),
                b.Batch.SentAt,
                itemsByBatch.GetValueOrDefault(b.Batch.Id, [])))
            .ToList();
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/GenerateDebtorReminderBatches/GenerateDebtorReminderBatchesCommand.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;

/// <summary>
/// Calcule les débiteurs de l'école courante et prépare un lot BROUILLON par classe (Étape 5 —
/// recouvrement semi-automatique). N'envoie AUCUN SMS elle-même : §13.7 du cahier des charges interdit
/// l'envoi de masse automatique, seul SendDebtorReminderBatchCommand (déclenché à la main par un
/// Directeur/Finance) fait effectivement partir les messages d'un lot.
///
/// Déclenchée une fois par nuit et par école par DebtorAgingHostedService, sous
/// TenantProvider.RunAsSchoolAsync — le "tenant courant" ici est celui posé par l'override, jamais un
/// JWT. Peut aussi être appelée manuellement (ex. bouton "Recalculer" côté écran Recouvrement).
/// </summary>
public record GenerateDebtorReminderBatchesCommand : IRequest<int>;

public class GenerateDebtorReminderBatchesCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider,
    ILogger<GenerateDebtorReminderBatchesCommandHandler> logger)
    : IRequestHandler<GenerateDebtorReminderBatchesCommand, int>
{
    public async Task<int> Handle(GenerateDebtorReminderBatchesCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Double interrupteur, comme partout ailleurs sur les relances (SendDuesReminderSmsCommand) :
        // aucun brouillon n'est généré tant que le Directeur n'a pas explicitement activé les relances
        // SMS — générer des lots qu'aucune école n'a demandés serait un travail inutile et confus.
        var settings = await dbContext.SchoolSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            // Distinct d'un opt-out délibéré (SmsOnDuesReminder = false, cas normal ci-dessous) :
            // une école sans aucune ligne SchoolSettings n'a jamais été paramétrée — un signal utile
            // pour repérer un établissement mal provisionné, invisible tant que ce tour ne fait que
            // rendre 0 silencieusement (DebtorAgingHostedService.cs).
            logger.LogWarning(
                "Calcul des lots de relance ignoré pour l'établissement {SchoolId} : aucun paramétrage (SchoolSettings) configuré.",
                schoolId);
            return 0;
        }

        if (!settings.SmsOnDuesReminder)
        {
            return 0;
        }

        var hasActiveSchoolYear = await dbContext.SchoolYears.AsNoTracking()
            .AnyAsync(y => y.IsActive, cancellationToken);
        if (!hasActiveSchoolYear)
        {
            logger.LogWarning(
                "Calcul des lots de relance ignoré pour l'établissement {SchoolId} : aucune année scolaire active.",
                schoolId);
            return 0;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        // Ne jamais empiler : une classe qui a déjà un brouillon EN ATTENTE n'en reçoit pas un second —
        // c'est à l'humain de l'envoyer ou de l'écarter avant qu'un nouveau calcul ne la reprenne.
        var classroomsWithDraft = await dbContext.DebtorReminderBatches.AsNoTracking()
            .Where(b => b.Status == DebtorReminderBatchStatus.Draft)
            .Select(b => b.ClassroomId)
            .ToListAsync(cancellationToken);

        var debtors = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.Status == EnrollmentStatus.Confirmed
                && e.AmountPaid < e.TotalDue
                && y.IsActive
                && !classroomsWithDraft.Contains(e.ClassroomId)
            select new { Enrollment = e, Student = s, SchoolYearStart = y.StartDate })
            .ToListAsync(cancellationToken);

        if (debtors.Count == 0)
        {
            return 0;
        }

        // Chargées une seule fois pour tous les débiteurs (au lieu de 2 requêtes par débiteur) —
        // corrige une régression N+1 introduite lors de la fiabilisation du calcul (commit e7005bf).
        var debtorEnrollmentIds = debtors.Select(d => d.Enrollment.Id).ToList();

        var feeLinesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => debtorEnrollmentIds.Contains(l.EnrollmentId))
                .OrderBy(l => l.IsRecurring)
                .ThenBy(l => l.Designation)
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var installmentsByEnrollment = (await dbContext.FeeInstallmentPlans.AsNoTracking()
                .Where(p => debtorEnrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active)
                .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id), (p, i) => new { p.EnrollmentId, Installment = i })
                .OrderBy(x => x.Installment.SequenceNo)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Installment).ToList());

        var createdCount = 0;

        foreach (var group in debtors.GroupBy(d => d.Enrollment.ClassroomId))
        {
            var items = new List<DebtorReminderBatchItem>();

            foreach (var debtor in group)
            {
                var lines = feeLinesByEnrollment.GetValueOrDefault(debtor.Enrollment.Id, []);
                var customInstallments = installmentsByEnrollment.GetValueOrDefault(debtor.Enrollment.Id, []);

                var overdue = InstallmentScheduleCalculator
                    .Calculate(debtor.Enrollment.AmountPaid, today, lines, debtor.SchoolYearStart, customInstallments)
                    .Where(c => c.RemainingDue > 0 && c.DueDate < today)
                    .ToList();

                if (overdue.Count == 0)
                {
                    // Un solde dû dont aucune échéance n'est encore dépassée (tout est Pending/Partial
                    // à venir) n'est pas un retard : rien à relancer aujourd'hui.
                    continue;
                }

                // Ancienneté = plus ancienne échéance encore impayée, pas la moyenne : c'est ce qui
                // justifie une relance, même si des échéances plus récentes ont déjà été soldées.
                var oldestDueDate = overdue.Min(c => c.DueDate);
                var daysOverdue = today.DayNumber - oldestDueDate.DayNumber;

                if (daysOverdue < settings.DebtorReminderThresholdDays)
                {
                    continue;
                }

                items.Add(new DebtorReminderBatchItem
                {
                    SchoolId = schoolId,
                    EnrollmentId = debtor.Enrollment.Id,
                    StudentId = debtor.Student.Id,
                    GuardianPhone = debtor.Student.GuardianPhone,
                    RemainingBalance = debtor.Enrollment.TotalDue - debtor.Enrollment.AmountPaid,
                    DaysOverdue = daysOverdue
                });
            }

            if (items.Count == 0)
            {
                continue;
            }

            var batch = new DebtorReminderBatch
            {
                SchoolId = schoolId,
                ClassroomId = group.Key,
                ThresholdDays = settings.DebtorReminderThresholdDays,
                GeneratedAt = timeProvider.GetUtcNow(),
                Status = DebtorReminderBatchStatus.Draft
            };
            dbContext.DebtorReminderBatches.Add(batch);

            foreach (var item in items)
            {
                item.DebtorReminderBatchId = batch.Id;
                dbContext.DebtorReminderBatchItems.Add(item);
            }

            createdCount++;
        }

        if (createdCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return createdCount;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/SendDebtorReminderBatch/SendDebtorReminderBatchCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;

/// <summary>
/// POST /finance/dues-reminder-batches/{id}/send — LE seul geste qui fait effectivement partir les
/// SMS d'un lot BROUILLON (Étape 5). Déclenché à la main par un Directeur/Finance, jamais par le job
/// nocturne lui-même : §13.7 du cahier des charges interdit tout envoi de masse automatique — le
/// calcul (GenerateDebtorReminderBatchesCommand) et l'envoi sont deux actes délibérément séparés.
/// </summary>
public record SendDebtorReminderBatchCommand(Guid BatchId) : IRequest<SendDebtorReminderBatchResult>;

/// <summary>Mêmes conventions de comptage que SendDuesReminderSmsCommand : "mis en file", pas "envoyé".</summary>
public record SendDebtorReminderBatchResult(int QueuedCount, int SkippedCount, string? FirstSkipReason);
````


#### `src/SamaEcole.Application/Finance/Commands/SendDebtorReminderBatch/SendDebtorReminderBatchCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BusinessRuleException = SamaEcole.Application.Common.Exceptions.BusinessRuleException;

namespace SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;

public class SendDebtorReminderBatchCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    ISmsDispatcher smsDispatcher,
    TimeProvider timeProvider,
    ILogger<SendDebtorReminderBatchCommandHandler> logger)
    : IRequestHandler<SendDebtorReminderBatchCommand, SendDebtorReminderBatchResult>
{
    public async Task<SendDebtorReminderBatchResult> Handle(
        SendDebtorReminderBatchCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter borne déjà à l'école courante : un lot d'une autre école est
        // structurellement introuvable ici.
        var batch = await dbContext.DebtorReminderBatches
            .SingleOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot de relance introuvable : {request.BatchId}");

        if (batch.Status != DebtorReminderBatchStatus.Draft)
        {
            throw new BusinessRuleException(
                "Ce lot de relance a déjà été envoyé ou écarté : impossible de l'envoyer une seconde fois.");
        }

        var items = await (
            from i in dbContext.DebtorReminderBatchItems
            where i.DebtorReminderBatchId == batch.Id
            join s in dbContext.Students.AsNoTracking() on i.StudentId equals s.Id
            select new { Item = i, s.FullName })
            .ToListAsync(cancellationToken);

        var queuedCount = 0;
        var skippedCount = 0;
        string? firstSkipReason = null;

        foreach (var row in items)
        {
            var body =
                $"Rappel : la scolarité de {row.FullName} présente un reliquat de "
                + $"{row.Item.RemainingBalance:N0} FCFA, en retard de {row.Item.DaysOverdue} jour(s). "
                + "Merci de régulariser auprès de l'établissement.";

            var outcome = await smsDispatcher.DispatchAsync(
                new SmsDispatchRequest(schoolId, row.Item.GuardianPhone, body, SmsTrigger.DuesReminder, row.Item.StudentId),
                cancellationToken);

            row.Item.SmsMessageId = outcome.MessageId;

            if (outcome.IsQueued)
            {
                queuedCount++;
            }
            else
            {
                skippedCount++;
                firstSkipReason ??= outcome.Reason;
            }
        }

        batch.Status = DebtorReminderBatchStatus.Sent;
        batch.SentByUserId = actorId;
        batch.SentAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Lot de relance {BatchId} envoyé pour l'établissement {SchoolId} : {Queued} mis en file, {Skipped} écarté(s).",
            batch.Id, schoolId, queuedCount, skippedCount);

        return new SendDebtorReminderBatchResult(queuedCount, skippedCount, firstSkipReason);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/DismissDebtorReminderBatch/DismissDebtorReminderBatchCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;

/// <summary>
/// POST /finance/dues-reminder-batches/{id}/dismiss — écarte un lot BROUILLON sans envoyer aucun SMS
/// (Étape 5), ex. « ces familles ont déjà régularisé en espèces, la relance n'a plus lieu d'être ».
/// Jamais supprimé physiquement (règle #6) : le lot reste consultable, à l'état Dismissed.
/// </summary>
public record DismissDebtorReminderBatchCommand(Guid BatchId) : IRequest;
````


#### `src/SamaEcole.Application/Finance/Commands/DismissDebtorReminderBatch/DismissDebtorReminderBatchCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using BusinessRuleException = SamaEcole.Application.Common.Exceptions.BusinessRuleException;

namespace SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;

public class DismissDebtorReminderBatchCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<DismissDebtorReminderBatchCommand>
{
    public async Task Handle(DismissDebtorReminderBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await dbContext.DebtorReminderBatches
            .SingleOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot de relance introuvable : {request.BatchId}");

        if (batch.Status != DebtorReminderBatchStatus.Draft)
        {
            throw new BusinessRuleException(
                "Ce lot de relance a déjà été envoyé ou écarté : impossible de l'écarter une seconde fois.");
        }

        batch.Status = DebtorReminderBatchStatus.Dismissed;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDuesNotice/GetDuesNoticeQuery.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDuesNotice;

public record GetDuesNoticeQuery(Guid EnrollmentId) : IRequest<DuesNoticeDto>;

/// <summary>
/// Sommation pour impayés — lettre formelle de mise en demeure adressée au tuteur d'un élève dont au
/// moins une échéance est en retard (règle #4 : ce document ne modifie rien, il constate le solde déjà
/// posé par le Secrétariat à l'inscription). Refuse d'émettre une sommation pour une inscription à
/// jour : ce n'est pas un document que la Finance peut produire "au cas où", il constate un état réel.
/// </summary>
public record DuesNoticeDto(
    Guid EnrollmentId,
    string NoticeNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    string? GuardianName,
    string? GuardianPhone,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    IReadOnlyList<DuesNoticeInstallmentDto> OverdueInstallments,
    DateTimeOffset IssuedAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public record DuesNoticeInstallmentDto(string Designation, decimal Amount, DateOnly DueDate);

public class GetDuesNoticeQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetDuesNoticeQuery, DuesNoticeDto>
{
    public async Task<DuesNoticeDto> Handle(GetDuesNoticeQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            join sch in dbContext.Schools.AsNoTracking() on e.SchoolId equals sch.Id
            where e.Id == request.EnrollmentId && e.Status != EnrollmentStatus.Cancelled
            select new
            {
                Enrollment = e,
                Student = s,
                ClassroomName = c.Name,
                SchoolYearLabel = y.Label,
                SchoolYearStart = y.StartDate,
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription introuvable ou annulée : {request.EnrollmentId}");

        var lines = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == row.Enrollment.Id)
            .OrderBy(l => l.IsRecurring)
            .ThenBy(l => l.Designation)
            .ToListAsync(cancellationToken);

        // Un plan personnalisé ACTIF (Étape 5) prime sur la synthèse mensuelle uniforme — même règle
        // que GetStudentBalanceQuery, pour que le montant dû et les échéances en retard restent
        // cohérents entre les deux écrans.
        var customInstallments = await dbContext.FeeInstallmentPlans.AsNoTracking()
            .Where(p => p.EnrollmentId == row.Enrollment.Id && p.Status == FeeInstallmentPlanStatus.Active)
            .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id))
            .OrderBy(i => i.SequenceNo)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        // Comportement historique préservé : une échéance PARTIELLEMENT payée mais dont la date est
        // dépassée reste une sommation légitime pour son reliquat — l'état "Overdue" du calculateur
        // exige lui un paiement nul, plus strict (voir GetStudentBalanceQuery), donc on filtre ici sur
        // le fait brut (reliquat positif + date dépassée) plutôt que sur ce statut.
        var overdue = InstallmentScheduleCalculator
            .Calculate(row.Enrollment.AmountPaid, today, lines, row.SchoolYearStart, customInstallments)
            .Where(c => c.RemainingDue > 0 && c.DueDate < today)
            .Select(c => new DuesNoticeInstallmentDto(c.Label, c.RemainingDue, c.DueDate))
            .ToList();

        if (overdue.Count == 0)
        {
            throw new BusinessRuleException(
                "Aucune échéance en retard sur cette inscription : impossible d'émettre une sommation pour un compte à jour.");
        }

        return new DuesNoticeDto(
            row.Enrollment.Id,
            $"SOM-{row.Enrollment.Id.ToString()[..8].ToUpperInvariant()}",
            row.Student.Matricule,
            row.Student.FullName,
            row.ClassroomName,
            row.SchoolYearLabel,
            row.Student.GuardianName,
            row.Student.GuardianPhone,
            row.Enrollment.TotalDue,
            row.Enrollment.AmountPaid,
            row.Enrollment.TotalDue - row.Enrollment.AmountPaid,
            overdue,
            timeProvider.GetUtcNow(),
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Phone,
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDuesNoticePdf/GetDuesNoticePdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetDuesNoticePdf;

public record GetDuesNoticePdfQuery(Guid EnrollmentId) : IRequest<DuesNoticePdfResult>, IAuditableRequest;

public record DuesNoticePdfResult(byte[] Content, string NoticeNumber);

public class GetDuesNoticePdfQueryHandler(
    ISender mediator,
    IDuesNoticePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetDuesNoticePdfQuery, DuesNoticePdfResult>
{
    public async Task<DuesNoticePdfResult> Handle(GetDuesNoticePdfQuery request, CancellationToken cancellationToken)
    {
        var notice = await mediator.Send(new GetDuesNoticeQuery(request.EnrollmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(notice.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={notice.NoticeNumber}");

        return new DuesNoticePdfResult(pdfGenerator.Generate(notice, logo, qrCode), notice.NoticeNumber);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFinancialCommitment/CreateFinancialCommitmentCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;

public record CreateFinancialCommitmentCommand(Guid EnrollmentId, decimal Amount, DateOnly DueDate, string Terms)
    : IRequest<Guid>;
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFinancialCommitment/CreateFinancialCommitmentCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;

/// <summary>
/// Enregistre un engagement financier (reconnaissance de dette) — une trace écrite de l'accord, PAS
/// un encaissement : ne touche jamais <see cref="Enrollment.AmountPaid"/> ni <see cref="Enrollment.TotalDue"/>
/// (AGENTS.md règle #4, seul un <c>Payment</c> réel modifie le solde).
/// </summary>
public class CreateFinancialCommitmentCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateFinancialCommitmentCommand, Guid>
{
    public async Task<Guid> Handle(CreateFinancialCommitmentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var enrollmentExists = await _context.Enrollments.FindAsync(new object[] { request.EnrollmentId }, cancellationToken);
        if (enrollmentExists == null)
            throw new NotFoundException(nameof(Enrollment), request.EnrollmentId.ToString());

        var commitment = new FinancialCommitment
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            EnrollmentId = request.EnrollmentId,
            Amount = request.Amount,
            DueDate = request.DueDate,
            Terms = request.Terms
        };

        _context.FinancialCommitments.Add(commitment);
        await _context.SaveChangesAsync(cancellationToken);

        return commitment.Id;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFinancialCommitment/CreateFinancialCommitmentCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;

public class CreateFinancialCommitmentCommandValidator : AbstractValidator<CreateFinancialCommitmentCommand>
{
    public CreateFinancialCommitmentCommandValidator()
    {
        RuleFor(v => v.EnrollmentId).NotEmpty();
        RuleFor(v => v.Amount).GreaterThan(0);
        RuleFor(v => v.DueDate).NotEmpty();
        RuleFor(v => v.Terms).NotEmpty().MaximumLength(2000);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetFinancialCommitment/GetFinancialCommitmentQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFinancialCommitment;

public record GetFinancialCommitmentQuery(Guid FinancialCommitmentId) : IRequest<FinancialCommitmentDto>;

/// <summary>
/// Engagement financier imprimable — document RH/Finance interne (comme le reçu) : pas de bandeau
/// M.E.N., établissement en tant que créancier.
/// </summary>
public record FinancialCommitmentDto(
    Guid FinancialCommitmentId,
    string CommitmentNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    decimal Amount,
    DateOnly DueDate,
    string Terms,
    DateTimeOffset SignedAt,
    string? GuardianName,
    string? GuardianPhone,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetFinancialCommitmentQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFinancialCommitmentQuery, FinancialCommitmentDto>
{
    public async Task<FinancialCommitmentDto> Handle(GetFinancialCommitmentQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from fc in dbContext.FinancialCommitments.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on fc.EnrollmentId equals e.Id
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            join sch in dbContext.Schools.AsNoTracking() on fc.SchoolId equals sch.Id
            where fc.Id == request.FinancialCommitmentId
            select new
            {
                fc.Id,
                fc.Amount,
                fc.DueDate,
                fc.Terms,
                fc.CreatedAt,
                s.Matricule,
                StudentFullName = s.FullName,
                s.GuardianName,
                s.GuardianPhone,
                ClassroomName = c.Name,
                SchoolYearLabel = y.Label,
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Engagement financier introuvable : {request.FinancialCommitmentId}");

        return new FinancialCommitmentDto(
            row.Id,
            $"ENG-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.ClassroomName,
            row.SchoolYearLabel,
            row.Amount,
            row.DueDate,
            row.Terms,
            row.CreatedAt,
            row.GuardianName,
            row.GuardianPhone,
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Phone,
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetFinancialCommitmentPdf/GetFinancialCommitmentPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFinancialCommitmentPdf;

public record GetFinancialCommitmentPdfQuery(Guid FinancialCommitmentId) : IRequest<FinancialCommitmentPdfResult>, IAuditableRequest;

public record FinancialCommitmentPdfResult(byte[] Content, string CommitmentNumber);

public class GetFinancialCommitmentPdfQueryHandler(
    ISender mediator,
    IFinancialCommitmentPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetFinancialCommitmentPdfQuery, FinancialCommitmentPdfResult>
{
    public async Task<FinancialCommitmentPdfResult> Handle(GetFinancialCommitmentPdfQuery request, CancellationToken cancellationToken)
    {
        var commitment = await mediator.Send(new GetFinancialCommitmentQuery(request.FinancialCommitmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(commitment.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={commitment.CommitmentNumber}");

        return new FinancialCommitmentPdfResult(pdfGenerator.Generate(commitment, logo, qrCode), commitment.CommitmentNumber);
    }
}
````


#### `src/SamaEcole.Infrastructure/Finance/DebtorAgingHostedService.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Infrastructure.Multitenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Finance;

/// <summary>
/// Calcule chaque jour, POUR CHAQUE ÉCOLE, les lots de relance de débiteurs (Étape 5). Ne contient
/// AUCUNE règle métier — seulement l'itération des écoles et la garantie qu'un échec sur une école
/// n'empêche pas les suivantes ni ne tue le service (même parti pris que SmsQueueHostedService).
///
/// École par école sous TenantProvider.RunAsSchoolAsync, avec un scope DI et un DbContext NEUFS à
/// CHAQUE itération : hors requête HTTP il n'existe aucun JWT, donc aucun tenant ambient — c'est
/// l'override qui en tient lieu, et il exige une connexion neuve pour que
/// TenantConnectionInterceptor repositionne correctement `app.current_school_id` (voir la doc de
/// RunAsSchoolAsync). Partager une connexion entre deux écoles sous cet override romprait
/// l'isolation multi-tenant (AGENTS.md règle #2) — c'est le risque principal de ce mécanisme, couvert
/// par un test d'isolation dédié (Category=MultiTenant).
/// </summary>
public class DebtorAgingHostedService(
    IServiceScopeFactory scopeFactory,
    DebtorAgingSettings settings,
    ILogger<DebtorAgingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Calcul quotidien des lots de relance de débiteurs démarré (toutes les {Interval}).",
            settings.Interval);

        using var timer = new PeriodicTimer(settings.Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Le service ne meurt jamais d'un tour raté : base injoignable, panne réseau, tout est
                // retenté au tour suivant — sans quoi plus aucun brouillon ne serait jamais calculé.
                logger.LogError(ex, "Tour de calcul des lots de relance en échec ; reprise au tour suivant.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("Calcul quotidien des lots de relance de débiteurs arrêté.");
    }

    private async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        List<Guid> schoolIds;
        using (var listScope = scopeFactory.CreateScope())
        {
            var listDbContext = listScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            // `schools` n'est pas une table tenant (elle DÉFINIT le tenant) : la lire sans SchoolId
            // ambient est normal, même lecture que SendSubscriptionReminderCommandHandler.
            schoolIds = await listDbContext.Schools.AsNoTracking()
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);
        }

        foreach (var schoolId in schoolIds)
        {
            try
            {
                await TenantProvider.RunAsSchoolAsync(schoolId, async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
                    var created = await mediator.Send(new GenerateDebtorReminderBatchesCommand(), stoppingToken);
                    return created;
                });
            }
            catch (Exception ex)
            {
                // Isolation par école : un établissement dont le calcul échoue ne doit jamais bloquer
                // celui des autres.
                logger.LogError(ex, "Calcul des lots de relance en échec pour l'établissement {SchoolId}.", schoolId);
            }
        }
    }
}
````


### 6.8 Décaissements (dépenses) et contre-écritures
* **Création** : motif (≤ 255), catégorie, montant > 0, moyen, date, bénéficiaire (≤ 150), justificatif (URL du document téléversé), `VatRate` facultatif ∈ [0;1] ; `VatAmount` calculé et **figé**. Invalide le cache dashboard.
* **« Suppression »** (`DELETE /finance/disbursements/{id}`) = **contre-écriture** : une nouvelle ligne `Amount = −original`, `VatAmount = −original`, motif préfixé « Annulation — », datée **du jour de l'annulation**, `ReversalOfId = original`. Refus si l'original est déjà une contre-écriture (`DISBURSEMENT_IS_REVERSAL`) ou déjà contre-passé (`DISBURSEMENT_ALREADY_REVERSED`). L'index unique sur `ReversalOfId` tranche deux annulations simultanées (409).
* **Liste** (`GetDisbursements`) : filtres période/catégorie ; chaque ligne porte `IsReversed` et `ReversalOfId` pour l'affichage (« barré », « contre-écriture »).


#### `src/SamaEcole.Application/Features/Disbursements/CreateDisbursementCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;

using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Features.Disbursements;

//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record CreateDisbursementCommand(
    string Reason,
    DisbursementCategory Category,
    decimal Amount,
    PaymentMethod PaymentMethod,
    DateOnly Date,
    string Beneficiary,
    string? ReceiptUrl,

    /// <summary>
    /// Taux de TVA (fraction, ex. 0.18) payé au fournisseur si cette dépense est assujettie, ou null
    /// (défaut) sinon — ex. Salaires n'est jamais assujettie. Déclaré explicitement par la Finance,
    /// jamais déduit automatiquement d'une catégorie.
    /// </summary>
    decimal? VatRate = null) : IRequest<Guid>;

public class CreateDisbursementCommandValidator : AbstractValidator<CreateDisbursementCommand>
{
    public CreateDisbursementCommandValidator()
    {
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(255);
        RuleFor(v => v.Category).IsInEnum();
        RuleFor(v => v.Amount).GreaterThan(0);
        RuleFor(v => v.PaymentMethod).IsInEnum();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Beneficiary).NotEmpty().MaximumLength(150);
        RuleFor(v => v.VatRate)
            .InclusiveBetween(0m, 1m).WithMessage("Le taux de TVA doit être compris entre 0 et 1 (ex. 0.18 pour 18 %).")
            .When(v => v.VatRate.HasValue);
    }
}

public class CreateDisbursementCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider,
    IKpiCacheService kpiCache) : IRequestHandler<CreateDisbursementCommand, Guid>
{
    public async Task<Guid> Handle(CreateDisbursementCommand request, CancellationToken cancellationToken)
    {
        var disbursement = new Disbursement
        {
            SchoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required."),
            Reason = request.Reason,
            Category = request.Category,
            Amount = request.Amount,
            VatRate = request.VatRate,
            VatAmount = VatCalculator.ComputeVatAmount(request.Amount, request.VatRate),
            PaymentMethod = request.PaymentMethod,
            Date = request.Date,
            Beneficiary = request.Beneficiary,
            ReceiptUrl = request.ReceiptUrl
        };

        context.Disbursements.Add(disbursement);
        await context.SaveChangesAsync(cancellationToken);

        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return disbursement.Id;
    }
}
````


#### `src/SamaEcole.Application/Features/Disbursements/DeleteDisbursementCommand.cs`

````csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Disbursements;

/// <summary>
/// DELETE /api/v1/finance/disbursements/{id} — ANNULE un décaissement par une contre-écriture.
///
/// Un décaissement est une écriture comptable (il alimente le solde réel, la trésorerie et la TVA déductible) :
/// il n'est ni supprimé physiquement ni masqué par une suppression logique (conception soft delete 2026-10-01
/// §3.3). La route et le verbe restent ceux de l'écran existant ; l'effet est une NOUVELLE ligne, aux montants
/// opposés, qui référence l'écriture d'origine. Tous les agrégats (somme des montants, TVA) s'annulent donc
/// d'eux-mêmes, et l'historique reste complet.
/// </summary>
//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record DeleteDisbursementCommand(Guid Id) : IRequest;

public class DeleteDisbursementCommandHandler(
    IApplicationDbContext context,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<DeleteDisbursementCommand>
{
    public const string AlreadyReversedCode = "DISBURSEMENT_ALREADY_REVERSED";
    public const string CannotReverseReversalCode = "DISBURSEMENT_IS_REVERSAL";

    public async Task Handle(DeleteDisbursementCommand request, CancellationToken cancellationToken)
    {
        var original = await context.Disbursements.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Disbursement), request.Id);

        if (original.ReversalOfId is not null)
        {
            throw new BusinessRuleException(
                "Cette écriture est déjà une annulation : elle ne peut pas être annulée à son tour.",
                CannotReverseReversalCode);
        }

        if (await context.Disbursements.AnyAsync(d => d.ReversalOfId == original.Id, cancellationToken))
        {
            throw new BusinessRuleException("Ce décaissement a déjà été annulé.", AlreadyReversedCode);
        }

        const string prefix = "Annulation — ";
        var reason = prefix + original.Reason;
        if (reason.Length > 255) reason = reason[..255];

        context.Disbursements.Add(new Disbursement
        {
            SchoolId = original.SchoolId,
            Reason = reason,
            Category = original.Category,
            Amount = -original.Amount,
            VatRate = original.VatRate,
            VatAmount = -original.VatAmount,
            PaymentMethod = original.PaymentMethod,
            // La contre-écriture est datée du jour de l'annulation : la période déjà déclarée n'est pas réécrite.
            Date = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            Beneficiary = original.Beneficiary,
            ReversalOfId = original.Id
        });

        // Deux annulations simultanées : l'index unique sur ReversalOfId refuse la seconde (traduit en 409).
        await context.SaveChangesAsync(cancellationToken);

        // CreateDisbursementCommand invalide déjà ce même cache à la création : sans ce miroir,
        // TotalDisbursements/RealBalance du dashboard Finance resteraient faux jusqu'à 7 minutes.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
    }
}
````


#### `src/SamaEcole.Application/Features/Disbursements/GetDisbursementsQuery.cs`

````csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

using SamaEcole.Domain.Enums;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Disbursements;

//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record GetDisbursementsQuery(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    DisbursementCategory? Category = null) : IRequest<List<DisbursementDto>>;

public class DisbursementDto
{
    public Guid Id { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public string Beneficiary { get; init; } = string.Empty;
    public string? ReceiptUrl { get; init; }

    /// <summary>Non null pour une contre-écriture : identifiant du décaissement qu'elle annule.</summary>
    public Guid? ReversalOfId { get; init; }

    /// <summary>Vrai si une contre-écriture annule déjà ce décaissement.</summary>
    public bool IsReversed { get; init; }
}

public class GetDisbursementsQueryHandler(IApplicationDbContext context) 
    : IRequestHandler<GetDisbursementsQuery, List<DisbursementDto>>
{
    public async Task<List<DisbursementDto>> Handle(GetDisbursementsQuery request, CancellationToken cancellationToken)
    {
        var query = context.Disbursements.AsNoTracking();

        if (request.StartDate.HasValue)
            query = query.Where(d => d.Date >= request.StartDate.Value);
            
        if (request.EndDate.HasValue)
            query = query.Where(d => d.Date <= request.EndDate.Value);
            
        if (request.Category.HasValue)
            query = query.Where(d => d.Category == request.Category.Value);

        return await query
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .Select(d => new DisbursementDto
            {
                Id = d.Id,
                Reason = d.Reason,
                Category = d.Category.ToString(),
                Amount = d.Amount,
                PaymentMethod = d.PaymentMethod.ToString(),
                Date = d.Date,
                Beneficiary = d.Beneficiary,
                ReceiptUrl = d.ReceiptUrl,
                ReversalOfId = d.ReversalOfId,
                IsReversed = context.Disbursements.Any(r => r.ReversalOfId == d.Id)
            })
            .ToListAsync(cancellationToken);
    }
}
````


### 6.9 Paie
**Contrats** (`CreateEmployeeContract`, `UpdateEmployeeContract`, `CloseEmployeeContract`, `GetEmployeeContracts`)
* Lien à **exactement une** personne : enseignant, utilisateur, ou **personnel sans compte** (nom obligatoire ; unicité sur le nom, insensible à la casse, parmi les contrats actifs).
* `Permanent` ⇒ `BaseSalary > 0` ; `Vacataire` ⇒ `HourlyRate > 0`.
* `Update` : change rémunération/régime d'un contrat **actif** ; ligne d'historique append-only avec **motif obligatoire** ; verrou `xmin`. Le **type** n'est pas modifiable (on clôture puis on recrée).
* `Close` : pose `EndDate`, irréversible, historisé ; plus de fiche possible ensuite ; l'attestation de travail reste éditable.

**Heures des vacataires** (`CreateTeacherHourRecord`, `GetTeacherHourRecords`, `GetHourRecordSheet` + PDF, `GetSuggestedPayrollHours`) : détail jour par jour, fiche de suivi à faire signer ; la **suggestion** d'heures pré-remplit la génération de la fiche (rapprochée de l'emploi du temps), toujours modifiable ; **la Direction valide** — pas de paie entièrement automatique (choix de contrôle assumé).

**Fiche de paie**
* `GenerateFichePaie` : par contrat/mois/année, **à la demande** ; refus si une fiche non annulée existe ou si le contrat est clôturé ; calcul par `PayrollCalculator` (sans retenues si `ApplyStatutoryDeductions = false`).
* `CancelFichePaie` / `RegenerateFichePaie` : motif ≥ 5 car. sans HTML, `RowVersion` obligatoire ; **refus si déclaration fiscale existante** pour la période ; contre-passe le décaissement si la fiche était payée (sauf s'il l'a déjà été à la main) ; la régénération est **atomique** (annule → sauve → insère la remplaçante → `ReplacedByFichePaieId`), reprend les heures de l'ancienne fiche et la prime de transport du contrat, et refuse un contrat clôturé.
* `PayFichePaie` : crée le décaissement `Salaires` (net, sans TVA, bénéficiaire = identité de l'employé tronquée à 150/255) et marque la fiche `PaidAt/PaidBy/PaidWith/DisbursementId` en **un seul `SaveChanges`**.
* PDF A4 : bulletin ; s'il est payé, il sert de **reçu de paiement du salaire** ; annulé : « FICHE ANNULÉE — SANS VALEUR » + motif ; sans retenues : « Paie sans retenues ».
* **Attestation de travail** : éditable à tout moment, y compris contrat clôturé ; date d'engagement = `EmployeeContract.CreatedAt` (aucune date d'embauche distincte).


#### `src/SamaEcole.Application/Finance/Services/PayrollCalculator.cs`

````csharp
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

public static class PayrollCalculator
{
    // Taux standards au Sénégal (à confirmer/modifier si besoin)
    public const decimal IpresEmployeeRate = 0.056m;
    public const decimal IpresEmployerRate = 0.084m;
    public const decimal CssEmployerRate = 0.07m;
    public const decimal CfmEmployerRate = 0.03m; // Contribution Forfaitaire à la charge de l'Employeur (souvent appelée CFCE)
    public const decimal BrsRate = 0.05m; // BRS (retenue à la source) simplifié
    
    // Plafonds (Exemples, souvent mis à jour)
    public const decimal IpresCeiling = 360000m; // Plafond mensuel IPRES RG
    public const decimal CssCeiling = 63000m;    // Plafond mensuel CSS Prestations Familiales

    public static FichePaie CalculateFichePaie(
        Guid schoolId, 
        Guid contractId, 
        int month, 
        int year, 
        decimal baseSalary, 
        decimal hourlyRate, 
        decimal hoursWorked,
        decimal transportAllowance,
        bool applyDeductions = true)
    {
        // Calcul du Brut
        decimal grossSalary = hourlyRate > 0
            ? hoursWorked * hourlyRate
            : baseSalary;

        // Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) : aucune cotisation, aucune charge, aucune
        // retenue — la personne reçoit exactement son salaire brut plus sa prime de transport. Les deux régimes
        // coexistent dans une même école : c'est le contrat de la personne qui choisit (EmployeeContract.ApplyStatutoryDeductions).
        if (!applyDeductions)
        {
            return new FichePaie
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolId,
                EmployeeContractId = contractId,
                Month = month,
                Year = year,
                HoursWorked = hoursWorked,
                GrossSalary = grossSalary,
                TransportAllowance = transportAllowance,
                IpresEmployee = 0m,
                IpresEmployer = 0m,
                CssEmployer = 0m,
                Vrs = 0m,
                Brs = 0m,
                NetSalary = grossSalary + transportAllowance,
                DeductionsApplied = false
            };
        }

        // Base IPRES plafonnée
        decimal ipresBase = Math.Min(grossSalary, IpresCeiling);
        
        // Base CSS plafonnée
        decimal cssBase = Math.Min(grossSalary, CssCeiling);

        decimal ipresEmployee = ipresBase * IpresEmployeeRate;
        decimal ipresEmployer = ipresBase * IpresEmployerRate;
        decimal cssEmployer = cssBase * CssEmployerRate;
        
        // CFCE/VRS (Employeur)
        decimal vrs = grossSalary * CfmEmployerRate; 

        // BRS (Employé) - Retenue à la source
        // Calcul très simplifié: base imposable = Brut - IPRES employé
        decimal taxableBase = grossSalary - ipresEmployee;
        decimal brs = taxableBase > 0 ? taxableBase * BrsRate : 0m; 

        // Calcul du net
        decimal netSalary = grossSalary + transportAllowance - ipresEmployee - brs;

        return new FichePaie
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            EmployeeContractId = contractId,
            Month = month,
            Year = year,
            HoursWorked = hoursWorked,
            GrossSalary = grossSalary,
            TransportAllowance = transportAllowance,
            IpresEmployee = ipresEmployee,
            IpresEmployer = ipresEmployer,
            CssEmployer = cssEmployer,
            Vrs = vrs,
            Brs = brs,
            NetSalary = netSalary
        };
    }
}
````


#### `src/SamaEcole.Application/Finance/Services/FichePaieCancellation.cs`

````csharp
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

/// <summary>
/// Annulation d'une fiche de paie (Volume 1 §14.5), partagée par « Annuler » et « Annuler et régénérer ».
/// La fiche n'est ni supprimée ni réécrite : elle est marquée annulée (auteur, date, motif). Si elle avait été
/// payée, le décaissement « Salaires » correspondant est CONTRE-PASSÉ (écriture inverse datée du jour), jamais
/// supprimé — même principe que <c>DeleteDisbursementCommand</c>. L'appelant sauvegarde.
/// </summary>
public static class FichePaieCancellation
{
    public const string PeriodDeclaredCode = "PAYSLIP_PERIOD_DECLARED";

    /// <returns>Vrai si une contre-écriture de trésorerie a été ajoutée (cache KPI à invalider).</returns>
    public static async Task<bool> ApplyAsync(
        IApplicationDbContext context,
        FichePaie fiche,
        string reason,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (fiche.CancelledAt is not null)
        {
            throw new BusinessRuleException("Cette fiche de paie est déjà annulée.");
        }

        // Une déclaration fiscale déjà générée a figé les totaux IPRES/CSS/BRS de la période : annuler une de ses
        // fiches la rendrait fausse sans que personne ne le voie. La déclaration est un document déposé.
        if (await context.TaxeDeclarations.AnyAsync(t => t.Month == fiche.Month && t.Year == fiche.Year, cancellationToken))
        {
            throw new BusinessRuleException(
                $"Une déclaration fiscale existe déjà pour {fiche.Month:00}/{fiche.Year} : cette fiche ne peut plus être annulée.",
                PeriodDeclaredCode);
        }

        var reversed = false;
        if (fiche.DisbursementId is Guid disbursementId)
        {
            var original = await context.Disbursements.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == disbursementId, cancellationToken);

            // Si la Finance a déjà annulé ce décaissement à la main depuis l'écran des dépenses, ne pas le
            // contre-passer une seconde fois (la trésorerie serait faussée dans l'autre sens).
            if (original is not null
                && original.ReversalOfId is null
                && !await context.Disbursements.AnyAsync(d => d.ReversalOfId == original.Id, cancellationToken))
            {
                const string prefix = "Annulation — ";
                var label = prefix + original.Reason;
                if (label.Length > 255) label = label[..255];

                context.Disbursements.Add(new Disbursement
                {
                    SchoolId = original.SchoolId,
                    Reason = label,
                    Category = original.Category,
                    Amount = -original.Amount,
                    VatRate = original.VatRate,
                    VatAmount = -original.VatAmount,
                    PaymentMethod = original.PaymentMethod,
                    Date = DateOnly.FromDateTime(now.UtcDateTime),
                    Beneficiary = original.Beneficiary,
                    ReversalOfId = original.Id
                });
                reversed = true;
            }
        }

        fiche.CancelledAt = now;
        fiche.CancelledByUserId = actorId;
        fiche.CancellationReason = reason.Trim();
        return reversed;
    }
}
````


#### `src/SamaEcole.Application/Finance/Services/EmployeeIdentity.cs`

````csharp
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

/// <summary>
/// Nom et fonction de la personne liée à un contrat (Volume 1 §14.1 et §14.7) : un enseignant, un utilisateur
/// de la plateforme, ou un membre du personnel SANS compte saisi sur le contrat. Le contrat doit être chargé avec
/// <c>Teacher</c> et <c>User</c> ; pour une requête LINQ-to-SQL, répliquer l'expression telle quelle.
/// </summary>
public static class EmployeeIdentity
{
    public const string DefaultExternalRole = "Personnel";

    public static string FullName(EmployeeContract c) =>
        c.Teacher?.FullName ?? c.User?.FullName ?? c.ExternalFullName ?? "—";

    public static string Role(EmployeeContract c) =>
        c.Teacher is not null ? "Enseignant"
        : c.User is not null ? c.User.Role.ToString()
        : string.IsNullOrWhiteSpace(c.ExternalJobTitle) ? DefaultExternalRole : c.ExternalJobTitle!;
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateEmployeeContract/CreateEmployeeContractCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.CreateEmployeeContract;

/// <summary>
/// Contrat de travail — prérequis à la génération d'une fiche de paie (<c>GenerateFichePaieCommand</c>).
/// Lié à EXACTEMENT une personne : un enseignant (<see cref="TeacherId"/>), OU un membre du personnel
/// non-enseignant compte utilisateur de la plateforme (<see cref="UserId"/>, ex. Secrétariat,
/// Surveillant, Directeur), OU un membre du personnel SANS compte (<see cref="ExternalFullName"/> : gardien,
/// cuisinier, agent d'entretien… Volume 1 §14.7), jamais deux à la fois ni aucun.
/// </summary>
public record CreateEmployeeContractCommand(
    Guid? TeacherId,
    Guid? UserId,
    ContractType Type,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    PayoutMethod PayoutMethod = PayoutMethod.Cash,
    string? PayoutAccountReference = null,
    string? ExternalFullName = null,
    string? ExternalJobTitle = null,
    string? ExternalPhone = null,
    bool ApplyStatutoryDeductions = true) : IRequest<Guid>;

public class CreateEmployeeContractCommandValidator : AbstractValidator<CreateEmployeeContractCommand>
{
    public CreateEmployeeContractCommandValidator()
    {
        RuleFor(v => v)
            .Must(v => new[] { v.TeacherId.HasValue, v.UserId.HasValue, !string.IsNullOrWhiteSpace(v.ExternalFullName) }.Count(x => x) == 1)
            .WithMessage("Le contrat doit être lié à un enseignant, à un utilisateur OU à une personne sans compte (nom saisi), jamais plusieurs ni aucun.");
        RuleFor(v => v.ExternalFullName).MaximumLength(150);
        RuleFor(v => v.ExternalJobTitle).MaximumLength(100);
        RuleFor(v => v.ExternalPhone).MaximumLength(30);

        RuleFor(v => v.Type).IsInEnum();
        RuleFor(v => v.BaseSalary).GreaterThanOrEqualTo(0);
        RuleFor(v => v.HourlyRate).GreaterThanOrEqualTo(0);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0);
        RuleFor(v => v.PayoutMethod).IsInEnum();
        RuleFor(v => v.PayoutAccountReference).MaximumLength(50);

        // Cohérence avec PayrollCalculator : un salaire se calcule sur HourlyRate s'il est renseigné,
        // sinon sur BaseSalary — un contrat Permanent sans BaseSalary ou Vacataire sans HourlyRate
        // produirait donc silencieusement une fiche de paie à 0.
        RuleFor(v => v.BaseSalary)
            .GreaterThan(0)
            .When(v => v.Type == ContractType.Permanent)
            .WithMessage("Un contrat Permanent doit porter un salaire de base.");
        RuleFor(v => v.HourlyRate)
            .GreaterThan(0)
            .When(v => v.Type == ContractType.Vacataire)
            .WithMessage("Un contrat Vacataire doit porter un taux horaire.");
    }
}

public class CreateEmployeeContractCommandHandler(IApplicationDbContext context, ITenantProvider tenantProvider)
    : IRequestHandler<CreateEmployeeContractCommand, Guid>
{
    public async Task<Guid> Handle(CreateEmployeeContractCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

        if (request.TeacherId.HasValue)
        {
            var teacherExists = await context.Teachers.AnyAsync(t => t.Id == request.TeacherId.Value, cancellationToken);
            if (!teacherExists)
            {
                throw new NotFoundException(nameof(Teacher), request.TeacherId.Value);
            }
        }
        else if (request.UserId.HasValue)
        {
            var userExists = await context.Users.AnyAsync(u => u.Id == request.UserId!.Value, cancellationToken);
            if (!userExists)
            {
                throw new NotFoundException(nameof(User), request.UserId!.Value);
            }
        }

        else
        {
            // Personnel sans compte : pas de clé d'unicité naturelle (contrairement à TeacherId/UserId), donc un
            // garde-fou sur le nom, sans quoi un double clic ou un doublon de saisie créerait deux contrats actifs.
            var name = request.ExternalFullName!.Trim();
            var duplicate = await context.EmployeeContracts.AnyAsync(
                c => c.TeacherId == null && c.UserId == null && c.EndDate == null
                     && c.ExternalFullName != null && c.ExternalFullName.ToLower() == name.ToLower(),
                cancellationToken);
            if (duplicate)
            {
                throw new BusinessRuleException($"Un contrat actif existe déjà pour « {name} ».");
            }
        }

        var contract = new EmployeeContract
        {
            SchoolId = schoolId,
            TeacherId = request.TeacherId,
            UserId = request.UserId,
            Type = request.Type,
            BaseSalary = request.BaseSalary,
            HourlyRate = request.HourlyRate,
            TransportAllowance = request.TransportAllowance,
            ApplyStatutoryDeductions = request.ApplyStatutoryDeductions,
            PayoutMethod = request.PayoutMethod,
            PayoutAccountReference = string.IsNullOrWhiteSpace(request.PayoutAccountReference) ? null : request.PayoutAccountReference.Trim(),
            ExternalFullName = Clean(request.ExternalFullName),
            ExternalJobTitle = Clean(request.ExternalJobTitle),
            ExternalPhone = Clean(request.ExternalPhone)
        };

        context.EmployeeContracts.Add(contract);
        await context.SaveChangesAsync(cancellationToken);

        return contract.Id;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateEmployeeContract/UpdateEmployeeContractCommand.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;

/// <summary>
/// PATCH /finance/employee-contracts/{id} — Volume 1 §14.1. Change les termes d'un contrat encore
/// ACTIF (salaire de base, taux horaire, prime de transport) : la seule voie prévue pour une
/// augmentation de salaire ou une révision du taux horaire, puisque <see cref="EmployeeContract"/> ne
/// se recrée jamais pour la même personne tant que son contrat courant n'est pas clôturé (index unique
/// TeacherId/UserId, migration AddEmployeeContractLifecycle).
///
/// <see cref="ContractType"/> n'est volontairement PAS modifiable ici : un changement de nature de
/// contrat (Permanent -> Vacataire) est un événement RH assez rare et structurant pour justifier une
/// clôture puis un nouveau contrat, plutôt qu'une bascule silencieuse qui romprait la cohérence de
/// l'historique (un PreviousType différent du NewType aurait fallu tracer, ce que ce ticket ne demande pas).
/// </summary>
public record UpdateEmployeeContractCommand(
    Guid ContractId,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    string Reason,
    uint RowVersion,
    PayoutMethod PayoutMethod = PayoutMethod.Cash,
    string? PayoutAccountReference = null,
    bool? ApplyStatutoryDeductions = null) : IRequest<UpdateEmployeeContractResult>, IAuditableRequest;

public record UpdateEmployeeContractResult(
    Guid ContractId,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    PayoutMethod PayoutMethod,
    string? PayoutAccountReference,
    uint RowVersion,
    bool ApplyStatutoryDeductions = true);

public class UpdateEmployeeContractCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateEmployeeContractCommand, UpdateEmployeeContractResult>
{
    public async Task<UpdateEmployeeContractResult> Handle(
        UpdateEmployeeContractCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser un
        // contrat d'une autre école renvoie 404, jamais une modification silencieuse.
        var contract = await dbContext.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat {request.ContractId} introuvable.");

        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException("Ce contrat est clôturé : ses termes ne peuvent plus être modifiés.");
        }

        // Cohérence avec PayrollCalculator (même garde qu'à la création, CreateEmployeeContractCommandValidator) :
        // un Permanent sans salaire de base ou un Vacataire sans taux horaire produirait silencieusement
        // une fiche de paie à 0. Vérifié ICI, pas dans un Validator stateless, puisque Type n'est connu
        // qu'après lecture du contrat existant.
        if (contract.Type == ContractType.Permanent && request.BaseSalary <= 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.BaseSalary), "Un contrat Permanent doit porter un salaire de base.")
            ]);
        }

        if (contract.Type == ContractType.Vacataire && request.HourlyRate <= 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.HourlyRate), "Un contrat Vacataire doit porter un taux horaire.")
            ]);
        }

        var normalizedReference = string.IsNullOrWhiteSpace(request.PayoutAccountReference)
            ? null
            : request.PayoutAccountReference.Trim();

        // Régime de la paie : non fourni (null) = inchangé, pour qu'un client qui ne connaît pas ce champ ne rebascule
        // jamais un contrat « sans retenues » vers « avec retenues » en renvoyant le reste du formulaire.
        var newDeductionsRegime = request.ApplyStatutoryDeductions ?? contract.ApplyStatutoryDeductions;

        // Rien ne change : ne pas écrire, sans quoi l'historique se remplirait d'entrées vides et le
        // verrou optimiste n'aurait rien à arbitrer.
        if (contract.BaseSalary == request.BaseSalary
            && contract.HourlyRate == request.HourlyRate
            && contract.TransportAllowance == request.TransportAllowance
            && contract.PayoutMethod == request.PayoutMethod
            && contract.PayoutAccountReference == normalizedReference
            && contract.ApplyStatutoryDeductions == newDeductionsRegime)
        {
            return new UpdateEmployeeContractResult(
                contract.Id, contract.BaseSalary, contract.HourlyRate, contract.TransportAllowance,
                contract.PayoutMethod, contract.PayoutAccountReference, request.RowVersion,
                contract.ApplyStatutoryDeductions);
        }

        // Cœur du verrou optimiste : la version LUE PAR LE CLIENT devient la valeur d'origine attendue
        // du jeton xmin (AGENTS.md règle #5). Si le contrat a changé en base depuis l'affichage,
        // SaveChangesAsync lève une ConcurrencyConflictException -> 409.
        dbContext.SetOriginalConcurrencyToken(contract, request.RowVersion);

        // L'entrée EmployeeContractHistory ne trace QUE les montants (AGENTS.md règle #4) : un
        // changement de PayoutMethod seul ne doit jamais produire une ligne Previous==New qui
        // affirmerait un changement de rémunération qui n'a pas eu lieu.
        var amountsChanged = contract.BaseSalary != request.BaseSalary
            || contract.HourlyRate != request.HourlyRate
            || contract.TransportAllowance != request.TransportAllowance;

        if (amountsChanged)
        {
            dbContext.EmployeeContractHistories.Add(new EmployeeContractHistory
            {
                SchoolId = schoolId,
                EmployeeContractId = contract.Id,
                ChangeType = EmployeeContractChangeType.Amended,
                PreviousBaseSalary = contract.BaseSalary,
                PreviousHourlyRate = contract.HourlyRate,
                PreviousTransportAllowance = contract.TransportAllowance,
                NewBaseSalary = request.BaseSalary,
                NewHourlyRate = request.HourlyRate,
                NewTransportAllowance = request.TransportAllowance,
                EndDate = null,
                Reason = request.Reason,
                ChangedByUserId = actorId,
                ChangedAt = timeProvider.GetUtcNow()
            });
        }

        contract.BaseSalary = request.BaseSalary;
        contract.HourlyRate = request.HourlyRate;
        contract.TransportAllowance = request.TransportAllowance;
        contract.PayoutMethod = request.PayoutMethod;
        contract.PayoutAccountReference = normalizedReference;
        // Ne touche jamais aux fiches déjà éditées (elles portent leur propre instantané, FichePaie.DeductionsApplied) :
        // le nouveau régime s'applique aux fiches générées ou régénérées à partir de maintenant.
        contract.ApplyStatutoryDeductions = newDeductionsRegime;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Nouveau jeton après écriture : PostgreSQL réécrit xmin à tout UPDATE — on le relit pour que
        // le client puisse enchaîner une seconde modification sans recharger toute la grille.
        var newRowVersion = await dbContext.EmployeeContracts
            .AsNoTracking()
            .Where(c => c.Id == contract.Id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateEmployeeContractResult(
            contract.Id, contract.BaseSalary, contract.HourlyRate, contract.TransportAllowance,
            contract.PayoutMethod, contract.PayoutAccountReference, newRowVersion, contract.ApplyStatutoryDeductions);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateEmployeeContract/UpdateEmployeeContractCommandValidator.cs`

````csharp
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;

public class UpdateEmployeeContractCommandValidator : AbstractValidator<UpdateEmployeeContractCommand>
{
    public UpdateEmployeeContractCommandValidator()
    {
        RuleFor(v => v.ContractId).NotEmpty();
        RuleFor(v => v.BaseSalary).GreaterThanOrEqualTo(0);
        RuleFor(v => v.HourlyRate).GreaterThanOrEqualTo(0);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0);

        // Même exigence que ChangeUserStatusCommand (JGK-A05) et UpdateClassFee : un motif vide ou
        // « ok » ne documente rien pour l'historique de rémunération.
        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CloseEmployeeContract/CloseEmployeeContractCommand.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CloseEmployeeContract;

/// <summary>
/// POST /finance/employee-contracts/{id}/close — Volume 1 §14.1 : « un contrat n'est jamais supprimé
/// physiquement… il est clôturé à une date, et reste consultable pour l'historique de paie et les
/// attestations ». Ne supprime donc RIEN (AGENTS.md règle #6) : pose <see cref="EmployeeContract.EndDate"/>.
///
/// Une fois clôturé, un contrat ne peut plus générer de nouvelle fiche de paie (voir
/// GenerateFichePaieCommandHandler) — la dernière fiche due doit être générée AVANT cet appel, et la
/// clôture ne peut pas être annulée (pas de « rouvrir » : si la personne revient, un nouveau contrat se
/// crée, l'index unique TeacherId/UserId l'autorise désormais puisqu'il exclut les contrats clôturés).
/// </summary>
public record CloseEmployeeContractCommand(
    Guid ContractId, DateOnly EndDate, string Reason, uint RowVersion) : IRequest<CloseEmployeeContractResult>, IAuditableRequest;

public record CloseEmployeeContractResult(Guid ContractId, DateOnly EndDate);

public class CloseEmployeeContractCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<CloseEmployeeContractCommand, CloseEmployeeContractResult>
{
    public async Task<CloseEmployeeContractResult> Handle(
        CloseEmployeeContractCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var contract = await dbContext.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat {request.ContractId} introuvable.");

        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException("Ce contrat est déjà clôturé.");
        }

        dbContext.SetOriginalConcurrencyToken(contract, request.RowVersion);

        contract.EndDate = request.EndDate;

        dbContext.EmployeeContractHistories.Add(new EmployeeContractHistory
        {
            SchoolId = schoolId,
            EmployeeContractId = contract.Id,
            ChangeType = EmployeeContractChangeType.Closed,
            // Les termes ne changent PAS à la clôture — Previous == New, seule EndDate est nouvelle.
            PreviousBaseSalary = contract.BaseSalary,
            PreviousHourlyRate = contract.HourlyRate,
            PreviousTransportAllowance = contract.TransportAllowance,
            NewBaseSalary = contract.BaseSalary,
            NewHourlyRate = contract.HourlyRate,
            NewTransportAllowance = contract.TransportAllowance,
            EndDate = request.EndDate,
            Reason = request.Reason,
            ChangedByUserId = actorId,
            ChangedAt = timeProvider.GetUtcNow()
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseEmployeeContractResult(contract.Id, request.EndDate);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CloseEmployeeContract/CloseEmployeeContractCommandValidator.cs`

````csharp
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CloseEmployeeContract;

public class CloseEmployeeContractCommandValidator : AbstractValidator<CloseEmployeeContractCommand>
{
    public CloseEmployeeContractCommandValidator()
    {
        RuleFor(v => v.ContractId).NotEmpty();
        RuleFor(v => v.EndDate).NotEmpty();

        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetEmployeeContracts/GetEmployeeContractsQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetEmployeeContracts;

public record GetEmployeeContractsQuery : IRequest<List<EmployeeContractDto>>;

public record EmployeeContractDto(
    Guid Id,
    Guid? TeacherId,
    Guid? UserId,
    string EmployeeFullName,
    string EmployeeRole,
    string Type,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    string PayoutMethod,
    string? PayoutAccountReference,
    DateOnly? EndDate,
    uint RowVersion,
    string? ExternalPhone = null,
    bool ApplyStatutoryDeductions = true);

public class GetEmployeeContractsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEmployeeContractsQuery, List<EmployeeContractDto>>
{
    public async Task<List<EmployeeContractDto>> Handle(GetEmployeeContractsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.EmployeeContracts.AsNoTracking()
            .Include(c => c.Teacher)
            .Include(c => c.User)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new EmployeeContractDto(
                c.Id,
                c.TeacherId,
                c.UserId,
                c.Teacher != null ? c.Teacher.FullName : c.User != null ? c.User.FullName : c.ExternalFullName ?? "—",
                c.Teacher != null ? "Enseignant" : c.User != null ? c.User.Role.ToString() : (c.ExternalJobTitle ?? "Personnel"),
                c.Type.ToString(),
                c.BaseSalary,
                c.HourlyRate,
                c.TransportAllowance,
                c.PayoutMethod.ToString(),
                c.PayoutAccountReference,
                c.EndDate,
                EF.Property<uint>(c, "xmin"),
                c.ExternalPhone,
                c.ApplyStatutoryDeductions))
            .ToListAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateTeacherHourRecord/CreateTeacherHourRecordCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.CreateTeacherHourRecord;

/// <summary>
/// Un jour d'heures effectuées par un contrat Vacataire — alimente la fiche de suivi imprimable
/// (<c>GetHourRecordSheetQuery</c>), pas le calcul de paie lui-même (<c>PayrollCalculator</c> reste
/// alimenté par <c>PayslipDto.HoursWorked</c>, saisi séparément à la génération de la fiche : relier
/// automatiquement les deux est hors périmètre de ce ticket).
/// </summary>
public record CreateTeacherHourRecordCommand(Guid EmployeeContractId, DateOnly Date, decimal Hours, string? Note)
    : IRequest<Guid>;

public class CreateTeacherHourRecordCommandValidator : AbstractValidator<CreateTeacherHourRecordCommand>
{
    public CreateTeacherHourRecordCommandValidator()
    {
        RuleFor(v => v.EmployeeContractId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Hours).GreaterThan(0).LessThanOrEqualTo(24);
        RuleFor(v => v.Note).MaximumLength(500);
    }
}

public class CreateTeacherHourRecordCommandHandler(IApplicationDbContext context, ITenantProvider tenantProvider)
    : IRequestHandler<CreateTeacherHourRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateTeacherHourRecordCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var contract = await context.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.EmployeeContractId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeContract), request.EmployeeContractId.ToString());

        if (contract.Type != ContractType.Vacataire)
        {
            throw new BusinessRuleException(
                "Le suivi horaire ne concerne que les contrats Vacataire — ce contrat est Permanent.");
        }

        var record = new TeacherHourRecord
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            EmployeeContractId = request.EmployeeContractId,
            Date = request.Date,
            Hours = request.Hours,
            Note = request.Note
        };

        context.TeacherHourRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetTeacherHourRecords/GetTeacherHourRecordsQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTeacherHourRecords;

public record TeacherHourRecordDto(Guid Id, DateOnly Date, decimal Hours, string? Note);

public record GetTeacherHourRecordsQuery(Guid EmployeeContractId, int? Month, int? Year) : IRequest<List<TeacherHourRecordDto>>;

public class GetTeacherHourRecordsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTeacherHourRecordsQuery, List<TeacherHourRecordDto>>
{
    public async Task<List<TeacherHourRecordDto>> Handle(GetTeacherHourRecordsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TeacherHourRecords.AsNoTracking()
            .Where(r => r.EmployeeContractId == request.EmployeeContractId);

        if (request.Month.HasValue)
        {
            query = query.Where(r => r.Date.Month == request.Month.Value);
        }

        if (request.Year.HasValue)
        {
            query = query.Where(r => r.Date.Year == request.Year.Value);
        }

        return await query
            .OrderBy(r => r.Date)
            .Select(r => new TeacherHourRecordDto(r.Id, r.Date, r.Hours, r.Note))
            .ToListAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetHourRecordSheet/GetHourRecordSheetQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetHourRecordSheet;

public record GetHourRecordSheetQuery(Guid EmployeeContractId, int Month, int Year) : IRequest<HourRecordSheetDto>;

/// <summary>
/// Fiche de suivi des heures d'un Vacataire pour un mois donné — document RH interne (comme le
/// bulletin de paie), pas de bandeau M.E.N.
/// </summary>
public record HourRecordSheetDto(
    Guid EmployeeContractId,
    string SheetNumber,
    string EmployeeFullName,
    int Month,
    int Year,
    decimal HourlyRate,
    IReadOnlyList<HourRecordLineDto> Lines,
    decimal TotalHours,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public record HourRecordLineDto(DateOnly Date, decimal Hours, string? Note);

public class GetHourRecordSheetQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetHourRecordSheetQuery, HourRecordSheetDto>
{
    public async Task<HourRecordSheetDto> Handle(GetHourRecordSheetQuery request, CancellationToken cancellationToken)
    {
        var contract = await (
            from c in dbContext.EmployeeContracts.AsNoTracking()
            join sch in dbContext.Schools.AsNoTracking() on c.SchoolId equals sch.Id
            where c.Id == request.EmployeeContractId
            select new
            {
                c.Id,
                c.HourlyRate,
                EmployeeFullName = c.Teacher != null ? c.Teacher.FullName : c.User != null ? c.User.FullName : c.ExternalFullName ?? "—",
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat introuvable : {request.EmployeeContractId}");

        var lines = await dbContext.TeacherHourRecords.AsNoTracking()
            .Where(r => r.EmployeeContractId == request.EmployeeContractId
                && r.Date.Month == request.Month && r.Date.Year == request.Year)
            .OrderBy(r => r.Date)
            .Select(r => new HourRecordLineDto(r.Date, r.Hours, r.Note))
            .ToListAsync(cancellationToken);

        return new HourRecordSheetDto(
            contract.Id,
            $"FH-{contract.Id.ToString()[..8].ToUpperInvariant()}-{request.Year}{request.Month:D2}",
            contract.EmployeeFullName,
            request.Month,
            request.Year,
            contract.HourlyRate,
            lines,
            lines.Sum(l => l.Hours),
            contract.School.Name,
            contract.School.Address,
            ReceiptCity.FromAddress(contract.School.Address),
            contract.School.Phone,
            contract.School.Ninea,
            contract.School.LogoUrl);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetHourRecordSheetPdf/GetHourRecordSheetPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetHourRecordSheetPdf;

public record GetHourRecordSheetPdfQuery(Guid EmployeeContractId, int Month, int Year) : IRequest<HourRecordSheetPdfResult>, IAuditableRequest;

public record HourRecordSheetPdfResult(byte[] Content, string SheetNumber);

public class GetHourRecordSheetPdfQueryHandler(
    ISender mediator,
    IHourRecordSheetPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetHourRecordSheetPdfQuery, HourRecordSheetPdfResult>
{
    public async Task<HourRecordSheetPdfResult> Handle(GetHourRecordSheetPdfQuery request, CancellationToken cancellationToken)
    {
        var sheet = await mediator.Send(new GetHourRecordSheetQuery(request.EmployeeContractId, request.Month, request.Year), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(sheet.SchoolLogoUrl, cancellationToken);

        return new HourRecordSheetPdfResult(pdfGenerator.Generate(sheet, logo), sheet.SheetNumber);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetSuggestedPayrollHours/GetSuggestedPayrollHoursQuery.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;

/// <summary>
/// GET /finance/employee-contracts/{id}/suggested-hours — ticket JGK-K01. Agrège
/// <c>TeacherHourRecord</c> (le registre d'émargement du vacataire, Volume 1 §14.3) et le rapproche
/// de <c>ScheduleSlot</c> (l'emploi du temps planifié) pour PRÉ-REMPLIR le champ <c>HoursWorked</c>
/// de <c>GenerateFichePaieCommand</c> côté client.
///
/// N'écrit RIEN et ne modifie en rien <c>GenerateFichePaieCommand</c> : c'est une suggestion
/// consultative, la Direction reste seule à valider (ou corriger) le nombre d'heures avant de
/// soumettre la commande existante — arbitrage explicite, Volume 1 §14.3 amendé. Un écart signalé ici
/// n'empêche jamais la génération de la fiche de paie.
/// </summary>
public record GetSuggestedPayrollHoursQuery(Guid EmployeeContractId, int Month, int Year) : IRequest<SuggestedPayrollHoursDto>;

public record SuggestedPayrollHoursDto(
    Guid EmployeeContractId,
    decimal SuggestedHours,
    IReadOnlyList<PayrollHoursDiscrepancyDto> Discrepancies);

/// <summary>
/// Écart, pour un jour donné, entre les heures DÉCLARÉES (TeacherHourRecord) et les heures
/// PLANIFIÉES (ScheduleSlot du même jour de la semaine). Purement informatif — voir la classe.
/// </summary>
public record PayrollHoursDiscrepancyDto(DateOnly Date, decimal DeclaredHours, decimal ScheduledHours);
````


#### `src/SamaEcole.Application/Finance/Queries/GetSuggestedPayrollHours/GetSuggestedPayrollHoursQueryHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;

public class GetSuggestedPayrollHoursQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSuggestedPayrollHoursQuery, SuggestedPayrollHoursDto>
{
    public async Task<SuggestedPayrollHoursDto> Handle(
        GetSuggestedPayrollHoursQuery request, CancellationToken cancellationToken)
    {
        var contract = await dbContext.EmployeeContracts.AsNoTracking()
            .Where(c => c.Id == request.EmployeeContractId)
            .Select(c => new { c.Id, c.TeacherId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat {request.EmployeeContractId} introuvable.");

        // Même source que la fiche de suivi imprimable (GetHourRecordSheetQuery) : les deux doivent
        // toujours raconter le même total, faute de quoi la Direction verrait deux chiffres
        // contradictoires pour le même mois.
        var declaredByDate = await dbContext.TeacherHourRecords.AsNoTracking()
            .Where(r => r.EmployeeContractId == request.EmployeeContractId
                        && r.Date.Month == request.Month && r.Date.Year == request.Year)
            .GroupBy(r => r.Date)
            .Select(g => new { Date = g.Key, Hours = g.Sum(r => r.Hours) })
            .ToListAsync(cancellationToken);

        var suggestedHours = declaredByDate.Sum(d => d.Hours);

        // Un contrat lié à un UserId (personnel non-enseignant) n'a pas d'emploi du temps de classe :
        // pas de rapprochement possible, seule la suggestion agrégée est renvoyée.
        if (contract.TeacherId is not { } teacherId)
        {
            return new SuggestedPayrollHoursDto(contract.Id, suggestedHours, []);
        }

        var scheduledHoursByDayOfWeek = await dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.TeacherId == teacherId)
            .GroupBy(s => s.DayOfWeek)
            .Select(g => new { DayOfWeek = g.Key, Hours = g.Sum(s => (decimal)(s.EndTime - s.StartTime).TotalHours) })
            .ToDictionaryAsync(g => g.DayOfWeek, g => g.Hours, cancellationToken);

        var discrepancies = declaredByDate
            .Select(d => new
            {
                d.Date,
                d.Hours,
                Scheduled = scheduledHoursByDayOfWeek.GetValueOrDefault(d.Date.DayOfWeek, 0m)
            })
            .Where(d => d.Hours != d.Scheduled)
            .OrderBy(d => d.Date)
            .Select(d => new PayrollHoursDiscrepancyDto(d.Date, d.Hours, d.Scheduled))
            .ToList();

        return new SuggestedPayrollHoursDto(contract.Id, suggestedHours, discrepancies);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/GenerateFichePaie/GenerateFichePaieCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Commands.GenerateFichePaie;

public record GenerateFichePaieCommand(
    Guid EmployeeContractId,
    int Month,
    int Year,
    decimal HoursWorked = 0,
    decimal TransportAllowance = 0) : IRequest<Guid>;

public class GenerateFichePaieCommandValidator : AbstractValidator<GenerateFichePaieCommand>
{
    public GenerateFichePaieCommandValidator()
    {
        RuleFor(v => v.EmployeeContractId).NotEmpty();
        RuleFor(v => v.Month).InclusiveBetween(1, 12);
        RuleFor(v => v.Year).GreaterThan(2000);
        RuleFor(v => v.HoursWorked).GreaterThanOrEqualTo(0);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0);
    }
}

public class GenerateFichePaieCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<GenerateFichePaieCommand, Guid>
{
    public async Task<Guid> Handle(GenerateFichePaieCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();
        
        // Vérifier si une fiche existe déjà pour ce mois
        var existing = await context.FichePaies
            .AnyAsync(f => f.EmployeeContractId == request.EmployeeContractId && 
                           f.Month == request.Month && 
                           f.Year == request.Year &&
                           f.CancelledAt == null, cancellationToken);
                           
        if (existing)
            throw new BusinessRuleException("Une fiche de paie existe déjà pour ce mois.");

        var contract = await context.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.EmployeeContractId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeContract), request.EmployeeContractId);

        // Volume 1 §14.1 : un contrat clôturé ne génère plus aucune nouvelle fiche de paie — la
        // dernière fiche due doit être générée AVANT la clôture (CloseEmployeeContractCommand).
        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException("Ce contrat est clôturé : impossible de générer une nouvelle fiche de paie.");
        }

        // Si le contrat est horaire (Vacataire) et qu'aucune heure n'est fournie, c'est une erreur logique,
        // mais le validateur permet 0, donc ça calculera un salaire de 0.
        // Si c'est un CDI/CDD, le salaire de base est utilisé.
        var fichePaie = PayrollCalculator.CalculateFichePaie(
            schoolId,
            contract.Id,
            request.Month,
            request.Year,
            contract.BaseSalary,
            contract.HourlyRate,
            request.HoursWorked,
            request.TransportAllowance,
            contract.ApplyStatutoryDeductions
        );

        context.FichePaies.Add(fichePaie);
        
        // Enregistrement des décaissements associés à la paie (Brut ou Net + Taxes) ?
        // Dans une implémentation avancée, la validation de la paie génèrerait un Disbursement.
        // Pour l'instant, on se contente de générer la fiche.
        
        await context.SaveChangesAsync(cancellationToken);

        return fichePaie.Id;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CancelFichePaie/CancelFichePaieCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Commands.CancelFichePaie;

/// <summary>
/// POST /finance/payroll/{id}/cancel — Volume 1 §14.5. Annule une fiche de paie erronée SANS la supprimer : elle
/// reste consultable, marquée annulée, avec auteur, date et motif. Si elle était payée, le décaissement
/// « Salaires » est contre-passé. Pour la remplacer par une fiche corrigée, voir
/// <c>RegenerateFichePaieCommand</c>.
/// </summary>
public record CancelFichePaieCommand(Guid FichePaieId, string Reason, uint RowVersion)
    : IRequest<CancelFichePaieResult>, IAuditableRequest;

public record CancelFichePaieResult(Guid FichePaieId, bool PaymentReversed);

public class CancelFichePaieCommandValidator : AbstractValidator<CancelFichePaieCommand>
{
    public CancelFichePaieCommandValidator()
    {
        RuleFor(v => v.FichePaieId).NotEmpty();
        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}

public class CancelFichePaieCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<CancelFichePaieCommand, CancelFichePaieResult>
{
    public async Task<CancelFichePaieResult> Handle(CancelFichePaieCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Filtre de tenant + RLS : viser la fiche d'une autre école renvoie 404, jamais une modification.
        var fiche = await dbContext.FichePaies.FirstOrDefaultAsync(f => f.Id == request.FichePaieId, cancellationToken)
            ?? throw new NotFoundException(nameof(FichePaie), request.FichePaieId);

        // Verrou optimiste (AGENTS.md règle #5) : la version lue par le client devient l'origine attendue de xmin.
        dbContext.SetOriginalConcurrencyToken(fiche, request.RowVersion);

        var reversed = await FichePaieCancellation.ApplyAsync(
            dbContext, fiche, request.Reason, actorId, timeProvider.GetUtcNow(), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (reversed) kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new CancelFichePaieResult(fiche.Id, reversed);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/RegenerateFichePaie/RegenerateFichePaieCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Commands.RegenerateFichePaie;

/// <summary>
/// POST /finance/payroll/{id}/regenerate — Volume 1 §14.5. Annule la fiche erronée (voir
/// <see cref="FichePaieCancellation"/>) puis en génère une NOUVELLE pour la même période, calculée sur les termes
/// ACTUELS du contrat (corriger d'abord le contrat, via sa modification). Les heures reprennent celles de la fiche
/// annulée, sauf si <see cref="HoursWorked"/> est fourni ; la prime de transport reprend celle du contrat, sauf si
/// <see cref="TransportAllowance"/> est fournie. Tout se fait dans une seule transaction : jamais d'état
/// intermédiaire « annulée sans remplaçante ».
/// </summary>
public record RegenerateFichePaieCommand(
    Guid FichePaieId,
    string Reason,
    uint RowVersion,
    decimal? HoursWorked = null,
    decimal? TransportAllowance = null) : IRequest<RegenerateFichePaieResult>, IAuditableRequest;

public record RegenerateFichePaieResult(Guid CancelledFichePaieId, Guid NewFichePaieId);

public class RegenerateFichePaieCommandValidator : AbstractValidator<RegenerateFichePaieCommand>
{
    public RegenerateFichePaieCommandValidator()
    {
        RuleFor(v => v.FichePaieId).NotEmpty();
        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
        RuleFor(v => v.HoursWorked).GreaterThanOrEqualTo(0).When(v => v.HoursWorked.HasValue);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0).When(v => v.TransportAllowance.HasValue);
    }
}

public class RegenerateFichePaieCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<RegenerateFichePaieCommand, RegenerateFichePaieResult>
{
    public async Task<RegenerateFichePaieResult> Handle(RegenerateFichePaieCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var old = await dbContext.FichePaies.FirstOrDefaultAsync(f => f.Id == request.FichePaieId, cancellationToken)
            ?? throw new NotFoundException(nameof(FichePaie), request.FichePaieId);

        var contract = await dbContext.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == old.EmployeeContractId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeContract), old.EmployeeContractId);

        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException(
                "Ce contrat est clôturé : impossible de régénérer une fiche de paie. Annulez la fiche si elle est erronée.");
        }

        dbContext.SetOriginalConcurrencyToken(old, request.RowVersion);

        var (newId, reversed) = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();

            // 1) Annulation d'abord et sauvegardée seule : l'unicité (contrat, mois, année) ne porte que sur les
            // fiches non annulées, donc la remplaçante ne peut être insérée qu'une fois l'ancienne annulée en base.
            var didReverse = await FichePaieCancellation.ApplyAsync(dbContext, old, request.Reason, actorId, now, ct);
            await dbContext.SaveChangesAsync(ct);

            // 2) Remplaçante, calculée sur les termes actuels du contrat.
            var replacement = PayrollCalculator.CalculateFichePaie(
                schoolId,
                contract.Id,
                old.Month,
                old.Year,
                contract.BaseSalary,
                contract.HourlyRate,
                request.HoursWorked ?? old.HoursWorked,
                request.TransportAllowance ?? contract.TransportAllowance,
                contract.ApplyStatutoryDeductions);

            dbContext.FichePaies.Add(replacement);
            old.ReplacedByFichePaieId = replacement.Id;
            await dbContext.SaveChangesAsync(ct);

            return (replacement.Id, didReverse);
        }, cancellationToken);

        if (reversed) kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new RegenerateFichePaieResult(old.Id, newId);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/PayFichePaie/PayFichePaieCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.PayFichePaie;

/// <summary>
/// POST /finance/payroll/{id}/pay — Volume 1 §14.6. Enregistre le règlement du salaire NET d'une fiche : crée un
/// décaissement de catégorie « Salaires » (trésorerie, comptabilité) au nom de l'employé — y compris pour une
/// personne SANS compte (gardien…) — et marque la fiche payée. Le bulletin devient alors un reçu de paiement à
/// faire signer. Une fiche ne se paie qu'une fois (un second appel est refusé, pas dupliqué) ; la corriger passe par
/// son annulation, qui contre-passe le décaissement.
/// </summary>
public record PayFichePaieCommand(Guid FichePaieId, PaymentMethod PaymentMethod, DateOnly Date, uint RowVersion)
    : IRequest<PayFichePaieResult>, IAuditableRequest;

public record PayFichePaieResult(Guid FichePaieId, Guid DisbursementId, decimal Amount);

public class PayFichePaieCommandValidator : AbstractValidator<PayFichePaieCommand>
{
    public PayFichePaieCommandValidator()
    {
        RuleFor(v => v.FichePaieId).NotEmpty();
        RuleFor(v => v.PaymentMethod).IsInEnum();
        RuleFor(v => v.Date).NotEmpty();
    }
}

public class PayFichePaieCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<PayFichePaieCommand, PayFichePaieResult>
{
    public async Task<PayFichePaieResult> Handle(PayFichePaieCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var fiche = await dbContext.FichePaies
            .Include(f => f.EmployeeContract).ThenInclude(c => c.Teacher)
            .Include(f => f.EmployeeContract).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(f => f.Id == request.FichePaieId, cancellationToken)
            ?? throw new NotFoundException(nameof(FichePaie), request.FichePaieId);

        if (fiche.CancelledAt is not null)
        {
            throw new BusinessRuleException("Cette fiche de paie est annulée : elle ne peut pas être payée.");
        }

        if (fiche.PaidAt is not null)
        {
            throw new BusinessRuleException("Cette fiche de paie a déjà été payée.");
        }

        if (fiche.NetSalary <= 0)
        {
            throw new BusinessRuleException("Le net à payer est nul : il n'y a rien à régler sur cette fiche.");
        }

        dbContext.SetOriginalConcurrencyToken(fiche, request.RowVersion);

        var name = EmployeeIdentity.FullName(fiche.EmployeeContract);
        var beneficiary = name.Length > 150 ? name[..150] : name;
        var reason = $"Salaire {fiche.Month:00}/{fiche.Year} — {name}";
        if (reason.Length > 255) reason = reason[..255];

        var disbursement = new Disbursement
        {
            SchoolId = schoolId,
            Reason = reason,
            Category = DisbursementCategory.Salaires,
            Amount = fiche.NetSalary,
            // Une rémunération n'est jamais une livraison taxable (cf. Disbursement.VatRate).
            VatRate = null,
            VatAmount = 0m,
            PaymentMethod = request.PaymentMethod,
            Date = request.Date,
            Beneficiary = beneficiary
        };

        dbContext.Disbursements.Add(disbursement);
        fiche.PaidAt = timeProvider.GetUtcNow();
        fiche.PaidByUserId = actorId;
        fiche.PaidWith = request.PaymentMethod;
        fiche.DisbursementId = disbursement.Id;

        // Un seul SaveChanges : décaissement et fiche payée sont écrits ensemble ou pas du tout.
        await dbContext.SaveChangesAsync(cancellationToken);

        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new PayFichePaieResult(fiche.Id, disbursement.Id, disbursement.Amount);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetFichePaies/GetFichePaiesQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFichePaies;

public record GetFichePaiesQuery(int? Month = null, int? Year = null) : IRequest<List<FichePaieListItemDto>>;

public record FichePaieListItemDto(
    Guid Id,
    Guid EmployeeContractId,
    string EmployeeFullName,
    int Month,
    int Year,
    decimal HoursWorked,
    decimal GrossSalary,
    decimal TransportAllowance,
    decimal IpresEmployee,
    decimal IpresEmployer,
    decimal CssEmployer,
    decimal Vrs,
    decimal Brs,
    decimal NetSalary,
    DateTimeOffset CreatedAt,
    string Status,
    DateTimeOffset? PaidAt,
    string? PaidWith,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    uint RowVersion,
    bool DeductionsApplied = true);

public class GetFichePaiesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFichePaiesQuery, List<FichePaieListItemDto>>
{
    public async Task<List<FichePaieListItemDto>> Handle(GetFichePaiesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.FichePaies.AsNoTracking()
            .Include(f => f.EmployeeContract).ThenInclude(c => c.Teacher)
            .Include(f => f.EmployeeContract).ThenInclude(c => c.User)
            .AsQueryable();

        if (request.Month.HasValue)
        {
            query = query.Where(f => f.Month == request.Month.Value);
        }

        if (request.Year.HasValue)
        {
            query = query.Where(f => f.Year == request.Year.Value);
        }

        return await query
            .OrderByDescending(f => f.Year).ThenByDescending(f => f.Month).ThenByDescending(f => f.CreatedAt)
            .Select(f => new FichePaieListItemDto(
                f.Id,
                f.EmployeeContractId,
                f.EmployeeContract.Teacher != null ? f.EmployeeContract.Teacher.FullName
                    : f.EmployeeContract.User != null ? f.EmployeeContract.User.FullName
                    : f.EmployeeContract.ExternalFullName ?? "—",
                f.Month,
                f.Year,
                f.HoursWorked,
                f.GrossSalary,
                f.TransportAllowance,
                f.IpresEmployee,
                f.IpresEmployer,
                f.CssEmployer,
                f.Vrs,
                f.Brs,
                f.NetSalary,
                f.CreatedAt,
                f.CancelledAt != null ? "Cancelled" : f.PaidAt != null ? "Paid" : "Generated",
                f.PaidAt,
                f.PaidWith != null ? f.PaidWith.ToString() : null,
                f.CancelledAt,
                f.CancellationReason,
                EF.Property<uint>(f, "xmin"),
                f.DeductionsApplied))
            .ToListAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetPayslip/GetPayslipQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPayslip;

public record GetPayslipQuery(Guid FichePaieId) : IRequest<PayslipDto>;

public record PayslipDto(
    Guid FichePaieId,
    string PayslipNumber,
    string EmployeeFullName,
    string EmployeeRole,
    string ContractType,
    int Month,
    int Year,
    decimal HoursWorked,
    decimal GrossSalary,
    decimal TransportAllowance,
    decimal IpresEmployee,
    decimal IpresEmployer,
    decimal CssEmployer,
    decimal Vrs,
    decimal Brs,
    decimal NetSalary,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolLogoUrl,
    DateTimeOffset? PaidAt = null,
    string? PaidWith = null,
    DateTimeOffset? CancelledAt = null,
    string? CancellationReason = null,
    bool DeductionsApplied = true);

public class GetPayslipQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPayslipQuery, PayslipDto>
{
    public async Task<PayslipDto> Handle(GetPayslipQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from f in dbContext.FichePaies.AsNoTracking()
            join c in dbContext.EmployeeContracts.AsNoTracking() on f.EmployeeContractId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on f.SchoolId equals sch.Id
            where f.Id == request.FichePaieId
            select new { FichePaie = f, Contract = c, School = sch })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Fiche de paie introuvable : {request.FichePaieId}");

        // Teacher/User sont chargés séparément (jointure conditionnelle peu naturelle en LINQ-to-SQL) :
        // un contrat porte l'un des trois (enseignant, utilisateur, personne sans compte), jamais plusieurs.
        string employeeFullName;
        string employeeRole;
        if (row.Contract.TeacherId.HasValue)
        {
            var teacher = await dbContext.Teachers.AsNoTracking()
                .FirstAsync(t => t.Id == row.Contract.TeacherId.Value, cancellationToken);
            employeeFullName = teacher.FullName;
            employeeRole = "Enseignant";
        }
        else if (row.Contract.UserId.HasValue)
        {
            var user = await dbContext.Users.AsNoTracking()
                .FirstAsync(u => u.Id == row.Contract.UserId.Value, cancellationToken);
            employeeFullName = user.FullName;
            employeeRole = user.Role.ToString();
        }
        else
        {
            employeeFullName = row.Contract.ExternalFullName ?? "—";
            employeeRole = string.IsNullOrWhiteSpace(row.Contract.ExternalJobTitle) ? "Personnel" : row.Contract.ExternalJobTitle!;
        }

        var f2 = row.FichePaie;

        return new PayslipDto(
            f2.Id,
            $"BP-{f2.Id.ToString()[..8].ToUpperInvariant()}",
            employeeFullName,
            employeeRole,
            row.Contract.Type.ToString(),
            f2.Month,
            f2.Year,
            f2.HoursWorked,
            f2.GrossSalary,
            f2.TransportAllowance,
            f2.IpresEmployee,
            f2.IpresEmployer,
            f2.CssEmployer,
            f2.Vrs,
            f2.Brs,
            f2.NetSalary,
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Ninea,
            row.School.LogoUrl,
            f2.PaidAt,
            f2.PaidWith?.ToString(),
            f2.CancelledAt,
            f2.CancellationReason,
            f2.DeductionsApplied);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetPayslipPdf/GetPayslipPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetPayslipPdf;

public record GetPayslipPdfQuery(Guid FichePaieId) : IRequest<PayslipPdfResult>;

public record PayslipPdfResult(byte[] Content, string PayslipNumber);

public class GetPayslipPdfQueryHandler(
    ISender mediator,
    IPayslipPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetPayslipPdfQuery, PayslipPdfResult>
{
    public async Task<PayslipPdfResult> Handle(GetPayslipPdfQuery request, CancellationToken cancellationToken)
    {
        var payslip = await mediator.Send(new GetPayslipQuery(request.FichePaieId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(payslip.SchoolLogoUrl, cancellationToken);

        return new PayslipPdfResult(pdfGenerator.Generate(payslip, logo), payslip.PayslipNumber);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetWorkCertificate/GetWorkCertificateQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetWorkCertificate;

public record GetWorkCertificateQuery(Guid ContractId) : IRequest<WorkCertificateDto>;

/// <summary>
/// Attestation de travail — document RH interne (comme le bulletin de paie, <see cref="SamaEcole.Application.Finance.Queries.GetPayslip.PayslipDto"/>) :
/// pas de bandeau M.E.N., juste l'établissement en tant qu'employeur. <see cref="SinceDate"/> est la
/// date de création du contrat (<c>EmployeeContract.CreatedAt</c>) — la seule date d'engagement connue
/// du système, aucune colonne "date d'embauche" distincte n'existe (AGENTS.md : ne jamais inventer une
/// donnée absente).
/// </summary>
public record WorkCertificateDto(
    Guid ContractId,
    string CertificateNumber,
    string EmployeeFullName,
    string EmployeeRole,
    string ContractType,
    DateTimeOffset SinceDate,
    DateTimeOffset IssuedAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetWorkCertificateQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetWorkCertificateQuery, WorkCertificateDto>
{
    public async Task<WorkCertificateDto> Handle(GetWorkCertificateQuery request, CancellationToken cancellationToken)
    {
        // Jointure explicite sur School via c.SchoolId : School n'implémente PAS ITenantEntity (elle
        // définit le tenant, elle ne lui appartient pas — voir School.cs), donc dbContext.Schools n'a
        // AUCUN filtre automatique. Lire "la" school sans cette jointure exposerait potentiellement
        // celle d'un autre établissement (AGENTS.md règle #2) : c'est l'appartenance du CONTRAT
        // (déjà borné par le Global Query Filter + RLS, lui, ITenantEntity) qui fixe l'école correcte.
        var row = await (
            from c in dbContext.EmployeeContracts.AsNoTracking()
            join sch in dbContext.Schools.AsNoTracking() on c.SchoolId equals sch.Id
            where c.Id == request.ContractId
            select new
            {
                c.Id,
                c.Type,
                c.CreatedAt,
                EmployeeFullName = c.Teacher != null ? c.Teacher.FullName : c.User != null ? c.User.FullName : c.ExternalFullName ?? "—",
                EmployeeRole = c.Teacher != null ? "Enseignant" : c.User != null ? c.User.Role.ToString() : (c.ExternalJobTitle ?? "Personnel"),
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat introuvable : {request.ContractId}");

        return new WorkCertificateDto(
            row.Id,
            $"AT-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.EmployeeFullName,
            row.EmployeeRole,
            row.Type.ToString(),
            row.CreatedAt,
            timeProvider.GetUtcNow(),
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Phone,
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetWorkCertificatePdf/GetWorkCertificatePdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetWorkCertificatePdf;

public record GetWorkCertificatePdfQuery(Guid ContractId) : IRequest<WorkCertificatePdfResult>, IAuditableRequest;

public record WorkCertificatePdfResult(byte[] Content, string CertificateNumber);

public class GetWorkCertificatePdfQueryHandler(
    ISender mediator,
    IWorkCertificatePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetWorkCertificatePdfQuery, WorkCertificatePdfResult>
{
    public async Task<WorkCertificatePdfResult> Handle(GetWorkCertificatePdfQuery request, CancellationToken cancellationToken)
    {
        var certificate = await mediator.Send(new GetWorkCertificateQuery(request.ContractId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(certificate.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={certificate.CertificateNumber}");

        return new WorkCertificatePdfResult(pdfGenerator.Generate(certificate, logo, qrCode), certificate.CertificateNumber);
    }
}
````


### 6.10 Fiscalité et TVA
* **TVA collectée** = `PaymentLedger.NetAsync(mois, p => p.VatAmount)` : la TVA d'un versement reste dans le mois d'encaissement même annulé plus tard ; l'annulation d'un versement d'un mois antérieur vient en **négatif** dans le mois où elle est faite (régularisation). Un versement `Partial` est un encaissement réel et compte.
* **TVA déductible** = Σ `VatAmount` des décaissements dont `Date` ∈ mois (contre-écritures négatives incluses).
* **Charges sociales** = agrégat des fiches **non annulées** du mois (§5.4).
* **Une seule déclaration par mois** : une seconde génération est refusée (aucune rectificative n'est implémentée — voir §13).
* PDF officiel (`TaxDeclarationDocument`) : déclarant = l'établissement, sans bandeau ministériel.


#### `src/SamaEcole.Application/Finance/Services/TaxCalculator.cs`

````csharp
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

public static class TaxCalculator
{
    public static TaxeDeclaration CalculateTaxDeclaration(
        Guid schoolId, 
        int month, 
        int year, 
        List<FichePaie> fichesPaie, 
        decimal tvaCollected, 
        decimal tvaDeductible)
    {
        // 1. Agréger les fiches de paie pour les charges sociales
        decimal totalIpresEmployee = fichesPaie.Sum(f => f.IpresEmployee);
        decimal totalIpresEmployer = fichesPaie.Sum(f => f.IpresEmployer);
        decimal totalIpres = totalIpresEmployee + totalIpresEmployer;
        
        decimal totalCss = fichesPaie.Sum(f => f.CssEmployer);
        decimal totalVrs = fichesPaie.Sum(f => f.Vrs);
        decimal totalBrs = fichesPaie.Sum(f => f.Brs);

        // 2. Calcul TVA
        decimal netTva = tvaCollected - tvaDeductible;

        // 3. Calcul total dû à l'État
        // IPRES et CSS vont aux caisses de sécurité sociale
        // VRS, BRS, TVA vont aux Impôts (VRS = CFCE, BRS = Retenue sur salaire)
        decimal totalDueToState = totalVrs + totalBrs + (netTva > 0 ? netTva : 0m);

        return new TaxeDeclaration
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            Month = month,
            Year = year,
            TotalIpres = totalIpres,
            TotalCss = totalCss,
            TotalVrs = totalVrs,
            TotalBrs = totalBrs,
            TvaCollected = tvaCollected,
            TvaDeductible = tvaDeductible,
            NetTva = netTva,
            TotalDueToState = totalDueToState
        };
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/GenerateTaxDeclaration/GenerateTaxDeclarationCommand.cs`

````csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;

public record GenerateTaxDeclarationCommand(int Month, int Year) : IRequest<Guid>;

public class GenerateTaxDeclarationCommandValidator : AbstractValidator<GenerateTaxDeclarationCommand>
{
    public GenerateTaxDeclarationCommandValidator()
    {
        RuleFor(v => v.Month).InclusiveBetween(1, 12);
        RuleFor(v => v.Year).GreaterThan(2000);
    }
}

public class GenerateTaxDeclarationCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<GenerateTaxDeclarationCommand, Guid>
{
    public async Task<Guid> Handle(GenerateTaxDeclarationCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

        // 1. Check if already generated
        var existing = await context.TaxeDeclarations
            .AnyAsync(t => t.Month == request.Month && t.Year == request.Year, cancellationToken);
            
        if (existing)
            throw new BusinessRuleException("Une déclaration fiscale existe déjà pour cette période.");

        // 2. Fetch all FichePaies for the period
        var fiches = await context.FichePaies
            .Where(f => f.Month == request.Month && f.Year == request.Year && f.CancelledAt == null)
            .ToListAsync(cancellationToken);

        // 3. TVA collectée : somme de VatAmount des encaissements du mois (Payment.VatRate/VatAmount,
        // renseignés transaction par transaction à l'encaissement — voir RecordPaymentCommandHandler).
        // Un paiement Cancelled n'a jamais représenté de l'argent réellement encaissé (même exclusion que
        // le tableau de bord Trésorerie) ; Status == Partial, lui, EST un encaissement réel, donc inclus.
        var startInclusive = new DateTimeOffset(new DateTime(request.Year, request.Month, 1), TimeSpan.Zero);
        var endExclusive = startInclusive.AddMonths(1);

        //
        // Registre daté (PaymentLedger) : la TVA d'un versement reste dans le mois où il a été encaissé, même annulé
        // plus tard (une déclaration déjà déposée ne se réécrit pas), et l'annulation d'un versement d'un mois antérieur
        // vient en négatif dans le mois où elle est faite (régularisation).
        decimal tvaCollected = await PaymentLedger.NetAsync(
            context, startInclusive, endExclusive, p => p.VatAmount, cancellationToken);

        // 4. TVA déductible : somme de VatAmount des décaissements du mois (Disbursement.VatRate/VatAmount).
        var periodStart = DateOnly.FromDateTime(startInclusive.DateTime);
        var periodEnd = DateOnly.FromDateTime(endExclusive.DateTime.AddDays(-1));

        decimal tvaDeductible = await context.Disbursements
            .Where(d => d.Date >= periodStart && d.Date <= periodEnd)
            .SumAsync(d => d.VatAmount, cancellationToken);

        // 5. Generate Declaration
        var declaration = TaxCalculator.CalculateTaxDeclaration(
            schoolId,
            request.Month,
            request.Year,
            fiches,
            tvaCollected,
            tvaDeductible
        );

        context.TaxeDeclarations.Add(declaration);
        await context.SaveChangesAsync(cancellationToken);

        return declaration.Id;
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetTaxDeclaration/GetTaxDeclarationQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTaxDeclaration;

public record GetTaxDeclarationQuery(Guid TaxDeclarationId) : IRequest<TaxDeclarationDto>;

/// <summary>
/// Déclaration fiscale mensuelle imprimable — document Finance interne (comme le bulletin de paie) :
/// pas de bandeau M.E.N., établissement en tant que déclarant.
/// </summary>
public record TaxDeclarationDto(
    Guid Id,
    string DeclarationNumber,
    int Month,
    int Year,
    decimal TotalIpres,
    decimal TotalCss,
    decimal TotalVrs,
    decimal TotalBrs,
    decimal TvaCollected,
    decimal TvaDeductible,
    decimal NetTva,
    decimal TotalDueToState,
    DateTimeOffset CreatedAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetTaxDeclarationQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTaxDeclarationQuery, TaxDeclarationDto>
{
    public async Task<TaxDeclarationDto> Handle(GetTaxDeclarationQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from t in dbContext.TaxeDeclarations.AsNoTracking()
            join sch in dbContext.Schools.AsNoTracking() on t.SchoolId equals sch.Id
            where t.Id == request.TaxDeclarationId
            select new { Declaration = t, School = sch })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Déclaration fiscale introuvable : {request.TaxDeclarationId}");

        return new TaxDeclarationDto(
            row.Declaration.Id,
            $"DECL-{row.Declaration.Id.ToString()[..8].ToUpperInvariant()}-{row.Declaration.Year}{row.Declaration.Month:D2}",
            row.Declaration.Month,
            row.Declaration.Year,
            row.Declaration.TotalIpres,
            row.Declaration.TotalCss,
            row.Declaration.TotalVrs,
            row.Declaration.TotalBrs,
            row.Declaration.TvaCollected,
            row.Declaration.TvaDeductible,
            row.Declaration.NetTva,
            row.Declaration.TotalDueToState,
            row.Declaration.CreatedAt,
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetTaxDeclarations/GetTaxDeclarationsQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTaxDeclarations;

public record GetTaxDeclarationsQuery(int? Year = null) : IRequest<List<TaxDeclarationListItemDto>>;

public record TaxDeclarationListItemDto(
    Guid Id,
    int Month,
    int Year,
    decimal TotalIpres,
    decimal TotalCss,
    decimal TotalVrs,
    decimal TotalBrs,
    decimal TvaCollected,
    decimal TvaDeductible,
    decimal NetTva,
    decimal TotalDueToState,
    DateTimeOffset CreatedAt);

public class GetTaxDeclarationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTaxDeclarationsQuery, List<TaxDeclarationListItemDto>>
{
    public async Task<List<TaxDeclarationListItemDto>> Handle(GetTaxDeclarationsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TaxeDeclarations.AsNoTracking().AsQueryable();

        if (request.Year.HasValue)
        {
            query = query.Where(t => t.Year == request.Year.Value);
        }

        return await query
            .OrderByDescending(t => t.Year).ThenByDescending(t => t.Month)
            .Select(t => new TaxDeclarationListItemDto(
                t.Id,
                t.Month,
                t.Year,
                t.TotalIpres,
                t.TotalCss,
                t.TotalVrs,
                t.TotalBrs,
                t.TvaCollected,
                t.TvaDeductible,
                t.NetTva,
                t.TotalDueToState,
                t.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetTaxDeclarationPdf/GetTaxDeclarationPdfQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetTaxDeclarationPdf;

public record GetTaxDeclarationPdfQuery(Guid TaxDeclarationId) : IRequest<TaxDeclarationPdfResult>;

public record TaxDeclarationPdfResult(byte[] Content, string DeclarationNumber);

public class GetTaxDeclarationPdfQueryHandler(
    ISender mediator,
    ITaxDeclarationPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetTaxDeclarationPdfQuery, TaxDeclarationPdfResult>
{
    public async Task<TaxDeclarationPdfResult> Handle(GetTaxDeclarationPdfQuery request, CancellationToken cancellationToken)
    {
        var declaration = await mediator.Send(new GetTaxDeclarationQuery(request.TaxDeclarationId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(declaration.SchoolLogoUrl, cancellationToken);

        return new TaxDeclarationPdfResult(pdfGenerator.Generate(declaration, logo), declaration.DeclarationNumber);
    }
}
````


### 6.11 Tableaux de bord, trésorerie, rapports
* **Dashboard Finance** (`GetFinanceDashboard`, mis en cache KPI) : encaissé jour/mois/année (net, via registre) ; solde dû et taux de recouvrement sur l'année scolaire **active** (inscriptions non annulées) ; attendu du mois (lignes ponctuelles le mois 1 + mensualités encore dues au `monthIndex`) et taux mensuel plafonné à 100 % ; 10 derniers paiements non annulés ; total des décaissements, **solde réel = encaissé de l'année − décaissements** ; décaissements par catégorie.
* **Trésorerie** (`GetTreasuryDashboard`, défaut = mois courant) : total encaissé (registre), total décaissé, position nette, **créance en cours** (solde courant des inscriptions confirmées, indépendant de la période), répartition par mode et par catégorie, 20 dernières opérations (« Encaissement », « Annulation », « Décaissement »).
* **Consolidation des revenus** (`GetRevenueConsolidation`, défaut = 1ᵉʳ janvier → aujourd'hui) : total, nombre de versements, par cycle (via `ClassroomCycle.CycleFor(level)`), par classe, par mode, par mois (ordre chronologique) ; agrégation en mémoire ; export **Excel** (`GetRevenueConsolidationExcel`, feuilles « Synthèse » / « Par cycle » / « Par classe » / « Par mode de paiement ») sur exactement la même requête.


#### `src/SamaEcole.Application/Finance/Queries/GetFinanceDashboard/GetFinanceDashboardQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFinanceDashboard;

/// <summary>
/// GET /finance/dashboard — ticket JGK-F04. Vue d'ensemble financière de l'école : ce qui a été
/// encaissé (jour/mois/année), ce qui reste dû, et la part déjà recouvrée sur l'année scolaire ACTIVE.
///
/// Lecture réservée à Directeur et Finance (pas Secrétariat) : composer le montant dû à l'inscription
/// n'est pas la même chose que voir la santé financière agrégée de l'établissement — cohérent avec la
/// séparation des rôles de la règle #4 (AGENTS.md).
/// </summary>
public record GetFinanceDashboardQuery : IRequest<FinanceDashboardDto>;

public record FinanceDashboardDto(
    decimal CollectedToday,
    decimal CollectedThisMonth,
    decimal CollectedThisYear,
    decimal OutstandingBalance,
    decimal RecoveryRate,
    IReadOnlyList<RecentPaymentDto> RecentPayments,
    decimal ExpectedThisMonth = 0m,
    decimal MonthlyRecoveryRate = 0m,
    decimal ExpectedThisYear = 0m,
    decimal YearlyRecoveryRate = 0m,
    decimal TotalDisbursements = 0m,
    decimal RealBalance = 0m,
    IReadOnlyList<DisbursementCategoryTotal>? DisbursementsByCategory = null);

public record DisbursementCategoryTotal(string Category, decimal Amount);

public record RecentPaymentDto(
    Guid PaymentId,
    string ReceiptNumber,
    string Matricule,
    string StudentFullName,
    decimal Amount,
    string Method,
    DateTimeOffset PaidAt);

public class GetFinanceDashboardQueryHandler(
    IApplicationDbContext dbContext, TimeProvider timeProvider, IKpiCacheService kpiCache)
    : IRequestHandler<GetFinanceDashboardQuery, FinanceDashboardDto>
{
    /// <summary>Combien de derniers versements affichés sous les cartes KPI (docs/design-references/dashboard-reference.jpg).</summary>
    private const int RecentPaymentsCount = 10;

    public Task<FinanceDashboardDto> Handle(GetFinanceDashboardQuery request, CancellationToken cancellationToken) =>
        kpiCache.GetOrCreateAsync(KpiCacheKeys.FinanceDashboard, ct => ComputeAsync(ct), cancellationToken);

    private async Task<FinanceDashboardDto> ComputeAsync(CancellationToken cancellationToken)
    {
        // "Aujourd'hui" vient de TimeProvider, jamais de DateTime.UtcNow en dur : les tests contrôlent
        // l'horloge exactement comme CreateEnrollmentCommandHandler.
        var now = timeProvider.GetUtcNow();
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset);

        // Un versement annulé (règle #6 : jamais de suppression physique, un Payment se corrige par
        // Status = Cancelled) n'est plus compté — mais selon le registre daté (PaymentLedger) : il sort de la période
        // où il est annulé, et l'annulation d'un versement d'une période antérieure est une ligne négative de la
        // période courante (les mois passés ne bougent pas).
        var collectedToday = await PaymentLedger.NetAsync(
            dbContext, todayStart, todayStart.AddDays(1), p => p.Amount, cancellationToken);

        var collectedThisMonth = await PaymentLedger.NetAsync(
            dbContext, monthStart, monthStart.AddMonths(1), p => p.Amount, cancellationToken);

        var collectedThisYear = await PaymentLedger.NetAsync(
            dbContext, yearStart, yearStart.AddYears(1), p => p.Amount, cancellationToken);

        // Solde dû et taux de recouvrement portent sur les inscriptions NON ANNULÉES de l'année scolaire
        // ACTIVE — le même périmètre que la caisse (GetStudentBalanceQuery), pas l'historique complet.
        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        var activeEnrollments = dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Status != EnrollmentStatus.Cancelled && activeYear != null && e.SchoolYearId == activeYear.Id);

        var totalDue = await activeEnrollments.SumAsync(e => e.TotalDue, cancellationToken);
        var totalPaid = await activeEnrollments.SumAsync(e => e.AmountPaid, cancellationToken);
        var outstandingBalance = totalDue - totalPaid;
        var recoveryRate = totalDue > 0 ? totalPaid / totalDue : 0m;

        decimal expectedThisMonth = 0m;
        if (activeYear != null)
        {
            int monthIndex = (now.Year - activeYear.StartDate.Year) * 12 + now.Month - activeYear.StartDate.Month + 1;
            if (monthIndex >= 1)
            {
                // Agrégation SQL plutôt qu'un ToListAsync + foreach : les deux conditions ne sont PAS
                // exclusives (une ligne récurrente de 1 mois au monthIndex 1 compte dans les deux sommes,
                // comme dans la boucle d'origine), donc deux SumAsync indépendants plutôt qu'un if/else.
                var relevantLines = dbContext.EnrollmentFeeLines.AsNoTracking()
                    .Where(l => dbContext.Enrollments.Any(e => e.Id == l.EnrollmentId && e.Status != EnrollmentStatus.Cancelled && e.SchoolYearId == activeYear.Id));

                var oneTimeTotal = monthIndex == 1
                    ? await relevantLines.Where(l => !l.IsRecurring || l.Months <= 1)
                        .SumAsync(l => (decimal?)l.LineTotal, cancellationToken) ?? 0m
                    : 0m;

                var recurringTotal = await relevantLines.Where(l => l.IsRecurring && l.Months >= monthIndex)
                    .SumAsync(l => (decimal?)l.UnitAmount, cancellationToken) ?? 0m;

                expectedThisMonth = oneTimeTotal + recurringTotal;
            }
        }

        var monthlyRecoveryRate = expectedThisMonth > 0 ? Math.Min(1.0m, collectedThisMonth / expectedThisMonth) : (collectedThisMonth > 0 ? 1.0m : 0m);

        var recentPayments = await (
            from p in dbContext.Payments.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on p.EnrollmentId equals e.Id
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            where p.Status != PaymentStatus.Cancelled
            orderby p.PaidAt descending
            select new RecentPaymentDto(
                p.Id, p.ReceiptNumber, s.Matricule, s.FullName, p.Amount, p.Method.ToString(), p.PaidAt))
            .Take(RecentPaymentsCount)
            .ToListAsync(cancellationToken);

        var totalDisbursements = await dbContext.Disbursements.AsNoTracking()
            .SumAsync(d => d.Amount, cancellationToken);
            
        var realBalance = collectedThisYear - totalDisbursements;

        var disbursementsByCategory = await dbContext.Disbursements.AsNoTracking()
            .GroupBy(d => d.Category)
            .Select(g => new DisbursementCategoryTotal(g.Key.ToString(), g.Sum(d => d.Amount)))
            .ToListAsync(cancellationToken);

        return new FinanceDashboardDto(
            collectedToday, collectedThisMonth, collectedThisYear,
            outstandingBalance, recoveryRate, recentPayments,
            expectedThisMonth, monthlyRecoveryRate, totalDue, recoveryRate,
            totalDisbursements, realBalance, disbursementsByCategory);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetTreasuryDashboard/GetTreasuryDashboardQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;

/// <summary>
/// Tableau de bord Trésorerie — agrège les encaissements (<c>Payments</c>, module Caisse) et les
/// décaissements (<c>Disbursements</c>) déjà existants sur une période, sans nouvelle table : la
/// trésorerie n'est pas un troisième registre de mouvements, c'est une LECTURE combinée des deux
/// registres qui existent déjà. Écriture inchangée : on encaisse toujours via Caisse/Finance, on
/// décaisse toujours via Disbursements — ce tableau ne fait qu'additionner.
/// </summary>
public record GetTreasuryDashboardQuery(DateOnly? StartDate = null, DateOnly? EndDate = null)
    : IRequest<TreasuryDashboardDto>;

public record TreasuryBreakdownItem(string Label, decimal Amount);

/// <summary>Une ligne du fil d'activité récent, encaissement ou décaissement confondus, triés par date.</summary>
public record TreasuryTransactionItem(string Kind, string Label, decimal Amount, DateOnly Date);

public record TreasuryDashboardDto(
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalCollected,
    decimal TotalDisbursed,
    decimal NetPosition,
    decimal TotalOutstandingDebt,
    IReadOnlyList<TreasuryBreakdownItem> CollectedByMethod,
    IReadOnlyList<TreasuryBreakdownItem> DisbursedByCategory,
    IReadOnlyList<TreasuryTransactionItem> RecentTransactions);

public class GetTreasuryDashboardQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTreasuryDashboardQuery, TreasuryDashboardDto>
{
    private const int RecentTransactionsLimit = 20;

    public async Task<TreasuryDashboardDto> Handle(GetTreasuryDashboardQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = request.StartDate ?? new DateOnly(today.Year, today.Month, 1);
        var endDate = request.EndDate ?? today;

        // Bornes en DateTimeOffset pour Payments.PaidAt : la fin est EXCLUSIVE (lendemain minuit) pour
        // inclure toute la journée de fin, quelle que soit l'heure de l'encaissement.
        var startInclusive = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endExclusive = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue).AddDays(1), TimeSpan.Zero);

        // Payment.Status reflète le solde de l'INSCRIPTION après ce versement (Paid = soldée, Partial =
        // reste dû), pas si l'argent de CE versement a été reçu — il l'a toujours été dès l'insertion de
        // la ligne (RecordPaymentCommandHandler). Ne retenir que Status == Paid exclurait donc tous les
        // versements par mensualités du total encaissé : seul Cancelled (paiement annulé) doit sortir.
        //
        // Registre daté (PaymentLedger) : les versements de la période, y compris ceux annulés APRÈS la période (la
        // trésorerie d'un mois clos ne change pas), et en NÉGATIF les annulations faites pendant la période pour des
        // versements antérieurs — au lieu de faire silencieusement disparaître le versement de son mois d'origine.
        var payments = await PaymentLedger.EntriesAsync(dbContext, startInclusive, endExclusive, cancellationToken);

        var disbursements = await dbContext.Disbursements.AsNoTracking()
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .Select(d => new { d.Amount, d.Category, d.Date, d.Reason })
            .ToListAsync(cancellationToken);

        // Reste à payer : solde COURANT des inscriptions confirmées, pas un flux de la période — une
        // photo de la créance encore due aujourd'hui, indépendante des dates de filtre ci-dessus.
        var totalOutstandingDebt = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Status == EnrollmentStatus.Confirmed)
            .SumAsync(e => e.TotalDue - e.AmountPaid, cancellationToken);

        var totalCollected = payments.Sum(p => p.Amount);
        var totalDisbursed = disbursements.Sum(d => d.Amount);

        var collectedByMethod = payments
            .GroupBy(p => p.Method.ToString())
            .Select(g => new TreasuryBreakdownItem(g.Key, g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var disbursedByCategory = disbursements
            .GroupBy(d => d.Category.ToString())
            .Select(g => new TreasuryBreakdownItem(g.Key, g.Sum(d => d.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var recentTransactions = payments
            .Select(p => new TreasuryTransactionItem(
                p.IsReversal ? "Annulation" : "Encaissement", p.ReceiptNumber, p.Amount, DateOnly.FromDateTime(p.At.UtcDateTime)))
            .Concat(disbursements.Select(d => new TreasuryTransactionItem("Décaissement", d.Reason, d.Amount, d.Date)))
            .OrderByDescending(t => t.Date)
            .Take(RecentTransactionsLimit)
            .ToList();

        return new TreasuryDashboardDto(
            startDate,
            endDate,
            totalCollected,
            totalDisbursed,
            totalCollected - totalDisbursed,
            totalOutstandingDebt,
            collectedByMethod,
            disbursedByCategory,
            recentTransactions);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetRevenueConsolidation/GetRevenueConsolidationQuery.cs`

````csharp
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;

/// <summary>
/// GET /finance/reports/revenue — consolidation des encaissements sur une période, ventilée par
/// CYCLE, par CLASSE et par MODE DE PAIEMENT (Espèces, Wave, Orange Money, Chèque…). Rapport
/// financier avancé : réservé aux formules Standard et Premium (<see cref="Feature.AdvancedFinancialReports"/>).
///
/// Le cycle n'est pas stocké sur la classe comme donnée saisie : il est DÉRIVÉ du niveau par
/// ClassroomCycle.CycleFor, seule source de vérité (voir CycleType). Le calculer ici plutôt que de
/// lire une colonne évite que ce rapport et les bulletins ne classent la même classe différemment.
///
/// Réservé à Directeur et Finance, comme GetFinanceDashboardQuery : c'est la santé financière
/// agrégée de l'établissement, pas une donnée de guichet.
/// </summary>
public record GetRevenueConsolidationQuery : IRequest<RevenueConsolidationDto>
{
    /// <summary>Bornes incluses. Absentes : l'année civile en cours, période la plus souvent demandée.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public record RevenueLine(string Label, decimal Amount, int PaymentCount);

/// <summary>Point de la série temporelle du graphique d'évolution — <see cref="Period"/> est le 1er du
/// mois, JAMAIS formaté en français côté serveur : le mois se traduit côté client (Intl.DateTimeFormat,
/// financial-report.js) comme le reste des dates de ce DTO (voir formatDate).</summary>
public record RevenueMonthLine(DateOnly Period, decimal Amount, int PaymentCount);

public record RevenueConsolidationDto(
    DateOnly From,
    DateOnly To,
    decimal TotalCollected,
    int PaymentCount,
    IReadOnlyList<RevenueLine> ByCycle,
    IReadOnlyList<RevenueLine> ByClassroom,
    IReadOnlyList<RevenueLine> ByPaymentMethod,
    IReadOnlyList<RevenueMonthLine> ByMonth);

public class GetRevenueConsolidationQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetRevenueConsolidationQuery, RevenueConsolidationDto>
{
    public async Task<RevenueConsolidationDto> Handle(
        GetRevenueConsolidationQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = request.From ?? new DateOnly(today.Year, 1, 1);
        var to = request.To ?? today;

        var fromInstant = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Borne HAUTE exclusive sur le lendemain plutôt qu'inclusive sur `to` : un versement encaissé
        // à 14 h le dernier jour de la période serait sinon exclu, la comparaison portant sur un
        // instant et non sur une date.
        var toExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Registre daté (PaymentLedger) : un versement annulé APRÈS la période y reste compté (un état déjà remis ne
        // change pas), un versement annulé pendant la période en sort, et l'annulation d'un versement d'une période
        // antérieure est une ligne NÉGATIVE de la période où elle est faite — sans jamais compter deux fois.
        // Le Global Query Filter borne déjà les tables au tenant courant.
        var ledger = await PaymentLedger.EntriesAsync(dbContext, fromInstant, toExclusive, cancellationToken);

        var enrollmentIds = ledger.Select(e => e.EnrollmentId).Distinct().ToList();
        var classrooms = await (
            from enrollment in dbContext.Enrollments.AsNoTracking()
            join classroom in dbContext.Classrooms.AsNoTracking()
                on enrollment.ClassroomId equals classroom.Id
            where enrollmentIds.Contains(enrollment.Id)
            select new { EnrollmentId = enrollment.Id, ClassroomName = classroom.Name, ClassroomLevel = classroom.Level })
            .ToDictionaryAsync(x => x.EnrollmentId, cancellationToken);

        var payments = ledger
            .Where(e => classrooms.ContainsKey(e.EnrollmentId))
            .Select(e => new
            {
                e.Amount,
                e.Method,
                PaidAt = e.At,
                // +1 pour un versement, −1 pour l'annulation d'un versement antérieur : le nombre de versements
                // se compense comme les montants.
                Count = e.IsReversal ? -1 : 1,
                classrooms[e.EnrollmentId].ClassroomName,
                classrooms[e.EnrollmentId].ClassroomLevel
            })
            .ToList();

        // Agrégation EN MÉMOIRE et non en SQL : le cycle se déduit du niveau via ClassroomCycle.CycleFor,
        // du code C# qu'aucun GROUP BY ne saurait exécuter. La volumétrie s'y prête — un exercice
        // compte quelques milliers de versements, pas des millions.
        var byCycle = payments
            .GroupBy(p => ClassroomCycle.CycleFor(p.ClassroomLevel))
            .Select(g => new RevenueLine(g.Key.ToString(), g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderByDescending(l => l.Amount)
            .ToList();

        var byClassroom = payments
            .GroupBy(p => p.ClassroomName)
            .Select(g => new RevenueLine(g.Key, g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderByDescending(l => l.Amount)
            .ToList();

        var byPaymentMethod = payments
            .GroupBy(p => p.Method)
            .Select(g => new RevenueLine(g.Key.ToString(), g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderByDescending(l => l.Amount)
            .ToList();

        // Seule série ordonnée CHRONOLOGIQUEMENT (pas par montant décroissant comme les autres) :
        // c'est un graphique d'évolution, pas un classement.
        var byMonth = payments
            .GroupBy(p => new DateOnly(p.PaidAt.Year, p.PaidAt.Month, 1))
            .Select(g => new RevenueMonthLine(g.Key, g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderBy(l => l.Period)
            .ToList();

        return new RevenueConsolidationDto(
            from, to,
            payments.Sum(p => p.Amount),
            payments.Sum(p => p.Count),
            byCycle, byClassroom, byPaymentMethod, byMonth);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetRevenueConsolidationExcel/GetRevenueConsolidationExcelQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetRevenueConsolidationExcel;

/// <summary>
/// GET /finance/reports/revenue/excel — le MÊME rapport que GetRevenueConsolidationQuery, au format
/// comptable (.xlsx).
///
/// Réutilise la requête de consolidation au lieu de recalculer : le fichier exporté et l'écran
/// affiché doivent porter exactement les mêmes chiffres, faute de quoi un comptable les découvrirait
/// contradictoires au pire moment.
/// </summary>
public record GetRevenueConsolidationExcelQuery : IRequest<RevenueExcelFile>
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public record RevenueExcelFile(byte[] Content, string FileName);

public class GetRevenueConsolidationExcelQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IRevenueReportExcelGenerator excelGenerator)
    : IRequestHandler<GetRevenueConsolidationExcelQuery, RevenueExcelFile>
{
    public async Task<RevenueExcelFile> Handle(
        GetRevenueConsolidationExcelQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var report = await mediator.Send(
            new GetRevenueConsolidationQuery { From = request.From, To = request.To }, cancellationToken);

        // `schools` n'est pas une table tenant : le filtre SchoolId est explicite.
        var schoolName = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        var content = excelGenerator.Generate(report, schoolName);
        var fileName = $"revenus_{report.From:yyyyMMdd}_{report.To:yyyyMMdd}.xlsx";

        return new RevenueExcelFile(content, fileName);
    }
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IRevenueReportExcelGenerator.cs`

````csharp
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Export comptable (.xlsx) de la consolidation des revenus — une feuille par axe d'analyse
/// (Cycle, Classe, Mode de paiement), plus une synthèse.
///
/// Implémentation côté Infrastructure (ClosedXML), même convention qu'
/// <see cref="IGradeSheetExcelGenerator"/> : Application ne référence aucune bibliothèque tierce.
/// </summary>
public interface IRevenueReportExcelGenerator
{
    byte[] Generate(RevenueConsolidationDto report, string schoolName);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IDebtorAgingExcelGenerator.cs`

````csharp
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Export comptable des débiteurs (Étape 5) — même contrat que IRevenueReportExcelGenerator.</summary>
public interface IDebtorAgingExcelGenerator
{
    byte[] Generate(DebtorAgingReportDto report, string schoolName);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IKpiCacheService.cs`

````csharp
namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Cache en mémoire des agrégats KPI (dashboards Finance/Directeur), scopé par établissement via
/// ITenantCacheKeyFactory. Interface côté Application pour qu'un futur remplacement par Redis
/// (IDistributedCache, prévu dès la V1 — voir ITenantCacheKeyFactory) ne touche aucun handler.
/// </summary>
public interface IKpiCacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken);

    /// <summary>Invalide une entrée précise pour l'école courante (ex. après un paiement, une présence).</summary>
    void Invalidate(string key);
}

/// <summary>Clés stables des dashboards mis en cache — jamais composées à la main dans un handler.</summary>
public static class KpiCacheKeys
{
    public const string FinanceDashboard = "finance-dashboard";
    public const string DirectorDashboard = "director-dashboard";
}
````


### 6.12 Barème : le code applicatif complet

#### `src/SamaEcole.Application/Finance/Commands/ApplyStandardFee/ApplyStandardFeeCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

/// <summary>
/// POST /api/v1/finance/fees/apply-standard — ticket JGK-F01, « Option 1 : définition globale ».
///
/// Applique un montant standard aux classes de l'établissement pour une catégorie, en un seul geste
/// (Volume 1 §7.4). C'est ce qui évite de saisir le même montant classe par classe.
///
/// <see cref="Level"/> délimite le périmètre : une école facture rarement le Primaire au même tarif
/// que le Lycée, et une remise à plat d'un cycle ne doit pas déborder sur les autres.
///
/// <see cref="OverwriteExisting"/> arbitre le sort des lignes DÉJÀ définies, à l'intérieur du
/// périmètre uniquement :
///   - false (défaut) : on ne remplit que les classes encore vierges. Les exceptions déjà ajustées
///     (Option 2) sont PRÉSERVÉES — rejouer un standard ne doit pas effacer un réglage fin.
///   - true : on réaligne aussi les classes existantes sur le montant standard (remise à plat
///     assumée). Chaque écrasement est historisé (ancien → nouveau).
/// </summary>
public record ApplyStandardFeeCommand : IRequest<ApplyStandardFeeResult>
{
    public required Guid FeeCategoryId { get; init; }
    public required decimal Amount { get; init; }
    public bool OverwriteExisting { get; init; }

    /// <summary>
    /// Périmètre d'application. NULL ou vide = toutes les classes de l'établissement (comportement
    /// d'origine). Sinon, seules les classes de CE niveau sont touchées ; les autres cycles gardent
    /// leur montant, y compris avec <see cref="OverwriteExisting"/>.
    ///
    /// Comparé sans tenir compte de la casse ni des espaces de bord : le niveau est une nomenclature
    /// LIBRE saisie par l'école (voir CreateClassroomCommandValidator), pas une énumération — « Collège »
    /// et « collège » désignent le même cycle et doivent cibler les mêmes classes.
    /// </summary>
    public string? Level { get; init; }
}

/// <summary>Compte-rendu de l'opération : combien de classes créées, réalignées, laissées telles quelles.</summary>
public record ApplyStandardFeeResult(int Created, int Updated, int Skipped);
````


#### `src/SamaEcole.Application/Finance/Commands/ApplyStandardFee/ApplyStandardFeeCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

public class ApplyStandardFeeCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<ApplyStandardFeeCommand, ApplyStandardFeeResult>
{
    public async Task<ApplyStandardFeeResult> Handle(ApplyStandardFeeCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // La catégorie doit exister DANS CETTE ÉCOLE. Le Global Query Filter borne déjà la requête au
        // tenant : une catégorie d'une autre école est introuvable ici, et le contrôle vaut
        // vérification d'appartenance autant que d'existence.
        var categoryExists = await dbContext.FeeCategories
            .AnyAsync(c => c.Id == request.FeeCategoryId, cancellationToken);

        if (!categoryExists)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.FeeCategoryId),
                    "La catégorie de frais indiquée n'existe pas dans votre établissement.")
            ]);
        }

        // Périmètre d'application : tout l'établissement, ou le seul niveau ciblé. La comparaison
        // passe par lower() côté PostgreSQL — le niveau est saisi librement par l'école, « Collège »
        // et « collège » doivent désigner les mêmes classes.
        var level = request.Level?.Trim();
        var normalizedLevel = string.IsNullOrEmpty(level) ? null : level.ToLowerInvariant();

        var classroomsQuery = dbContext.Classrooms.AsQueryable();
        if (normalizedLevel is not null)
        {
            classroomsQuery = classroomsQuery.Where(c => c.Level.ToLower() == normalizedLevel);
        }

        var classroomIds = await classroomsQuery
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (classroomIds.Count == 0)
        {
            // Aucune cible : rien à facturer. On ne crée pas une catégorie « fantôme » sans classe,
            // et un niveau vide est signalé sur SON champ — sinon l'utilisateur corrigerait le montant
            // alors que c'est le périmètre qui ne désigne rien.
            throw new ValidationException([
                normalizedLevel is null
                    ? new ValidationFailure(
                        nameof(request.FeeCategoryId),
                        "Aucune classe n'est encore définie : créez vos classes avant de paramétrer les frais.")
                    : new ValidationFailure(
                        nameof(request.Level),
                        $"Aucune classe n'est rattachée au niveau « {level} ».")
            ]);
        }

        // Lignes déjà présentes pour cette catégorie DANS LE PÉRIMÈTRE, indexées par classe. Le filtre
        // sur classroomIds n'est pas qu'une optimisation : hors périmètre, aucune entité n'est même
        // chargée, donc aucun montant d'un autre niveau ne peut être modifié par mégarde. On suit ces
        // entités (pas d'AsNoTracking) : on va potentiellement modifier leur montant, et EF a besoin
        // de leur jeton xmin pour le verrou optimiste.
        var existingByClassroom = await dbContext.ClassFees
            .Where(f => f.FeeCategoryId == request.FeeCategoryId && classroomIds.Contains(f.ClassroomId))
            .ToDictionaryAsync(f => f.ClassroomId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        int created = 0, updated = 0, skipped = 0;

        // Une seule transaction : soit tout le périmètre bascule sur le standard, soit rien. Un échec
        // au milieu ne doit pas laisser la moitié des classes réalignées et l'autre non.
        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var classroomId in classroomIds)
            {
                if (!existingByClassroom.TryGetValue(classroomId, out var fee))
                {
                    // Classe encore vierge : on crée la ligne de barème et sa première entrée d'audit
                    // (ancien montant NULL = définition initiale).
                    var newFee = new ClassFee
                    {
                        SchoolId = schoolId,
                        FeeCategoryId = request.FeeCategoryId,
                        ClassroomId = classroomId,
                        Amount = request.Amount
                    };
                    dbContext.ClassFees.Add(newFee);
                    AddHistory(schoolId, newFee.Id, oldAmount: null, request.Amount, actorId, now);
                    created++;
                }
                else if (request.OverwriteExisting && fee.Amount != request.Amount)
                {
                    // Réalignement assumé d'une classe déjà définie : historisé (ancien → nouveau).
                    AddHistory(schoolId, fee.Id, fee.Amount, request.Amount, actorId, now);
                    fee.Amount = request.Amount;
                    updated++;
                }
                else
                {
                    // Exception préservée (OverwriteExisting=false), ou montant déjà identique : rien
                    // à écrire, et surtout aucune entrée d'audit vide.
                    skipped++;
                }
            }

            await dbContext.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);

        return new ApplyStandardFeeResult(created, updated, skipped);
    }

    private void AddHistory(Guid schoolId, Guid classFeeId, decimal? oldAmount, decimal newAmount, Guid actorId, DateTimeOffset now)
        => dbContext.FeeChangeHistory.Add(new FeeChangeHistory
        {
            SchoolId = schoolId,
            ClassFeeId = classFeeId,
            OldAmount = oldAmount,
            NewAmount = newAmount,
            ChangedByUserId = actorId,
            ChangedAt = now
        });
}
````


#### `src/SamaEcole.Application/Finance/Commands/ApplyStandardFee/ApplyStandardFeeCommandValidator.cs`

````csharp
using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

public class ApplyStandardFeeCommandValidator : AbstractValidator<ApplyStandardFeeCommand>
{
    public ApplyStandardFeeCommandValidator()
    {
        RuleFor(x => x.FeeCategoryId).NotEmpty();

        // Périmètre facultatif : NULL/vide = toutes les classes. Renseigné, il suit la même règle que
        // le niveau d'une classe (nomenclature LIBRE, jamais une énumération figée) — on borne donc la
        // longueur et on refuse le HTML, sans imposer de liste. Un niveau qui ne désigne aucune classe
        // est rejeté par le handler, qui seul connaît les classes de l'école.
        RuleFor(x => x.Level)
            .MaximumLength(50).NoHtml()
            .When(x => !string.IsNullOrWhiteSpace(x.Level));

        // Zéro est permis (un frais peut être offert). Négatif ne l'est jamais : on facture, on ne
        // rembourse pas via le barème. Le plafond intercepte la faute de frappe — un zéro de trop
        // transformerait 15 000 F en 150 000 F sur toutes les classes d'un coup.
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Le montant ne peut pas être négatif.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Le montant annoncé semble irréaliste.");
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateClassFee/UpdateClassFeeCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.UpdateClassFee;

/// <summary>
/// PUT /api/v1/finance/fees/{id} — ticket JGK-F01, « Option 2 : exception par classe ».
///
/// Ajuste le montant d'UNE ligne de barème. <paramref name="RowVersion"/> est le jeton de
/// concurrence (xmin) lu à l'affichage : si un autre utilisateur a modifié cette ligne entre-temps,
/// l'écriture est refusée en 409 plutôt que d'écraser en silence (AGENTS.md règle #5). Le changement
/// est historisé (ancien → nouveau).
/// </summary>
public record UpdateClassFeeCommand(Guid Id, decimal Amount, uint RowVersion) : IRequest<UpdateClassFeeResult>;

public record UpdateClassFeeResult(Guid Id, decimal Amount, uint RowVersion);
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateClassFee/UpdateClassFeeCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.UpdateClassFee;

public class UpdateClassFeeCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateClassFeeCommand, UpdateClassFeeResult>
{
    public async Task<UpdateClassFeeResult> Handle(UpdateClassFeeCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser une
        // ligne de barème d'une autre école renvoie 404, jamais une modification silencieuse.
        var fee = await dbContext.ClassFees
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Ligne de barème {request.Id} introuvable.");

        // Montant inchangé : ne rien écrire. Réécrire polluerait l'historique d'une entrée vide, et
        // le verrou optimiste n'a rien à arbitrer puisque la valeur ne bouge pas.
        if (fee.Amount == request.Amount)
        {
            return new UpdateClassFeeResult(fee.Id, fee.Amount, request.RowVersion);
        }

        // Cœur du verrou optimiste : on impose à EF la version LUE PAR LE CLIENT comme valeur
        // d'origine du jeton xmin. Si la ligne a changé en base depuis l'affichage, l'UPDATE
        // « WHERE xmin = <version client> » ne touchera aucune ligne et SaveChangesAsync lèvera une
        // ConcurrencyConflictException → 409 (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(fee, request.RowVersion);

        var previousAmount = fee.Amount;
        fee.Amount = request.Amount;

        dbContext.FeeChangeHistory.Add(new FeeChangeHistory
        {
            SchoolId = schoolId,
            ClassFeeId = fee.Id,
            OldAmount = previousAmount,
            NewAmount = request.Amount,
            ChangedByUserId = actorId,
            ChangedAt = timeProvider.GetUtcNow()
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Nouveau jeton après écriture : PostgreSQL a réécrit xmin (tout UPDATE le fait). On le relit
        // pour que le client puisse enchaîner une seconde modification sans recharger toute la grille.
        var newRowVersion = await dbContext.ClassFees
            .AsNoTracking()
            .Where(f => f.Id == fee.Id)
            .Select(f => EF.Property<uint>(f, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateClassFeeResult(fee.Id, fee.Amount, newRowVersion);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateClassFee/UpdateClassFeeCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.UpdateClassFee;

public class UpdateClassFeeCommandValidator : AbstractValidator<UpdateClassFeeCommand>
{
    public UpdateClassFeeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Le montant ne peut pas être négatif.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Le montant annoncé semble irréaliste.");
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/DeleteClassFee/DeleteClassFeeCommand.cs`

````csharp
using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Commands.DeleteClassFee;

/// <summary>
/// DELETE /api/v1/finance/fees/{id} — supprime UNE ligne de barème (matrice d'autorisation
/// "Photoshop", volet Finance).
///
/// Toujours un SOFT DELETE (AGENTS.md règle #6), y compris quand aucun paiement n'y est lié :
/// EnrollmentFeeLine fige déjà un instantané du montant à l'inscription (voir son commentaire de
/// classe), donc rien ne dépend structurellement de cette ligne de barème pour l'historique
/// comptable — la suppression peut donc être libre, sans jamais recourir à un DELETE SQL réel. La
/// trace de la suppression elle-même vit sur DeletedAt/DeletedBy (AuditableEntity), pas dans
/// FeeChangeHistory (dédiée aux changements de MONTANT, volontairement append-only).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateClassFeeCommand (AGENTS.md
/// règle #5) — supprimer une ligne modifiée entre-temps par un autre utilisateur échoue en 409,
/// jamais un écrasement silencieux.
/// </summary>
public record DeleteClassFeeCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;
````


#### `src/SamaEcole.Application/Finance/Commands/DeleteClassFee/DeleteClassFeeCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.DeleteClassFee;

public class DeleteClassFeeCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteClassFeeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteClassFeeCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser une
        // ligne de barème d'une autre école renvoie 404, jamais une suppression silencieuse.
        var fee = await dbContext.ClassFees
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Ligne de barème {request.Id} introuvable.");

        // Même verrou optimiste que UpdateClassFeeCommandHandler (AGENTS.md règle #5) : une ligne
        // modifiée entre-temps par un autre utilisateur ne doit jamais disparaître en silence.
        dbContext.SetOriginalConcurrencyToken(fee, request.RowVersion);

        fee.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/DeleteClassFee/DeleteClassFeeCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.DeleteClassFee;

public class DeleteClassFeeCommandValidator : AbstractValidator<DeleteClassFeeCommand>
{
    public DeleteClassFeeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateFeeCategory/UpdateFeeCategoryCommand.cs`

````csharp
using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Commands.UpdateFeeCategory;

/// <summary>
/// PUT /api/v1/finance/fee-categories/{id} — bascule le caractère facultatif d'une catégorie de frais
/// (frais optionnels, Tâche 1). Seul <c>IsOptional</c> est modifiable : le nom et la récurrence restent
/// figés à la création, comme le documente <see cref="DeleteFeeCategory.DeleteFeeCategoryCommand"/>.
///
/// Rendre une catégorie optionnelle (ou obligatoire) n'a d'effet que sur les inscriptions FUTURES : les
/// lignes de frais d'une inscription existante sont un instantané figé (EnrollmentFeeLine), et le montant
/// dû d'une inscription n'est jamais recalculé (AGENTS.md règle #4).
///
/// Audité : décider quels frais les familles peuvent décliner change ce qui leur est facturé.
/// </summary>
public record UpdateFeeCategoryCommand(Guid Id, bool IsOptional) : IRequest<Unit>, IAuditableRequest;
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateFeeCategory/UpdateFeeCategoryCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.UpdateFeeCategory;

public class UpdateFeeCategoryCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateFeeCategoryCommand, Unit>
{
    public async Task<Unit> Handle(UpdateFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent la recherche à l'école courante : viser la catégorie
        // d'une autre école renvoie 404, jamais une modification silencieuse.
        var category = await dbContext.FeeCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Catégorie de frais {request.Id} introuvable.");

        // Même règle qu'à la création (CreateFeeCategoryCommandValidator), revérifiée ici parce que le
        // validateur ne voit pas l'état persisté : IsBoardingFee n'est pas dans la commande.
        if (request.IsOptional && category.IsBoardingFee)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.IsOptional),
                    "Une catégorie de pension ne peut pas être optionnelle : son inclusion se choisit avec le régime d'hébergement.")
            ]);
        }

        category.IsOptional = request.IsOptional;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/UpdateFeeCategory/UpdateFeeCategoryCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.UpdateFeeCategory;

public class UpdateFeeCategoryCommandValidator : AbstractValidator<UpdateFeeCategoryCommand>
{
    public UpdateFeeCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommand.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

/// <summary>
/// POST /api/v1/finance/fee-categories — ticket JGK-F01.
/// Le SchoolId vient du JWT, jamais du client (AGENTS.md règle #10). La liste est libre : aucune
/// catégorie n'est codée en dur, l'établissement facture ce qu'il veut (Volume 1 §7.1).
/// </summary>
public record CreateFeeCategoryCommand : IRequest<CreateFeeCategoryResult>
{
    public required string Name { get; init; }

    /// <summary>Vrai pour une mensualité (due chaque mois), faux pour un frais ponctuel.</summary>
    public bool IsRecurring { get; init; }

    /// <summary>Vrai pour une catégorie de pension (module Internat) : exclue du calcul des frais
    /// scolaires ordinaires à l'inscription, incluse seulement pour un élève Interne/Demi-pensionnaire.</summary>
    public bool IsBoardingFee { get; init; }

    /// <summary>Vrai pour un frais facultatif (uniforme, tenue de sport) que la famille coche à
    /// l'inscription. Faux par défaut : un client qui ignore le champ crée un frais obligatoire.</summary>
    public bool IsOptional { get; init; }
}

public record CreateFeeCategoryResult(Guid Id, string Name, bool IsRecurring, bool IsBoardingFee, bool IsOptional);
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

public class CreateFeeCategoryCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateFeeCategoryCommand, CreateFeeCategoryResult>
{
    public async Task<CreateFeeCategoryResult> Handle(CreateFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var name = request.Name.Trim();
        await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
            dbContext.FeeCategories, schoolId, c => c.Name == name, $"Une catégorie « {name} »", cancellationToken);

        var category = new FeeCategory
        {
            SchoolId = schoolId,
            Name = name,
            IsRecurring = request.IsRecurring,
            IsBoardingFee = request.IsBoardingFee,
            IsOptional = request.IsOptional
        };

        dbContext.FeeCategories.Add(category);

        // Deux catégories de même nom violent l'index unique : SaveChangesAsync traduit la violation
        // en ConcurrencyConflictException → 409, jamais un écrasement silencieux (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateFeeCategoryResult(category.Id, category.Name, category.IsRecurring, category.IsBoardingFee, category.IsOptional);
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommandValidator.cs`

````csharp
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

public class CreateFeeCategoryCommandValidator : AbstractValidator<CreateFeeCategoryCommand>
{
    public CreateFeeCategoryCommandValidator()
    {
        // NoHtml laisse passer « Frais d'inscription » (le MOT script n'est pas bloqué — voir SafeTextValidation).
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60).NoHtml();

        // La pension a déjà son propre choix explicite (régime d'hébergement + IncludeBoardingFee) : lui
        // ajouter une case « optionnel » créerait deux mécanismes concurrents pour la même décision.
        RuleFor(x => x.IsOptional).Equal(false)
            .When(x => x.IsBoardingFee)
            .WithMessage("Une catégorie de pension ne peut pas être optionnelle : son inclusion se choisit avec le régime d'hébergement.");
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/DeleteFeeCategory/DeleteFeeCategoryCommand.cs`

````csharp
using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Commands.DeleteFeeCategory;

/// <summary>
/// DELETE /api/v1/finance/fee-categories/{id} — supprime (soft delete) une catégorie de frais ET,
/// en cascade dans la MÊME transaction, toutes ses lignes de barème (ClassFee) associées : une
/// catégorie disparue ne doit jamais laisser de lignes orphelines encore visibles dans l'interface.
///
/// Pas de RowVersion ici, contrairement à DeleteClassFeeCommand : FeeCategory ne porte aujourd'hui
/// aucun verrou optimiste (aucun PUT n'existe sur elle — Name/IsRecurring sont figés à la création),
/// donc rien à protéger d'une modification concurrente. Un double DELETE reste sans risque : le
/// Global Query Filter rend la catégorie déjà supprimée invisible, la seconde tentative reçoit 404.
///
/// La cascade ne vérifie PAS individuellement le xmin de chaque ClassFee enfant : c'est une
/// suppression groupée assumée par celui qui la déclenche — bloquer sur le conflit isolé d'une seule
/// ligne enfant serait une friction disproportionnée pour un geste par nature global.
/// </summary>
public record DeleteFeeCategoryCommand(Guid Id) : IRequest<Unit>, IAuditableRequest;
````


#### `src/SamaEcole.Application/Finance/Commands/DeleteFeeCategory/DeleteFeeCategoryCommandHandler.cs`

````csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.DeleteFeeCategory;

public class DeleteFeeCategoryCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteFeeCategoryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser une
        // catégorie d'une autre école renvoie 404, jamais une suppression silencieuse.
        var category = await dbContext.FeeCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Catégorie de frais {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkFeeCategoryDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, FeeCategoryDeletionPolicy.Policy, request.Id, cancellationToken);

        category.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/DeleteFeeCategory/DeleteFeeCategoryCommandValidator.cs`

````csharp
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.DeleteFeeCategory;

public class DeleteFeeCategoryCommandValidator : AbstractValidator<DeleteFeeCategoryCommand>
{
    public DeleteFeeCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/RestoreFeeCategory/RestoreFeeCategoryCommand.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.RestoreFeeCategory;

/// <summary>POST /api/v1/fee-categories/{id}/restore — restaure une catégorie de frais supprimée logiquement.</summary>
public record RestoreFeeCategoryCommand(Guid Id) : IRequest<Unit>;

public class RestoreFeeCategoryCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreFeeCategoryCommand, Unit>
{
    public async Task<Unit> Handle(RestoreFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Seule la catégorie revient : les lignes de barème (class_fees) supprimées avec elle ne sont PAS
        // réactivées implicitement (conception §3.2, aucune fusion) — le Directeur les ressaisit ou les restaure.
        // Les lignes d'inscription déjà validées restent figées et ne sont pas touchées.
        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.FeeCategories, schoolId, request.Id, "Une catégorie de frais",
            c => other => other.Name == c.Name, beforeRestore: null, cancellationToken);

        return Unit.Value;
    }
}
````


#### `src/SamaEcole.Application/Finance/Commands/UnlinkFeeCategoryDependencies/UnlinkFeeCategoryDependenciesCommand.cs`

````csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.UnlinkFeeCategoryDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'une catégorie de frais (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est
/// DeleteFeeCategoryCommand, qui ne rompt jamais rien. Chaque lien est archivé ou détaché (rien n'est effacé).
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkFeeCategoryDependenciesCommand(Guid FeeCategoryId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkFeeCategoryDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkFeeCategoryDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkFeeCategoryDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, FeeCategoryDeletionPolicy.Policy, request.FeeCategoryId, actorId.ToString(), ct),
            cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Deletion/FeeCategoryDeletionPolicy.cs`

````csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Deletion;

/// <summary>
/// Règle de suppression d'une catégorie de frais (spec 2026-10-17, §6) :
///   • BLOQUÉE (donnée financière, sans option de rupture) si une inscription la facture (lignes de frais dus figées à
///     l'inscription) ou si un paiement NON ANNULÉ l'a ventilée ;
///   • les lignes du barème (ClassFee) se rompent explicitement par archivage — l'ancien comportement les archivait en
///     cascade, sans le dire.
/// Un test (DeletionDependencyCoverageTests) garantit que toute table liée à une catégorie est classée ici.
/// </summary>
public static class FeeCategoryDeletionPolicy
{
    public const string FinancialBlock =
        "Impossible de supprimer cette catégorie de frais car des inscriptions ou des paiements enregistrés l'utilisent.";

    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Owned<ClassFee>("ClassFee", "ligne du barème", "lignes du barème",
            "Archiver ces lignes du barème", db => db.ClassFees, id => f => f.FeeCategoryId == id)
    ];

    /// <summary>Tables qui référencent une catégorie et sont couvertes par la règle financière ci-dessous (pas par un lien rompable).</summary>
    public static IReadOnlySet<Type> CoveredByFinancialRule { get; } = new HashSet<Type>
    {
        typeof(EnrollmentFeeLine),
        typeof(PaymentBreakdown)
    };

    public static EntityDeletionPolicy Policy { get; } = new(
        "FeeCategory",
        id => $"Catégorie de frais {id} introuvable.",
        "Cette catégorie de frais est actuellement rattachée",
        (db, id, ct) => db.FeeCategories.AnyAsync(c => c.Id == id, ct),
        async (db, id, ct) =>
        {
            var billed = await db.EnrollmentFeeLines.AnyAsync(l => l.FeeCategoryId == id, ct);
            if (billed)
            {
                return FinancialBlock;
            }

            var paid = await (
                from b in db.PaymentBreakdowns
                join p in db.Payments on b.PaymentId equals p.Id
                where b.FeeCategoryId == id && p.Status != PaymentStatus.Cancelled
                select b.Id)
                .AnyAsync(ct);

            return paid ? FinancialBlock : null;
        },
        Dependencies);
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetClassFees/GetClassFeesQuery.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetClassFees;

/// <summary>
/// GET /api/v1/finance/fees — ticket JGK-F01. Le barème de l'école courante : une ligne par
/// (catégorie × classe) déjà définie. Les couples encore vierges n'apparaissent pas — c'est
/// l'interface qui, connaissant catégories et classes, affiche « non défini » là où il manque une
/// ligne. Le tenant vient du JWT (RLS + Global Query Filter).
/// </summary>
public record GetClassFeesQuery : IRequest<IReadOnlyList<ClassFeeDto>>;

/// <summary>
/// <paramref name="RowVersion"/> est le jeton de concurrence (xmin PostgreSQL) : l'interface le
/// renvoie tel quel lors d'une modification, pour que deux éditions concurrentes ne s'écrasent pas
/// (AGENTS.md règle #5). Les noms de catégorie et de classe sont dénormalisés pour que la liste soit
/// lisible sans jointure côté client.
/// </summary>
public record ClassFeeDto(
    Guid Id,
    Guid FeeCategoryId,
    string FeeCategoryName,
    Guid ClassroomId,
    string ClassroomName,
    string Level,
    decimal Amount,
    uint RowVersion);
````


#### `src/SamaEcole.Application/Finance/Queries/GetClassFees/GetClassFeesQueryHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetClassFees;

public class GetClassFeesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassFeesQuery, IReadOnlyList<ClassFeeDto>>
{
    public async Task<IReadOnlyList<ClassFeeDto>> Handle(
        GetClassFeesQuery request, CancellationToken cancellationToken)
    {
        // Jointures vers fee_categories et classrooms pour restituer des noms lisibles. Toutes les
        // tables sont filtrées sur le même tenant (RLS + Global Query Filter), la jointure ne peut
        // donc pas faire fuiter une catégorie ou une classe d'une autre école.
        //
        // xmin est le jeton de concurrence exposé au client (EF.Property : c'est une colonne système,
        // sans propriété CLR sur l'entité).
        return await (
            from fee in dbContext.ClassFees.AsNoTracking()
            join category in dbContext.FeeCategories.AsNoTracking() on fee.FeeCategoryId equals category.Id
            join classroom in dbContext.Classrooms.AsNoTracking() on fee.ClassroomId equals classroom.Id
            orderby category.Name, classroom.Level, classroom.Name
            select new ClassFeeDto(
                fee.Id,
                fee.FeeCategoryId,
                category.Name,
                fee.ClassroomId,
                classroom.Name,
                classroom.Level,
                fee.Amount,
                EF.Property<uint>(fee, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetFeeCategories/GetFeeCategoriesQuery.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFeeCategories;

/// <summary>
/// GET /api/v1/finance/fee-categories — ticket JGK-F01.
/// Les catégories de l'école courante, et elles seules (tenant du JWT, RLS + Global Query Filter).
/// </summary>
public record GetFeeCategoriesQuery : IRequest<IReadOnlyList<FeeCategoryDto>>;

public record FeeCategoryDto(Guid Id, string Name, bool IsRecurring, bool IsOptional);
````


#### `src/SamaEcole.Application/Finance/Queries/GetFeeCategories/GetFeeCategoriesQueryHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFeeCategories;

public class GetFeeCategoriesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFeeCategoriesQuery, IReadOnlyList<FeeCategoryDto>>
{
    public async Task<IReadOnlyList<FeeCategoryDto>> Handle(
        GetFeeCategoriesQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.FeeCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new FeeCategoryDto(c.Id, c.Name, c.IsRecurring, c.IsOptional))
            .ToListAsync(cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetDeletedFeeCategories/GetDeletedFeeCategoriesQuery.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetDeletedFeeCategories;

/// <summary>GET /api/v1/fee-categories/deleted — corbeille des catégories de frais de l'école courante.</summary>
public record GetDeletedFeeCategoriesQuery : IRequest<IReadOnlyList<DeletedFeeCategoryDto>>;

public record DeletedFeeCategoryDto(Guid Id, string Name, DateTimeOffset? DeletedAt);

public class GetDeletedFeeCategoriesQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetDeletedFeeCategoriesQuery, IReadOnlyList<DeletedFeeCategoryDto>>
{
    public Task<IReadOnlyList<DeletedFeeCategoryDto>> Handle(
        GetDeletedFeeCategoriesQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return SoftDeleteLifecycle.ListDeletedAsync(
            dbContext.FeeCategories, schoolId,
            c => new DeletedFeeCategoryDto(c.Id, c.Name, c.DeletedAt), cancellationToken);
    }
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetFeeCategoryDeletionImpact/GetFeeCategoryDeletionImpactQuery.cs`

````csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFeeCategoryDeletionImpact;

/// <summary>Analyse avant suppression d'une catégorie de frais : ce qui l'empêche ou l'accompagne, sans rien modifier.</summary>
public record GetFeeCategoryDeletionImpactQuery(Guid FeeCategoryId) : IRequest<DeletionImpact>;

public class GetFeeCategoryDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFeeCategoryDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetFeeCategoryDeletionImpactQuery request, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(dbContext, FeeCategoryDeletionPolicy.Policy, request.FeeCategoryId, cancellationToken);
}
````


#### `src/SamaEcole.Application/Finance/Queries/GetFeeHistory/GetFeeHistoryQuery.cs`

````csharp
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFeeHistory;

/// <summary>
/// GET /api/v1/finance/fees/{id}/history — ticket JGK-F01, Volume 1 §7.4.
/// L'historique d'une ligne de barème, du plus récent au plus ancien. Tenant du JWT (RLS + filtre).
/// </summary>
public record GetFeeHistoryQuery(Guid ClassFeeId) : IRequest<IReadOnlyList<FeeHistoryDto>>;

/// <summary>
/// <paramref name="OldAmount"/> est null pour la toute première définition du montant (création).
/// <paramref name="ChangedByName"/> est le nom de l'auteur, joint depuis la table des comptes.
/// </summary>
public record FeeHistoryDto(
    decimal? OldAmount,
    decimal NewAmount,
    Guid ChangedByUserId,
    string ChangedByName,
    DateTimeOffset ChangedAt);
````


#### `src/SamaEcole.Application/Finance/Queries/GetFeeHistory/GetFeeHistoryQueryHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFeeHistory;

public class GetFeeHistoryQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFeeHistoryQuery, IReadOnlyList<FeeHistoryDto>>
{
    public async Task<IReadOnlyList<FeeHistoryDto>> Handle(
        GetFeeHistoryQuery request, CancellationToken cancellationToken)
    {
        // Jointure GAUCHE vers les comptes : l'auteur d'un changement pourrait avoir été archivé
        // depuis (soft delete), et son entrée d'audit doit rester lisible malgré tout — l'histoire ne
        // s'efface pas parce qu'un compte a été fermé. Sans le left join, ces lignes disparaîtraient.
        return await (
            from h in dbContext.FeeChangeHistory.AsNoTracking()
            where h.ClassFeeId == request.ClassFeeId
            join u in dbContext.Users.AsNoTracking() on h.ChangedByUserId equals u.Id into authors
            from author in authors.DefaultIfEmpty()
            orderby h.ChangedAt descending
            select new FeeHistoryDto(
                h.OldAmount,
                h.NewAmount,
                h.ChangedByUserId,
                author != null ? author.FullName : "Compte supprimé",
                h.ChangedAt))
            .ToListAsync(cancellationToken);
    }
}
````


---

## 7. API REST (toutes les routes)

Préfixe `api/v1/finance`. Contrôleur `FinanceController` (`[Authorize]`, `[RequireModule(SchoolModule.Finance)]`), plus `DisbursementsController` et `FinancialReportsController`. Constantes de rôles : `PaymentWriters`, `CashierRoles` (voir code §14.4).

| Méthode | Route | Rôle / politique | Rôle de la route |
|---|---|---|---|
| GET | `/fee-categories` | tout rôle de l'école | liste des catégories |
| POST | `/fee-categories` | `CanModifyFees` | créer |
| PUT | `/fee-categories/{id}` | `CanModifyFees` | modifier (`IsOptional`) |
| DELETE | `/fee-categories/{id}` | `CanDeleteFees` | supprimer (soft) |
| GET | `/fee-categories/{id}/deletion-impact` | `CanDeleteFees` | dépendances bloquantes |
| POST | `/fee-categories/{id}/unlink-dependencies` | `CanDeleteFees` | rompre explicitement les liens |
| GET | `/fee-categories/deleted` | `CanDeleteFees` | corbeille |
| POST | `/fee-categories/{id}/restore` | `CanDeleteFees` | restaurer |
| GET | `/fees` | tout rôle | barème complet |
| POST | `/fees/apply-standard` | `CanModifyFees` | montant standard (tout / niveau) |
| PUT | `/fees/{id}` | `CanModifyFees` | modifier un montant `{amount,rowVersion}` |
| DELETE | `/fees/{id}?rowVersion=` | `CanDeleteFees` | supprimer une ligne |
| GET | `/fees/{id}/history` | tout rôle | historique |
| GET | `/students/{studentId}/balance` | rôles tenant une caisse | solde + échéancier + historique |
| GET | `/students/search?q=` | rôles tenant une caisse | recherche élève + solde |
| GET | `/payments` | Directeur, Finance | liste paginée (page, pageSize, search, method, classroomId) |
| POST | `/payments` | `PaymentWriters` (Directeur, Finance, Secretariat) | **encaisser** → 201 `RecordPaymentResult` |
| POST | `/payments/{id}/cancel` | `CanCancelPayments` | annuler / contre-passer `{reason,outcome,reversal}` |
| GET | `/payments/{id}/receipt` | tout rôle | reçu (JSON) |
| GET | `/payments/{id}/receipt/pdf` | tout rôle | reçu A5 PDF |
| GET | `/dashboard` | Directeur, Finance | KPI finance |
| GET | `/daily-cash-register/pdf` | Directeur, Finance | journal de caisse du jour |
| GET | `/sessions/current` | `CashierRoles` (Directeur, Finance, Secretariat) | session ouverte |
| POST | `/sessions/open` | `CashierRoles` | ouvrir `{openingBalance}` |
| POST | `/sessions/{id}/close` | `CashierRoles` | clôturer `{actualCashAmount, discrepancyReason}` |
| GET | `/sessions/{id}/closing-report` | `CashierRoles` | PDF de clôture |
| GET | `/treasury` | Directeur, Finance | trésorerie (startDate, endDate) |
| GET/POST/DELETE | `/disbursements` (`DisbursementsController`) | Directeur, Finance, SuperAdmin | liste / créer / contre-passer |
| GET | `/reports/revenue`, `/reports/revenue/excel` | Directeur, Finance + `Feature.AdvancedFinancialReports` | consolidation + export |
| GET | `/reports/debtor-aging`, `/reports/debtor-aging/excel` | idem | balance âgée + export |
| GET/POST | `/employee-contracts` | Directeur, Finance | liste / créer |
| PATCH | `/employee-contracts/{id}` | Directeur, Finance | modifier rémunération/régime |
| POST | `/employee-contracts/{id}/close` | Directeur, Finance | clôturer |
| GET/POST | `/employee-contracts/{id}/hour-records` | Directeur, Finance | heures vacataire |
| GET | `/employee-contracts/{id}/hour-records/sheet[/pdf]` | Directeur, Finance | fiche de suivi |
| GET | `/employee-contracts/{id}/suggested-hours` | Directeur, Finance | heures suggérées |
| GET | `/employee-contracts/{id}/work-certificate[/pdf]` | Directeur, Finance | attestation de travail |
| GET/POST | `/payroll` | Directeur, Finance | lister / générer une fiche |
| POST | `/payroll/{id}/cancel`, `/payroll/{id}/regenerate` | `CanCancelPayslips` | annuler / régénérer |
| POST | `/payroll/{id}/pay` | Directeur, Finance | régler le salaire |
| GET | `/payroll/{id}/pdf` | Directeur, Finance | bulletin / reçu |
| GET | `/tax-declarations?year=` | Directeur, Finance | liste |
| POST | `/tax-declaration` | Directeur, Finance | générer `{month,year}` |
| GET | `/tax-declarations/{id}/pdf` | Directeur, Finance | PDF officiel |
| GET | `/enrollments/{id}/dues-notice[/pdf]` | Directeur, Finance | sommation |
| POST | `/enrollments/{id}/installment-plan` | Directeur, Finance | échéancier négocié |
| POST | `/classrooms/{id}/installment-plan` | Directeur, Finance | modèle appliqué à une classe |
| POST | `/commitments` ; GET `/commitments/{id}[/pdf]` | Directeur, Finance | engagement financier |
| GET | `/dues-reminder-batches` | Directeur, Finance | lots de relance |
| POST | `/dues-reminder-batches/recompute` | Directeur, Finance | recalcul |
| POST | `/dues-reminder-batches/{id}/send` , `/dismiss` | Directeur, Finance | envoyer / écarter |

Pages Razor (rendues par `PagesController`) : `/frais`, `/caisse`, `/tresorerie`, `/paie`, `/fiscalite`, `/rapports/financiers`.

Codes d'erreur : `404` introuvable (ou autre école), `403` rôle/délégation/fonctionnalité, `409` conflit de concurrence / règle métier / unicité, `422` validation.

---

## 8. Autorisations, délégations, formules d'abonnement

* **Module** : `[RequireModule(SchoolModule.Finance)]` → désactivable par école.
* **Rôles** : Directeur et Finance accèdent à presque tout. Les constantes `PaymentWriters` et `CashierRoles` valent `"Directeur,Finance,Secretariat"` : **le Secrétariat peut donc ouvrir/clôturer une session et encaisser** (c'est lui qui compose aussi le dû à l'inscription, et tout rôle de l'école lit le barème). La liste des paiements, le dashboard, la trésorerie, la paie, la fiscalité et les rapports sont réservés à Directeur/Finance. L'élève et le parent n'ont aucun accès (portails reportés en V3).
* **Matrice de délégation (« Photoshop »)** — politiques ASP.NET `FinancePolicies` + `AuthorizationHandler` qui lisent `SchoolSettings` : `CanModifyFees`, `CanDeleteFees`, `CanCancelPayments`, `CanCancelPayslips`. Le Directeur passe toujours ; la Finance passe si l'interrupteur de **son** école est actif (valeur par défaut si aucune ligne de paramètres n'existe).
* **Contre-passation d'un paiement** : revérifiée **dans le handler** (`Role.Directeur`), pas seulement par la politique.
* **Formules** : `Feature.AdvancedFinancialReports` (Standard, Premium), `Feature.SmsNotifications` (Premium) via `[RequireFeature]`.
* **Journal d'audit** : toute commande `IAuditableRequest`.


#### `src/SamaEcole.Web/Authorization/FinancePolicies.cs`

````csharp
namespace SamaEcole.Web.Authorization;

/// <summary>Noms des politiques d'autorisation liées à la délégation Finance (matrice "Photoshop").</summary>
public static class FinancePolicies
{
    /// <summary>
    /// Ajuster un montant de barème déjà défini (PUT /finance/fees/{id}). Voir
    /// CanModifyFeesRequirement pour la règle et CanModifyFeesHandler pour son évaluation.
    /// </summary>
    public const string CanModifyFees = "CanModifyFees";

    /// <summary>
    /// Supprimer (soft delete) une catégorie de frais ou une ligne de barème. Voir
    /// CanDeleteFeesRequirement pour la règle et CanDeleteFeesHandler pour son évaluation.
    /// </summary>
    public const string CanDeleteFees = "CanDeleteFees";

    /// <summary>Annuler un versement (48 h) : Directeur toujours, Finance sauf retrait du droit par le Directeur (CanCancelPaymentsHandler).</summary>
    public const string CanCancelPayments = "CanCancelPayments";

    /// <summary>Annuler / corriger une fiche de paie : Directeur toujours, Finance sauf retrait du droit par le Directeur (CanCancelPayslipsHandler).</summary>
    public const string CanCancelPayslips = "CanCancelPayslips";
}
````


#### `src/SamaEcole.Web/Authorization/CanModifyFeesRequirement.cs`

````csharp
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Remplace le `[Authorize(Roles = "Directeur")]` codé en dur sur PUT /finance/fees/{id}. Le
/// Directeur passe toujours ; la Finance ne passe que si SON école a activé la délégation
/// (SchoolSettings.AllowFinanceToModifyFees) — voir CanModifyFeesHandler pour la règle.
/// </summary>
public class CanModifyFeesRequirement : IAuthorizationRequirement;
````


#### `src/SamaEcole.Web/Authorization/CanModifyFeesHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée UNIQUEMENT si le Directeur de SON école a
/// coché « déléguer la modification des frais à la Finance » (SchoolSettings.AllowFinanceToModifyFees).
///
/// La lecture passe par IApplicationDbContext, pas par un rôle codé en dur — même mécanique que
/// CanManageGradingScaleHandler (JGK-G02). Le Global Query Filter EF Core cantonne déjà la requête
/// à l'école du JWT courant (AGENTS.md règle #2).
///
/// Absence de ligne SchoolSettings : FirstOrDefaultAsync sur un bool renvoie `false` par défaut —
/// même comportement sûr que SchoolSettingsDefaults.AllowFinanceToModifyFees, la délégation reste
/// fermée tant que le Directeur ne l'a pas explicitement activée.
/// </summary>
public class CanModifyFeesHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanModifyFeesRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanModifyFeesRequirement requirement)
    {
        if (context.User.IsInRole(nameof(Role.Directeur)))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole(nameof(Role.Finance)))
        {
            return;
        }

        var allowFinance = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => s.AllowFinanceToModifyFees)
            .FirstOrDefaultAsync();

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
````


#### `src/SamaEcole.Web/Authorization/CanDeleteFeesRequirement.cs`

````csharp
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Protège DELETE /finance/fee-categories/{id} et DELETE /finance/fees/{id}. Le Directeur passe
/// toujours ; la Finance ne passe que si SON école a activé la délégation
/// (SchoolSettings.AllowFinanceToDeleteFees) — voir CanDeleteFeesHandler pour la règle.
///
/// Distincte de CanModifyFeesRequirement : supprimer une catégorie entière est un geste plus lourd
/// de conséquences (elle disparaît de toute l'interface) qu'ajuster un montant, donc un commutateur
/// de délégation séparé.
/// </summary>
public class CanDeleteFeesRequirement : IAuthorizationRequirement;
````


#### `src/SamaEcole.Web/Authorization/CanDeleteFeesHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée UNIQUEMENT si le Directeur de SON école a
/// coché « déléguer la suppression des frais à la Finance » (SchoolSettings.AllowFinanceToDeleteFees).
/// Même mécanique que CanModifyFeesHandler, avec son propre commutateur (règle de délégation
/// distincte de la simple modification de montant).
/// </summary>
public class CanDeleteFeesHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanDeleteFeesRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanDeleteFeesRequirement requirement)
    {
        if (context.User.IsInRole(nameof(Role.Directeur)))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole(nameof(Role.Finance)))
        {
            return;
        }

        var allowFinance = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => s.AllowFinanceToDeleteFees)
            .FirstOrDefaultAsync();

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
````


#### `src/SamaEcole.Web/Authorization/CanCancelPayslipsRequirement.cs`

````csharp
using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Exigence « peut annuler ou corriger une fiche de paie » : Directeur toujours ; Finance seulement si le Directeur de
/// son école n'a pas retiré ce droit (SchoolSettings.AllowFinanceToCancelPayslips, vrai par défaut) — voir
/// CanCancelPayslipsHandler.
/// </summary>
public class CanCancelPayslipsRequirement : IAuthorizationRequirement;
````


#### `src/SamaEcole.Web/Authorization/CanCancelPayslipsHandler.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Directeur : toujours autorisé. Finance : autorisée sauf si le Directeur de SON école a retiré ce droit
/// (SchoolSettings.AllowFinanceToCancelPayslips, vrai par défaut). Même mécanique que CanCancelPaymentsHandler. Tout autre
/// rôle est refusé. Payer une fiche n'est pas concerné : il reste ouvert au Directeur et à la Finance.
/// </summary>
public class CanCancelPayslipsHandler(IApplicationDbContext dbContext)
    : AuthorizationHandler<CanCancelPayslipsRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CanCancelPayslipsRequirement requirement)
    {
        if (context.User.IsInRole(nameof(Role.Directeur)))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole(nameof(Role.Finance)))
        {
            return;
        }

        // Pas de ligne de paramètres du tout (école antérieure aux paramètres) : le défaut s'applique.
        var allowFinance = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => (bool?)s.AllowFinanceToCancelPayslips)
            .FirstOrDefaultAsync() ?? SchoolSettingsDefaults.AllowFinanceToCancelPayslips;

        if (allowFinance)
        {
            context.Succeed(requirement);
        }
    }
}
````


---

## 9. Interface utilisateur

Vues Razor + composants **Alpine.js** (`Alpine.data('…')`) + Tailwind. Tous les appels passent par le client `api` (préfixe `/api/v1`, JWT, gestion 401/409/422). Le code complet est dans l'**annexe A**. Résumé :

| Écran | Vue | Composant JS | Contenu |
|---|---|---|---|
| `/caisse` | `Views/Caisse/Index.cshtml` | `caisseView` (`caisse.js`) + mixin `payment-cancel.js` | ouverture/clôture de session, recherche élève avec solde, **paiement rapide par lignes** (« payer l'exigible », « solder »), brouillon restauré localement, **clé d'idempotence** générée à l'ouverture du formulaire, reçu A5 imprimable avec **indicateur de tenue en page** (`checkReceiptFit`), annulation / contre-passation (délai calculé serveur) |
| `/frais` | `Views/Fees/Index.cshtml` | `feesView` (`fees.js`) | onglets par catégorie, grille classes × montants triable, modification avec `rowVersion`, historique, montant standard, création/suppression de catégories, onglet **dépenses** (création, contre-écriture, état « annulé »/« contre-écriture ») |
| `/paie` | `Views/Payroll/Index.cshtml` | `payrollView` (`payroll.js`) | contrats (création, modification, clôture, personnel sans compte, moyen de règlement), fiches (génération avec heures suggérées, annulation, régénération, paiement), heures vacataires, PDF bulletin / fiche de suivi / attestation |
| `/fiscalite` | `Views/Taxes/Index.cshtml` | `taxesView` (`taxes.js`) | choix mois/année, génération, liste, PDF |
| `/tresorerie` | `Views/Treasury/Index.cshtml` | `treasuryView` (`treasury.js`) | filtres de période, totaux, répartitions avec parts, dernières opérations |
| `/rapports/financiers` | `Views/Reports/Financial.cshtml` | `financialReportView` (`financial-report.js`) | consolidation (cycle, classe, mode, mois) + export `.xlsx` (désactivé et signalé en formule Primaire) |

Contraintes UI notables : reçu, bulletin et dashboard suivent **exactement** `docs/design-references/` ; les messages d'erreur métier sont affichés tels que renvoyés par l'API (codes stables).

---

## 10. Documents imprimés et exports

Générés **côté serveur** avec **QuestPDF** (`Infrastructure/Documents`, un `*Document` + un `*PdfGenerator` par pièce, contrats `I*PdfGenerator` dans `Application/Common/Interfaces`) et **ClosedXML** (`Infrastructure/Files`).

| Pièce | Format | Particularités |
|---|---|---|
| Reçu de paiement | A5 paysage, marge 10 mm | n° `REC-AAAA-NNNN`, lignes de ventilation (colonne « Période / Note »), solde **figé** (`BalanceAfter`), mention légale fixe, bloc de signatures « Le Caissier / Le Directeur », version annulée avec motif et destination de l'argent |
| Reçu d'inscription | A5 paysage | uniquement ce qui a été encaissé (`AmountCollected`) |
| Rapport de clôture de caisse | A4 portrait | ventilations méthode/catégorie, détail, écart |
| Journal de caisse du jour | A4 portrait | chronologique, totaux par méthode |
| Bulletin de paie / reçu de salaire | A4 | marques « ANNULÉE », « REÇU DE PAIEMENT DU SALAIRE », « Paie sans retenues » |
| Fiche de suivi des heures | A4 | à signer |
| Attestation de travail | A4 | |
| Déclaration fiscale | A4 | TVA + charges sociales |
| Sommation pour impayés | A4 | refusée si à jour |
| Engagement financier | A4 | |
| Rapport de revenus / balance âgée | `.xlsx` | classeur « Synthèse » + feuilles de ventilation (revenus) ; feuille « Débiteurs » (balance âgée) ; mêmes chiffres que l'écran |

Code : annexe A (§A.4).

---

## 11. Stratégie de tests

~9 600 lignes de tests, catégories `MultiTenant` (isolation) et suites Finance. Le code intégral est en **annexe B**. Ce qu'il faut reprendre en priorité (invariants à ne jamais perdre) :

| Invariant | Test (fichier) |
|---|---|
| Deux encaissements concurrents → un seul passe, l'autre 409 | `PaymentConcurrencyTests` |
| Retry avec même clé → même reçu, pas de doublon | `PaymentIdempotencyTests` |
| Registre daté (annulation tardive / antérieure / session) | `PaymentLedgerTests`, `CancelPaymentTests`, `CancellationReportsTests` |
| Clôture : écart espèces, motif obligatoire, pas de re-clôture | `CashierSessionClosingTests`, `GetCurrentCashierSessionQueryTests`, `CloseCashierSessionCommandValidatorTests` |
| Barème : verrou, isolation, historique | `ClassFeeConcurrencyTests`, `FeeIsolationTests`, `FeesEndpointsTests` |
| Frais facultatifs / pension | `OptionalFeeCategoryTests`, `OptionalFeesAnnualDueTests`, `CreateFeeCategoryBoardingFeeTests` |
| Paie : barèmes, sans retenues, isolation, correction | `PayrollCalculatorTests`, `PayrollWithoutDeductionsCalculatorTests`, `PayrollIsolationTests`, `PayslipCorrectionTests`, `PayslipCancellationDelegationTests` |
| Contrats : cycle de vie | `EmployeeContractLifecycleTests` |
| Fiscalité : déclaration unique, TVA nette | `GenerateTaxDeclarationCommandTests`, `VatCalculatorTests` |
| Recouvrement : balance âgée, isolation des lots | `GetDebtorAgingReportTests`, `DebtorReminderBatchIsolationTests`, `InstallmentScheduleCalculatorTests` |
| Trésorerie | `GetTreasuryDashboardQueryTests` |
| Historique d'un élève supprimé | `DeletedStudentFinancialHistoryTests` |
| Registres immuables | `ImmutableRegistersTests` |
| PDF (reçu, bulletin, déclaration, clôture…) | `*PdfGeneratorTests` |
| JS : caisse rapide, retenues de paie, frais/dépenses | `src/SamaEcole.Web/tests/js/*.test.mjs` |

---

## 12. Guide de reproduction pas à pas dans un autre projet

> Ordre recommandé : chaque étape est livrable et testable seule. Les numéros renvoient au code de ce document.

### Étape 0 — Prérequis techniques
1. .NET 9, EF Core + **Npgsql uniquement**, MediatR, FluentValidation, QuestPDF, ClosedXML.
2. Deux rôles PostgreSQL (`app` non propriétaire, `NOBYPASSRLS`, et `owner` pour les migrations) et un `RlsGuard` au démarrage.
3. Socle commun : `AuditableEntity` (+ `SoftDelete`), `ITenantEntity`, `ITenantProvider` (SchoolId du JWT), filtre global de tenant, `ICurrentUserService`, `TimeProvider`, `IApplicationDbContext` (avec `ExecuteInTransactionAsync` et `SetOriginalConcurrencyToken`), `ExceptionHandlingMiddleware` (mapping des erreurs §3.7), pipeline MediatR (validation + audit `IAuditableRequest`), `IKpiCacheService` scopé tenant.
4. Générateur de numéros **gapless** : table `matricule_sequences(SchoolId, Kind, Year, LastValue)` + `INSERT … ON CONFLICT … RETURNING` (code §14.1).

### Étape 1 — Barème (§6.1, §6.12)
Entités `FeeCategory`, `ClassFee`, `FeeChangeHistory` ; migrations avec RLS + GRANT (**append-only** pour l'historique) ; commandes/requêtes ; politique de suppression ; contrôleur + écran `/frais`.

### Étape 2 — Inscription → dû (§6.2)
`Enrollment` (`TotalDue`, `AmountPaid`, `ReceiptNumber`, **xmin**), `EnrollmentFeeLine`. Copier le barème en lignes figées, calculer le total, attribuer le n° de reçu dans la même transaction. Ajouter frais facultatifs et pension si nécessaires.

### Étape 3 — Caisse (§6.4–§6.6)
`CashierSession` (index unique partiel), `Payment`, `PaymentBreakdown`. Implémenter dans cet ordre : `OpenCashierSession` → `RecordPayment` (idempotence, contrôles de solde, xmin, n° de reçu, TVA, SMS après commit) → `PaymentLedger` → `CancelPayment` + `PaymentCancellationPolicy` → `CloseCashierSession`. Reçu PDF A5 et rapport de clôture. **Écrire les tests de concurrence et d'idempotence dès cette étape.**

### Étape 4 — Solde, échéancier, recouvrement (§6.3, §6.7)
`InstallmentScheduleCalculator`, `GetStudentBalance`, recherche pour la caisse, plans d'échéances, sommation, engagement, balance âgée, lots de relance + service d'arrière-plan multi-tenant.

### Étape 5 — Décaissements et trésorerie (§6.8, §6.11)
`Disbursement` + contre-écriture (contraintes SQL !), `GetTreasuryDashboard`, `GetFinanceDashboard`.

### Étape 6 — Paie (§6.9)
`EmployeeContract` (+ historique append-only), `TeacherHourRecord`, `FichePaie`, `PayrollCalculator` (**rendez les barèmes paramétrables avec date d'effet** — §13), annulation/régénération/règlement, attestation, bulletin PDF.

### Étape 7 — Fiscalité (§6.10)
`TaxeDeclaration` (unique par mois), `TaxCalculator`, PDF.

### Étape 8 — Rapports (§6.11)
Consolidation + exports Excel, restreints par formule.

### Étape 9 — Délégations et sécurité (§8)
Interrupteurs de `SchoolSettings`, politiques et handlers, `[RequireModule]`, `[RequireFeature]`.

### Étape 10 — Qualité
Tests d'isolation multi-tenant (**obligatoires** pour toute table financière), tests des invariants du §11, `CHECK` SQL, vérification que le rôle applicatif n'a ni `DELETE` ni `UPDATE` sur les tables append-only.

### 12.1 Pièges déjà rencontrés dans ce dépôt (à ne pas refaire)
1. **`SchoolId` explicite sur les entités enfants** (ex. `PaymentBreakdown`) : EF ne le propage pas, la RLS rejette l'INSERT (« new row violates row-level security policy »).
2. **`IgnoreQueryFilters()` retire aussi le filtre de tenant** : toujours rétablir `SchoolId == …` à la main (voir `PaymentOwnerResolver`).
3. **Numéro de reçu dans la transaction**, pas à l'ouverture du formulaire ; sinon trous de numérotation.
4. **Statut d'un paiement ≠ « argent reçu »** : `Paid/Partial` décrit le solde de l'inscription, seul `Cancelled` sort un versement des totaux.
5. **Comparer le comptage physique aux espèces seules**, pas au total toutes méthodes, sinon écart systématique dès qu'il y a Wave/virement.
6. **Effets externes après commit** (SMS, cache) ; un SMS ne doit jamais faire échouer un encaissement.
7. **Contre-écriture datée du jour de l'annulation** (jamais de réécriture d'une période déclarée).
8. **Ne pas faire reposer l'unicité sur le soft delete** : index uniques **partiels** (`IsDeleted=false`, `CancelledAt IS NULL`, `EndDate IS NULL`, `Status='Active'`).
9. **Agrégats « par cycle »** nécessitant du C# (`ClassroomCycle.CycleFor`) : agrégation en mémoire, volumétrie annuelle faible.
10. **Une tâche de fond multi-tenant** doit ouvrir une connexion neuve par école.
11. **Idempotence testée avant tout autre contrôle** (une session depuis fermée ne doit pas empêcher de rejouer).
12. **Taux de TVA en fraction** et validation `[0;1]` à trois niveaux (validator, handler, `CHECK` SQL).

---

## 13. Limites connues, dette et améliorations recommandées

### 13.1 Limites documentées dans le dépôt (Volume 1)
1. **Barèmes sociaux figés dans le code** (IPRES 5,6 %/8,4 %, CSS 7 %, CFCE 3 %, BRS 5 %, plafonds 360 000 / 63 000) : un changement légal impose une livraison et s'applique **rétroactivement** aux recalculs. *Recommandation :* table `payroll_rate_schedules` avec date d'effet ; la fiche garde un instantané du barème utilisé.
2. **Pas de déclaration rectificative** : une déclaration est un instantané ; si les données du mois changent ensuite, il faut un circuit « annuler puis regénérer » ou « rectificative » (non implémenté).
3. **Rapprochement de caisse** : implémenté depuis (JGK-F09) — la note « limite connue » du Volume 1 §15.2 est **obsolète** (colonnes `ExpectedCashAmount/ActualCashAmount/DiscrepancyAmount/DiscrepancyReason`). Le statut `Verified` existe dans l'énumération mais **aucun flux** ne l'utilise (validation par un superviseur à prévoir).
4. **Calcul BRS « très simplifié »** (commentaire du code) : base = brut − IPRES salarié ; le barème IRPP/BRS réel est plus complexe — à faire valider par un fiscaliste avant tout usage réel.

### 13.2 Observations issues de la lecture du code
* `GenerateFichePaieCommand` et `GenerateTaxDeclarationCommand` **n'implémentent pas `IAuditableRequest`** alors que l'annulation, la régénération et le paiement le font ; à aligner.
* `CreateDisbursementCommand` / `DeleteDisbursementCommand` n'implémentent pas non plus `IAuditableRequest`, alors qu'un décaissement est une écriture comptable.
* Les décaissements utilisent le nommage de table PascalCase (`"Disbursements"`) et `decimal(18,2)`, contrairement au reste du module (snake_case, `numeric(12,2)`) ; ce sont des migrations plus anciennes. À harmoniser dans un nouveau projet.
* La migration `AddScheduleAndDisbursements` accorde `DELETE` ; la migration finale `RevokeDeleteOnBusinessTables` le retire — **dans un nouveau projet, ne jamais accorder `DELETE` d'emblée**.
* Les contre-écritures de salaires reprennent `Category`, `PaymentMethod` et `Beneficiary` de l'original mais **pas** `ReceiptUrl`.
* Les formats d'export, la TVA par défaut (aucune hypothèse codée : tout est déclaré à la saisie) et les libellés sont **en français / FCFA** : prévoir une couche d'internationalisation et de devise si votre projet cible un autre pays.

### 13.3 Pour une vraie comptabilité en partie double (extension recommandée)
Ajoutez, sans toucher aux registres existants :
1. `accounting_accounts` (plan comptable par école, import SYSCOHADA), `accounting_journals` (CAISSE, BANQUE, OD, PAIE), `accounting_entries` + `accounting_entry_lines` (débit/crédit, `SourceType`, `SourceId`).
2. Un **posting service** déclenché après commit par événement de domaine :
   * `Payment` créé → D `5xx` (caisse/banque selon `Method`) / C `706x` (produits, par `FeeCategory`) + C `4431` (TVA collectée si `VatAmount > 0`) ; si `BalanceAfter` > 0 et facturation à l'inscription, passer par un compte client `411` (facturation → `EnrollmentFeeLine`).
   * Annulation → écriture d'extourne datée du jour (miroir du `PaymentLedger`).
   * `Disbursement` → D `6xx` + D `4452` (TVA déductible) / C `5xx` ; contre-écriture → extourne.
   * `PayFichePaie` → D `661` / C `422` puis règlement ; charges patronales (IPRES/CSS/VRS) → D `664` / C `431/447`.
   * `TaxeDeclaration` → apurement TVA (D `4431`, C `4452`, solde `4441`).
3. Verrouillage de période (période comptable close = écritures interdites, extourne seulement dans la période ouverte).
4. États : grand livre, balance, journal, compte de résultat simplifié.

---

## 14. Code source intégral du back-end

> Les sections précédentes ont déjà inséré le code **au fil des flux**. Cette section regroupe le reste du back-end indispensable (socle technique, contrôleurs, services d'infrastructure, DI) pour que le fichier soit autosuffisant.

### 14.1 Socle technique (contexte de données, transactions, concurrence, numérotation, erreurs)

#### `src/SamaEcole.Persistence/ApplicationDbContext.cs`

````csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Persistence.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SamaEcole.Persistence;

/// <summary>
/// Toute entité implémentant ITenantEntity DOIT recevoir ici un Global Query Filter sur
/// SchoolId (AGENTS.md règle #2). Ce filtre est une défense en profondeur : la policy RLS
/// PostgreSQL (voir Migrations/) reste la protection réelle et doit bloquer même si ce
/// filtre est un jour oublié sur une nouvelle entité.
/// </summary>
public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantProvider tenantProvider,
    ILogger<ApplicationDbContext> logger)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<FeeCategory> FeeCategories => Set<FeeCategory>();
    public DbSet<ClassFee> ClassFees => Set<ClassFee>();
    public DbSet<FeeChangeHistory> FeeChangeHistory => Set<FeeChangeHistory>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<EnrollmentFeeLine> EnrollmentFeeLines => Set<EnrollmentFeeLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CashierSession> CashierSessions => Set<CashierSession>();
    public DbSet<PaymentBreakdown> PaymentBreakdowns => Set<PaymentBreakdown>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionChangeRequest> SubscriptionChangeRequests => Set<SubscriptionChangeRequest>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<QuotaTierPrice> QuotaTierPrices => Set<QuotaTierPrice>();
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<UserSchool> UserSchools => Set<UserSchool>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<Disbursement> Disbursements => Set<Disbursement>();
    public DbSet<SchoolRegistrationRequest> SchoolRegistrationRequests => Set<SchoolRegistrationRequest>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<UserStatusHistory> UserStatusHistory => Set<UserStatusHistory>();
    public DbSet<SchoolSettings> SchoolSettings => Set<SchoolSettings>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<SchoolClosure> SchoolClosures => Set<SchoolClosure>();
    public DbSet<RollBookEntry> RollBookEntries => Set<RollBookEntry>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Mention> Mentions => Set<Mention>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<TeacherAssignment> TeacherAssignments => Set<TeacherAssignment>();
    public DbSet<AttendanceSheet> AttendanceSheets => Set<AttendanceSheet>();
    public DbSet<ClassJournalEntry> ClassJournalEntries => Set<ClassJournalEntry>();
    public DbSet<SubjectCoefficientOverride> SubjectCoefficientOverrides => Set<SubjectCoefficientOverride>();
    public DbSet<ClassSubject> ClassSubjects => Set<ClassSubject>();
    public DbSet<StudentSubjectEnrollment> StudentSubjectEnrollments => Set<StudentSubjectEnrollment>();
    public DbSet<StudentSubjectExemption> StudentSubjectExemptions => Set<StudentSubjectExemption>();
    public DbSet<Dormitory> Dormitories => Set<Dormitory>();
    public DbSet<DormitoryRoom> DormitoryRooms => Set<DormitoryRoom>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<BoardingEnrollment> BoardingEnrollments => Set<BoardingEnrollment>();
    public DbSet<BoardingLeave> BoardingLeaves => Set<BoardingLeave>();
    public DbSet<BoardingAttendance> BoardingAttendances => Set<BoardingAttendance>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<GradeAgeNorm> GradeAgeNorms => Set<GradeAgeNorm>();
    public DbSet<SyllabusUnit> SyllabusUnits => Set<SyllabusUnit>();
    public DbSet<ClassJournalEntryUnit> ClassJournalEntryUnits => Set<ClassJournalEntryUnit>();
    public DbSet<WeeklyHourNorm> WeeklyHourNorms => Set<WeeklyHourNorm>();
    public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();
    public DbSet<ReportCardRemark> ReportCardRemarks => Set<ReportCardRemark>();
    public DbSet<DisciplineRecord> DisciplineRecords => Set<DisciplineRecord>();
    public DbSet<AbsenceJustification> AbsenceJustifications => Set<AbsenceJustification>();
    public DbSet<LateArrival> LateArrivals => Set<LateArrival>();
    public DbSet<TeacherAttendance> TeacherAttendances => Set<TeacherAttendance>();
    public DbSet<EmployeeContract> EmployeeContracts => Set<EmployeeContract>();
    public DbSet<EmployeeContractHistory> EmployeeContractHistories => Set<EmployeeContractHistory>();
    public DbSet<FichePaie> FichePaies => Set<FichePaie>();
    public DbSet<TaxeDeclaration> TaxeDeclarations => Set<TaxeDeclaration>();
    public DbSet<EarlyDeparture> EarlyDepartures => Set<EarlyDeparture>();
    public DbSet<ParentSummons> ParentSummons => Set<ParentSummons>();
    public DbSet<FinancialCommitment> FinancialCommitments => Set<FinancialCommitment>();
    public DbSet<TeacherHourRecord> TeacherHourRecords => Set<TeacherHourRecord>();
    public DbSet<FeeInstallmentPlan> FeeInstallmentPlans => Set<FeeInstallmentPlan>();
    public DbSet<FeeInstallment> FeeInstallments => Set<FeeInstallment>();
    public DbSet<DebtorReminderBatch> DebtorReminderBatches => Set<DebtorReminderBatch>();
    public DbSet<DebtorReminderBatchItem> DebtorReminderBatchItems => Set<DebtorReminderBatchItem>();

    // ------------------------------------------------------------------ Module Coran/Franco-Arabe (socle)
    public DbSet<QuranProgress> QuranProgresses => Set<QuranProgress>();
    public DbSet<QuranEvaluation> QuranEvaluations => Set<QuranEvaluation>();

    // Module Internat/Daara : Oustaz (Halqa) et suivi coranique par Hizb.
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<StudentHizbStatus> StudentHizbStatuses => Set<StudentHizbStatus>();
    public DbSet<StudentHizbEvaluation> StudentHizbEvaluations => Set<StudentHizbEvaluation>();

    // Module Inventaire (patrimoine, stock, prêts de matériel).
    public DbSet<InventoryCategory> InventoryCategories => Set<InventoryCategory>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ItemAssignment> ItemAssignments => Set<ItemAssignment>();

    // Module Examens officiels (CFEE/BFEM/BAC).
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<ExamDossier> ExamDossiers => Set<ExamDossier>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<StudentMutationCertificate> StudentMutationCertificates => Set<StudentMutationCertificate>();

    // Console Super Admin — entités SANS CLÉ, jamais gérées par les migrations (voir OnModelCreating) :
    // la première est adossée à une vue réelle, la seconde n'existe qu'à travers FromSqlRaw.
    public DbSet<PlatformDashboardStats> PlatformDashboardStats => Set<PlatformDashboardStats>();
    public DbSet<GlobalAuditLogEntry> GlobalAuditLogEntries => Set<GlobalAuditLogEntry>();
    public DbSet<MutationCertificateVerification> MutationCertificateVerifications => Set<MutationCertificateVerification>();
    public DbSet<PlatformSubscriptionRow> PlatformSubscriptions => Set<PlatformSubscriptionRow>();

    /// <summary>Annuaire public (B2C) — vue en lecture seule, voir <see cref="PublicSchoolListing"/>.</summary>
    public DbSet<PublicSchoolListing> PublicSchoolDirectory => Set<PublicSchoolListing>();

    // Compteurs de matricules : écrits uniquement par MatriculeGenerator (INSERT ... ON CONFLICT),
    // jamais manipulés à la main par un Handler. Volontairement absent d'IApplicationDbContext.
    public DbSet<MatriculeSequence> MatriculeSequences => Set<MatriculeSequence>();

    // Refresh tokens : manipulés uniquement par AuthStore (ticket JGK-A04). Hors IApplicationDbContext,
    // aucun Handler métier n'a de raison d'y toucher.
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Jetons de réinitialisation self-service. Comme <see cref="RefreshTokens"/>, hors du contrat
    /// IApplicationDbContext : ce chemin s'exécute sans tenant et n'est atteint que par AuthStore.
    /// </summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // `citext` : type e-mail insensible à la casse pour `users.Email` (voir UserConfiguration).
        // Extension « trusted » depuis PostgreSQL 13 — le rôle propriétaire des migrations peut
        // l'installer sans être superutilisateur. La migration émet CREATE EXTENSION IF NOT EXISTS.
        modelBuilder.HasPostgresExtension("citext");

        // Console Super Admin (contournement RLS auditée, AGENTS.md règle #2) : deux entités SANS CLÉ.
        // La première lit la vue `v_platform_dashboard_stats` (créée par migration, OWNER sama_ecole,
        // security_invoker = false) ; la seconde n'a AUCUNE table/vue propre — elle n'existe que comme
        // forme de résultat pour FromSqlRaw(get_global_audit_logs(...)), ToView(null) l'exclut donc des
        // migrations tout en la gardant interrogeable.
        modelBuilder.Entity<PlatformDashboardStats>(e =>
        {
            e.HasNoKey();
            e.ToView("v_platform_dashboard_stats");
        });

        modelBuilder.Entity<GlobalAuditLogEntry>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
        });

        // Vérification publique d'un certificat de mutation : même mécanisme que GlobalAuditLogEntry —
        // entité SANS CLÉ, sans table/vue propre, interrogée uniquement via FromSqlRaw sur la fonction
        // SECURITY DEFINER `verify_mutation_certificate` (migration AddStateIntegrationModule).
        modelBuilder.Entity<MutationCertificateVerification>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
        });

        // Troisième entité SANS CLÉ de la console Super Admin : lit `v_platform_subscriptions`
        // (créée par migration, OWNER sama_ecole, security_invoker = false), même mécanisme que
        // PlatformDashboardStats.
        modelBuilder.Entity<PlatformSubscriptionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("v_platform_subscriptions");
        });

        // Annuaire PUBLIC (B2C) — quatrième entité SANS CLÉ, même mécanisme : lit
        // `public_school_directory` (migration AddPublicSchoolDirectory), qui fige la liste des
        // colonnes ET la condition de consentement. C'est cette vue, et non le code appelant, qui
        // garantit qu'aucune école sans consentement ni aucune colonne sensible n'est atteignable.
        modelBuilder.Entity<PublicSchoolListing>(e =>
        {
            e.HasNoKey();
            e.ToView("public_school_directory");
        });

        // Le verrou optimiste xmin du barème (ClassFee, AGENTS.md règle #5) est configuré dans
        // ClassFeeConfiguration : une propriété fantôme uint marquée IsRowVersion, que la convention
        // Npgsql mappe automatiquement sur la colonne système xmin sans générer de migration.

        // Applique automatiquement le filtre SchoolId à toute entité ITenantEntity,
        // pour ne pas dépendre de la discipline de chaque développeur/agent à chaque ajout de table.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(ApplicationDbContext)
                    .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : AuditableEntity, ITenantEntity
    {
        // Combine isolation tenant ET soft delete en un seul filtre : EF Core n'autorise
        // qu'un HasQueryFilter par entité, le second appel écraserait le premier.
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.SchoolId == tenantProvider.CurrentSchoolId && !e.IsDeleted);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Verrou optimiste xmin (AGENTS.md règle #5) : la ligne visée a changé depuis sa lecture,
            // l'UPDATE « WHERE xmin = <valeur lue> » n'a donc touché aucune ligne. Ce catch doit
            // précéder celui de DbUpdateException — dont il hérite — sinon il ne serait jamais atteint.
            var entry = ex.Entries.FirstOrDefault();
            var table = entry?.Metadata.GetTableName() ?? "inconnue";
            var key = entry?.Entity is AuditableEntity audited ? audited.Id.ToString() : "inconnue";

            throw new ConcurrencyConflictException(table, key);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Traduit ici, et pas dans les Handlers : SamaEcole.Application ne doit pas connaître Npgsql.
            // Une violation d'unicité est un conflit d'écriture -> 409, jamais un 500 ni un doublon
            // silencieux (AGENTS.md règle #5, docs/Volume_4_API_Design.md §0.4).
            //
            // Le message vient du catalogue et non des noms Npgsql : `pg.TableName` et
            // `pg.ConstraintName` partaient jusqu'ici tels quels jusqu'à l'écran de l'utilisateur, qui
            // lisait un nom de table et un nom d'index tronqué par PostgreSQL. Ces deux valeurs restent
            // disponibles pour les journaux, via TechnicalDetail.
            throw new DuplicateRecordException(
                UniqueConstraintCatalog.Describe(pg.ConstraintName, pg.TableName),
                $"{pg.TableName ?? "table inconnue"} / {pg.ConstraintName ?? "contrainte inconnue"}");
        }
    }

    /// <summary>
    /// Positionne le jeton de concurrence (xmin) ATTENDU par le client sur une entité déjà suivie.
    /// Le prochain SaveChangesAsync comparera cette valeur à celle en base : si la ligne a changé
    /// entre-temps, il refusera en 409 plutôt que d'écraser (AGENTS.md règle #5). Encapsulé ici pour
    /// que SamaEcole.Application n'ait pas à manipuler l'API de suivi d'EF Core ni à connaître xmin.
    /// </summary>
    public void SetOriginalConcurrencyToken<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class
        => Entry(entity).Property("xmin").OriginalValue = expectedVersion;

    /// <summary>
    /// Console Super Admin — une page du journal d'audit toutes écoles confondues, via la fonction
    /// SECURITY DEFINER `get_global_audit_logs` (migrations AddPlatformAdminViews puis
    /// ExtendGlobalAuditLogsFilters). IgnoreQueryFilters() est un no-op ici (GlobalAuditLogEntry,
    /// ToView(null), n'a aucun HasQueryFilter), gardé explicite pour documenter que cette lecture est
    /// volontairement hors du cloisonnement tenant.
    ///
    /// Paramètres NOMMÉS avec un NpgsqlDbType explicite plutôt que des `{0}` positionnels : un `null`
    /// C# passé tel quel à FromSqlRaw n'a pas de type Postgres déterminable pour une fonction
    /// surchargeable, et Npgsql échoue à résoudre l'appel (« could not determine polymorphic type »).
    /// Même prudence que SubscriptionAdminStore avec DBNull.Value.
    /// </summary>
    public async Task<IReadOnlyList<GlobalAuditLogEntry>> GetGlobalAuditLogsAsync(
        int limit, int offset, string? module, bool? success, Guid? schoolId,
        DateTimeOffset? dateFrom, DateTimeOffset? dateTo, CancellationToken cancellationToken)
    {
        var parameters = new[]
        {
            new NpgsqlParameter("limit_val", NpgsqlTypes.NpgsqlDbType.Integer) { Value = limit },
            new NpgsqlParameter("offset_val", NpgsqlTypes.NpgsqlDbType.Integer) { Value = offset },
            new NpgsqlParameter("module_filter", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)module ?? DBNull.Value },
            new NpgsqlParameter("success_filter", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = (object?)success ?? DBNull.Value },
            new NpgsqlParameter("school_id_filter", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)schoolId ?? DBNull.Value },
            new NpgsqlParameter("date_from", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)dateFrom ?? DBNull.Value },
            new NpgsqlParameter("date_to", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)dateTo ?? DBNull.Value }
        };

        return await GlobalAuditLogEntries
            .FromSqlRaw(
                """
                SELECT * FROM public.get_global_audit_logs(
                    @limit_val, @offset_val, @module_filter, @success_filter, @school_id_filter, @date_from, @date_to)
                """,
                parameters)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Vérification publique d'un certificat de mutation (Volume 1 §23.5) — la fonction SECURITY
    /// DEFINER `verify_mutation_certificate` s'exécute hors RLS pour répondre à un appel anonyme, au
    /// périmètre minimal (aucune donnée d'élève). Zéro ligne = code inconnu, renvoyé comme `null`.
    /// </summary>
    public async Task<MutationCertificateVerification?> VerifyMutationCertificateAsync(
        string verificationCode, CancellationToken cancellationToken)
        => await MutationCertificateVerifications
            .FromSqlRaw("SELECT * FROM public.verify_mutation_certificate({0})", verificationCode)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc cref="IApplicationDbContext.ToListOrEmptyOnMissingTableAsync{T}" />
    public async Task<IReadOnlyList<T>> ToListOrEmptyOnMissingTableAsync<T>(
        IQueryable<T> query, CancellationToken cancellationToken)
    {
        try
        {
            return await query.ToListAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // TableName n'est pas peuplé par Postgres pour cette classe d'erreur (contrairement aux
            // violations de contrainte) : MessageText, lui, cite toujours la relation manquante.
            logger.LogWarning(ex,
                "Lecture impossible : {PostgresMessage}. Les migrations EF sont-elles à jour sur cet " +
                "environnement ? Lancez 'dotnet ef database update -p src/SamaEcole.Persistence " +
                "-s src/SamaEcole.Web'. Liste vide renvoyée en attendant.",
                ex.MessageText);
            return [];
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        // Transaction déjà ouverte par l'appelant (ex. test d'intégration qui rollback) : on s'y greffe,
        // sinon la transaction imbriquée committerait un travail que l'appelant voulait pouvoir annuler.
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        // ExecutionStrategy : rejoue toute l'opération en cas d'erreur transitoire, transaction comprise.
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}
````


#### `src/SamaEcole.Persistence/MatriculeGenerator.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Persistence;

/// <summary>
/// Numérotation séquentielle par établissement (docs/Volume_1_Cahier_des_Charges.md §2.2).
///
/// Le GABARIT vient des paramètres de l'école (ticket JGK-B02 : studentMatriculeFormat /
/// teacherMatriculeFormat), il n'est plus codé en dur. Une école qui n'a pas encore touché à ses
/// réglages retombe sur les valeurs par défaut — « ELEV-{YEAR}-{SEQ:4} », « ENS-{YEAR}-{SEQ:3} ».
///
/// Concurrence : le compteur est incrémenté par un unique INSERT ... ON CONFLICT DO UPDATE
/// ... RETURNING. PostgreSQL pose un verrou de ligne sur le compteur : deux inscriptions
/// simultanées dans la même école sont sérialisées, jamais servies avec le même numéro.
///
/// Absence de trou : l'incrément s'exécute dans la transaction ouverte par le Handler
/// (AGENTS.md règle #3). Si l'insertion de l'élève échoue, l'incrément est annulé avec elle.
/// C'est la raison pour laquelle une SEQUENCE PostgreSQL n'est PAS utilisée ici : une séquence
/// ne se rembobine pas en cas de rollback et laisserait un trou dans la numérotation.
/// </summary>
public class MatriculeGenerator(ApplicationDbContext dbContext, TimeProvider timeProvider) : IMatriculeGenerator
{
    /// <summary>
    /// Gabarit FIXE des numéros de reçu (ticket JGK-E02), contrairement aux matricules dont le format
    /// est paramétrable par l'école : un reçu est une pièce officielle, sa forme ne se règle pas. Rendu
    /// par le même moteur que les matricules — « REC-2025-0002 ».
    /// </summary>
    private const string ReceiptNumberFormat = "REC-{YEAR}-{SEQ:4}";

    /// <summary>Gabarit FIXE des certificats de mutation (ticket JGK-M06) — même raison que le reçu.</summary>
    private const string MutationCertificateNumberFormat = "MUT-{YEAR}-{SEQ:4}";

    public Task<string> GenerateNextStudentMatriculeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        GenerateAsync(schoolId, MatriculeKind.Student, cancellationToken);

    public Task<string> GenerateNextTeacherMatriculeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        GenerateAsync(schoolId, MatriculeKind.Teacher, cancellationToken);

    public async Task<string> GenerateNextReceiptNumberAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // Même année SCOLAIRE que le matricule généré au même instant dans la même transaction : le reçu
        // et le matricule d'une inscription portent donc toujours le même millésime.
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, MatriculeKind.Receipt, year, cancellationToken);

        return MatriculeFormat.Render(ReceiptNumberFormat, year, next);
    }

    public async Task<string> GenerateNextMutationCertificateNumberAsync(
        Guid schoolId, CancellationToken cancellationToken)
    {
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, MatriculeKind.MutationCertificate, year, cancellationToken);

        return MatriculeFormat.Render(MutationCertificateNumberFormat, year, next);
    }

    /// <summary>
    /// Prochaine valeur du compteur d'IEN provisoires de l'école — exposée à
    /// <see cref="NationalIenGenerator"/>, qui compose le numéro final avec le code établissement.
    /// La séquence vit ici, et non dans le générateur d'IEN, pour hériter telle quelle de la
    /// sérialisation sous concurrence et de l'annulation sur rollback décrites en tête de classe.
    /// </summary>
    internal Task<int> NextProvisionalIenSequenceAsync(
        Guid schoolId, int year, CancellationToken cancellationToken) =>
        NextValueAsync(schoolId, MatriculeKind.ProvisionalIen, year, cancellationToken);

    private async Task<string> GenerateAsync(
        Guid schoolId,
        MatriculeKind kind,
        CancellationToken cancellationToken)
    {
        var format = await ResolveFormatAsync(schoolId, kind, cancellationToken);

        // Année SCOLAIRE (bascule en octobre), pas année civile — voir AcademicYear.
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, kind, year, cancellationToken);

        return MatriculeFormat.Render(format, year, next);
    }

    private async Task<string> ResolveFormatAsync(
        Guid schoolId,
        MatriculeKind kind,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + filtre explicite : le générateur est appelé dans la transaction
        // d'inscription, où le tenant EST celui de l'école — mais aussi par le provisionnement, où
        // le filtre global ne serait pas satisfait. On filtre donc soi-même, sans se reposer dessus.
        var settings = await dbContext.SchoolSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId && !s.IsDeleted, cancellationToken);

        // Aucun réglage enregistré (école antérieure à JGK-B02) : on retombe sur les valeurs par
        // défaut plutôt que d'échouer. Une inscription ne doit pas dépendre d'un écran de réglages
        // que personne n'a encore ouvert.
        return kind switch
        {
            MatriculeKind.Student => settings?.StudentMatriculeFormat ?? SchoolSettingsDefaults.StudentMatriculeFormat,
            MatriculeKind.Teacher => settings?.TeacherMatriculeFormat ?? SchoolSettingsDefaults.TeacherMatriculeFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Type de matricule inconnu.")
        };
    }

    private async Task<int> NextValueAsync(
        Guid schoolId,
        MatriculeKind kind,
        int year,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var kindText = kind.ToString();
        var now = timeProvider.GetUtcNow();

        // SqlQuery<int> paramètre automatiquement les trous d'interpolation (aucune concaténation SQL).
        // L'alias "Value" est imposé par EF Core pour projeter un scalaire.
        //
        // ToListAsync, et surtout PAS SingleAsync : un INSERT ... RETURNING n'est pas composable,
        // et SingleAsync tenterait de l'envelopper dans un SELECT ... LIMIT — EF Core lève alors
        // « non-composable SQL ». On matérialise donc la ligne unique renvoyée, puis on la valide.
        var values = await dbContext.Database.SqlQuery<int>(
            $"""
             INSERT INTO matricule_sequences ("Id", "SchoolId", "Kind", "Year", "LastValue", "CreatedAt", "IsDeleted")
             VALUES ({id}, {schoolId}, {kindText}, {year}, 1, {now}, FALSE)
             ON CONFLICT ("SchoolId", "Kind", "Year")
             DO UPDATE SET "LastValue" = matricule_sequences."LastValue" + 1,
                           "UpdatedAt" = {now}
             RETURNING "LastValue" AS "Value"
             """)
            .ToListAsync(cancellationToken);

        return values.Single();
    }
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IMatriculeGenerator.cs`

````csharp
namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère le prochain matricule séquentiel pour une école donnée, à l'intérieur de la même
/// transaction que l'insertion (AGENTS.md règle #3, ticket JGK-D01). L'implémentation
/// (SamaEcole.Persistence) doit garantir l'absence de trou/doublon sous concurrence
/// (ex. verrou transactionnel ou séquence PostgreSQL dédiée par école).
/// </summary>
public interface IMatriculeGenerator
{
    Task<string> GenerateNextStudentMatriculeAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<string> GenerateNextTeacherMatriculeAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Prochain numéro de reçu d'inscription (ticket JGK-E02, ex. « REC-2025-0002 »). Même contrat que
    /// les matricules : appelé DANS la transaction d'inscription, gapless et unique par établissement.
    /// </summary>
    Task<string> GenerateNextReceiptNumberAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Prochain numéro de certificat de mutation (ticket JGK-M06, ex. « MUT-2026-0007 »). Gabarit FIXE
    /// comme celui du reçu, et pour la même raison : la forme d'une pièce officielle opposable ne se
    /// règle pas dans un écran de paramètres.
    /// </summary>
    Task<string> GenerateNextMutationCertificateNumberAsync(Guid schoolId, CancellationToken cancellationToken);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`

````csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Abstraction du DbContext exposée à Application, implémentée par SamaEcole.Persistence.
/// Application ne référence jamais EF Core directement en dehors de ce contrat minimal.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<School> Schools { get; }
    DbSet<Student> Students { get; }
    DbSet<Classroom> Classrooms { get; }

    /// <summary>
    /// Annuaire PUBLIC des établissements (B2C) — vue en LECTURE SEULE <c>public_school_directory</c>,
    /// jamais la table <c>schools</c>. La vue fige les colonnes exposées et la condition de consentement
    /// (voir <see cref="PublicSchoolListing"/> et la migration AddPublicSchoolDirectory) : c'est elle,
    /// et non le Handler, qui garantit qu'aucune donnée sensible ni aucune école non consentante n'est
    /// atteignable par ce chemin anonyme.
    /// </summary>
    DbSet<PublicSchoolListing> PublicSchoolDirectory { get; }

    /// <summary>Bâtiments physiques de l'établissement (module Infrastructures).</summary>
    DbSet<Building> Buildings { get; }

    /// <summary>Salles physiques d'un bâtiment (module Infrastructures) — indépendant de Classroom.</summary>
    DbSet<Room> Rooms { get; }

    /// <summary>Années scolaires (ticket JGK-C01) : le pivot des inscriptions, des frais et des bulletins.</summary>
    DbSet<SchoolYear> SchoolYears { get; }

    /// <summary>Matières et coefficients par niveau (ticket JGK-C03). Le coefficient pilote les bulletins.</summary>
    DbSet<Subject> Subjects { get; }

    /// <summary>Catégories de frais paramétrables (ticket JGK-F01) : inscription, mensualité, cantine…</summary>
    DbSet<FeeCategory> FeeCategories { get; }

    /// <summary>Barème : le montant d'une catégorie pour une classe (ticket JGK-F01). Verrou optimiste xmin.</summary>
    DbSet<ClassFee> ClassFees { get; }

    /// <summary>Journal append-only des changements de barème (ticket JGK-F01) : on y AJOUTE, jamais plus.</summary>
    DbSet<FeeChangeHistory> FeeChangeHistory { get; }

    /// <summary>Inscriptions/réinscriptions (ticket JGK-E01). Portent le TotalDue calculé. Verrou optimiste xmin.</summary>
    DbSet<Enrollment> Enrollments { get; }

    /// <summary>Détail figé des frais d'une inscription (ticket JGK-E01) : l'instantané du barème pour le reçu.</summary>
    DbSet<EnrollmentFeeLine> EnrollmentFeeLines { get; }

    /// <summary>Encaissements de caisse (ticket JGK-F02). Le solde vit sur l'inscription (verrou optimiste xmin).</summary>
    DbSet<Payment> Payments { get; }

    DbSet<CashierSession> CashierSessions { get; }

    DbSet<PaymentBreakdown> PaymentBreakdowns { get; }

    DbSet<User> Users { get; }
    DbSet<Subscription> Subscriptions { get; }

    /// <summary>Souscription commerciale de l'école (profil, tranche d'effectif, modules) : table tenant, une ligne vivante par école.</summary>
    DbSet<TenantSubscription> TenantSubscriptions { get; }

    /// <summary>Demandes de changement d'abonnement de l'école (profil, cycles, tranche, modules) : table tenant.</summary>
    DbSet<SubscriptionChangeRequest> SubscriptionChangeRequests { get; }

    /// <summary>Codes promo (module Tarification &amp; Promotions) : table plateforme, comme Subscriptions/Schools.</summary>
    DbSet<PromoCode> PromoCodes { get; }

    /// <summary>Prix publié de chaque tranche d'effectif tarifée : table plateforme, éditée par le Super Admin.</summary>
    DbSet<QuotaTierPrice> QuotaTierPrices { get; }

    /// <summary>Historique des SMS envoyés (offre Premium) : table tenant, une ligne par tentative.</summary>
    DbSet<SmsMessage> SmsMessages { get; }

    /// <summary>
    /// Établissements supplémentaires d'un utilisateur (groupe scolaire). Table plateforme, hors RLS
    /// par nécessité — voir UserSchool.
    /// </summary>
    DbSet<UserSchool> UserSchools { get; }
    DbSet<ScheduleSlot> ScheduleSlots { get; }
    DbSet<Disbursement> Disbursements { get; }

    /// <summary>
    /// Demandes d'inscription self-service (ticket JGK-I01) : table plateforme (hors RLS/tenant) qui
    /// PRÉCÈDE l'existence de l'école. Écrite par le formulaire public anonyme.
    /// </summary>
    DbSet<SchoolRegistrationRequest> SchoolRegistrationRequests { get; }

    /// <summary>Paiements d'abonnement (ticket JGK-I05) : table tenant normale, écrite par le Directeur lui-même.</summary>
    DbSet<SubscriptionPayment> SubscriptionPayments { get; }

    /// <summary>Journal append-only des changements de statut (ticket JGK-A05) : on y AJOUTE, jamais plus.</summary>
    DbSet<UserStatusHistory> UserStatusHistory { get; }

    /// <summary>Paramètres d'établissement (ticket JGK-B02).</summary>
    DbSet<SchoolSettings> SchoolSettings { get; }

    /// <summary>
    /// Compteurs de matricules par (école, type, année). Incrémentés par
    /// <c>SamaEcole.Persistence.MatriculeGenerator</c> ; exposés ici pour que le Directeur puisse
    /// fixer le numéro de départ d'une année (SetMatriculeSequenceStartCommand).
    /// </summary>
    DbSet<MatriculeSequence> MatriculeSequences { get; }

    /// <summary>Journal d'audit append-only (ticket JGK-H01) : on y AJOUTE, jamais plus.</summary>
    DbSet<AuditLog> AuditLogs { get; }

    /// <summary>Trimestres d'une année scolaire (ticket JGK-G01), générés automatiquement à sa création.</summary>
    DbSet<Term> Terms { get; }

    /// <summary>Fermetures du calendrier (fériés, vacances) d'une année scolaire — cahier de roulement (Franco-Arabe Bloc 5).</summary>
    DbSet<SchoolClosure> SchoolClosures { get; }

    /// <summary>Saisies du cahier de roulement (une par élève et demi-journée) : elles priment sur le statut dérivé des appels (Franco-Arabe Bloc 5b).</summary>
    DbSet<RollBookEntry> RollBookEntries { get; }

    /// <summary>Notes (ticket JGK-G01) : une par (élève, matière, trimestre, type d'évaluation). Verrou optimiste xmin.</summary>
    DbSet<Grade> Grades { get; }

    /// <summary>Mentions personnalisables dérivées de la moyenne générale (ticket JGK-G02).</summary>
    DbSet<Mention> Mentions { get; }

    /// <summary>Enseignants (ticket JGK-D03). Matricule généré par le Handler, même règle que Students.</summary>
    DbSet<Teacher> Teachers { get; }

    /// <summary>Matières qu'un enseignant est qualifié à enseigner (ticket JGK-D03).</summary>
    DbSet<TeacherSubject> TeacherSubjects { get; }

    /// <summary>Attributions classe/matière/année d'un enseignant (ticket JGK-D04) — sert d'historique.</summary>
    DbSet<TeacherAssignment> TeacherAssignments { get; }

    /// <summary>Fiches d'appel : un appel par (classe, matière, date, créneau) (ticket JGK-D06).</summary>
    DbSet<AttendanceSheet> AttendanceSheets { get; }

    /// <summary>Cahier de texte : une entrée de journal par séance réellement tenue (ticket JGK-P04).</summary>
    DbSet<ClassJournalEntry> ClassJournalEntries { get; }

    /// <summary>Surcharges de coefficient par série ou par classe (Évolution N°4).</summary>
    DbSet<SubjectCoefficientOverride> SubjectCoefficientOverrides { get; }

    /// <summary>Matières au programme d'une classe, avec leur groupe d'options (Évolution N°6).</summary>
    DbSet<ClassSubject> ClassSubjects { get; }

    /// <summary>Choix d'option d'un élève pour une année scolaire (Évolution N°6).</summary>
    DbSet<StudentSubjectEnrollment> StudentSubjectEnrollments { get; }

    /// <summary>Dispenses d'un élève pour une matière obligatoire, par année scolaire (motif sensible).</summary>
    DbSet<StudentSubjectExemption> StudentSubjectExemptions { get; }

    // --- Internat (modèle Pavillon/Lit, spec 2026-10-06) ---
    DbSet<Dormitory> Dormitories { get; }

    DbSet<DormitoryRoom> DormitoryRooms { get; }

    DbSet<Bed> Beds { get; }

    DbSet<BoardingEnrollment> BoardingEnrollments { get; }

    DbSet<BoardingLeave> BoardingLeaves { get; }

    DbSet<BoardingAttendance> BoardingAttendances { get; }
    DbSet<AttendanceSession> AttendanceSessions { get; }

    /// <summary>Tranches d'âge par niveau propres à l'école (Évolution N°7, cartographie IEF).</summary>
    DbSet<GradeAgeNorm> GradeAgeNorms { get; }

    /// <summary>Programme officiel d'une matière pour un niveau : chapitres/objectifs (Évolution N°7).</summary>
    DbSet<SyllabusUnit> SyllabusUnits { get; }

    /// <summary>Unités du programme traitées pendant une séance du cahier de texte (Évolution N°7).</summary>
    DbSet<ClassJournalEntryUnit> ClassJournalEntryUnits { get; }

    /// <summary>Volumes horaires hebdomadaires propres à l'école, par niveau/série et matière (Évolution N°7).</summary>
    DbSet<WeeklyHourNorm> WeeklyHourNorms { get; }

    /// <summary>Statut de chaque élève sur une fiche d'appel (ticket JGK-D06).</summary>
    DbSet<StudentAttendance> StudentAttendances { get; }

    /// <summary>Distinction cochée + observations du conseil des professeurs, par (élève, trimestre) — bulletin JGK-G03.</summary>
    DbSet<ReportCardRemark> ReportCardRemarks { get; }

    DbSet<DisciplineRecord> DisciplineRecords { get; }
    DbSet<AbsenceJustification> AbsenceJustifications { get; }
    DbSet<LateArrival> LateArrivals { get; }
    DbSet<TeacherAttendance> TeacherAttendances { get; }
    DbSet<EarlyDeparture> EarlyDepartures { get; }
    DbSet<ParentSummons> ParentSummons { get; }

    DbSet<EmployeeContract> EmployeeContracts { get; }

    /// <summary>Journal append-only des changements de contrat (Volume 1 §14.1) : on y AJOUTE, jamais plus.</summary>
    DbSet<EmployeeContractHistory> EmployeeContractHistories { get; }
    DbSet<FichePaie> FichePaies { get; }
    DbSet<TaxeDeclaration> TaxeDeclarations { get; }
    DbSet<FinancialCommitment> FinancialCommitments { get; }
    DbSet<TeacherHourRecord> TeacherHourRecords { get; }

    /// <summary>Échéancier personnalisé d'une inscription (Étape 5 — recouvrement). Au plus un actif par inscription.</summary>
    DbSet<FeeInstallmentPlan> FeeInstallmentPlans { get; }

    /// <summary>Échéances d'un FeeInstallmentPlan — montants et dates librement négociés.</summary>
    DbSet<FeeInstallment> FeeInstallments { get; }

    /// <summary>Lot de relance de débiteurs généré chaque nuit par classe (Étape 5 — recouvrement semi-automatique).</summary>
    DbSet<DebtorReminderBatch> DebtorReminderBatches { get; }

    /// <summary>Débiteurs candidats d'un DebtorReminderBatch, avec leur ancienneté de retard au moment de la génération.</summary>
    DbSet<DebtorReminderBatchItem> DebtorReminderBatchItems { get; }

    // ------------------------------------------------------------------ Module Coran/Franco-Arabe (socle)

    /// <summary>Suivi individuel de mémorisation coranique (module Coran/Franco-Arabe). Verrou optimiste xmin.</summary>
    DbSet<QuranProgress> QuranProgresses { get; }

    /// <summary>Notes d'examen oral de récitation coranique (module Coran/Franco-Arabe). Verrou optimiste xmin.</summary>
    DbSet<QuranEvaluation> QuranEvaluations { get; }

    /// <summary>Oustaz (responsables de Halqa) du module Internat/Daara.</summary>
    DbSet<Instructor> Instructors { get; }

    /// <summary>État courant d'un élève sur chaque Hizb (module Internat/Daara). Verrou optimiste xmin.</summary>
    DbSet<StudentHizbStatus> StudentHizbStatuses { get; }

    /// <summary>Journal append-only des évaluations Hizb (historique et courbe d'évolution). Jamais modifié ni supprimé.</summary>
    DbSet<StudentHizbEvaluation> StudentHizbEvaluations { get; }

    // ------------------------------------------------------------------ Module Inventaire

    /// <summary>Familles de biens (Mobilier, Manuels scolaires, Informatique…), propres à chaque école.</summary>
    DbSet<InventoryCategory> InventoryCategories { get; }

    /// <summary>
    /// Lots de biens du patrimoine. <c>QuantityAvailable</c> est un compteur DÉRIVÉ : il ne se met à
    /// jour que dans la transaction d'un <see cref="StockMovements">mouvement</see>, sous verrou xmin.
    /// </summary>
    DbSet<InventoryItem> InventoryItems { get; }

    /// <summary>Journal APPEND-ONLY des mouvements de stock : on y AJOUTE, jamais plus (comme FeeChangeHistory).</summary>
    DbSet<StockMovement> StockMovements { get; }

    /// <summary>Fiches de prêt/attribution de matériel aux élèves, enseignants et personnel. Verrou optimiste xmin.</summary>
    DbSet<ItemAssignment> ItemAssignments { get; }

    // ------------------------------------------------------------------ Module Examens officiels

    /// <summary>Campagnes d'examen (CFEE/BFEM/BAC) de l'école, par année scolaire et série.</summary>
    DbSet<ExamSession> ExamSessions { get; }

    /// <summary>Dossiers de candidature. Un élève n'a qu'un dossier par session (index unique).</summary>
    DbSet<ExamDossier> ExamDossiers { get; }

    /// <summary>Résultats/mentions à la délibération. Au plus un résultat par dossier.</summary>
    DbSet<ExamResult> ExamResults { get; }

    /// <summary>
    /// Certificats de mutation délivrés (module Intégration étatique, ticket JGK-M06). Table tenant.
    /// Une ligne par pièce remise : c'est elle qui rend le QR code vérifiable et la révocation
    /// possible — voir <see cref="StudentMutationCertificate"/>.
    /// </summary>
    DbSet<StudentMutationCertificate> StudentMutationCertificates { get; }

    /// <summary>
    /// Agrégats plateforme (console Super Admin) : entité SANS CLÉ adossée à la vue PostgreSQL
    /// `v_platform_dashboard_stats`, qui contourne la RLS via `security_invoker = false` +
    /// OWNER sama_ecole (AGENTS.md règle #2, docs/Volume_7_Security.md §8).
    /// </summary>
    DbSet<PlatformDashboardStats> PlatformDashboardStats { get; }

    /// <summary>
    /// Abonnements de toutes les écoles (console Super Admin) : entité SANS CLÉ adossée à la vue
    /// PostgreSQL `v_platform_subscriptions`, même mécanisme que PlatformDashboardStats.
    /// </summary>
    DbSet<PlatformSubscriptionRow> PlatformSubscriptions { get; }

    /// <summary>
    /// Journal d'audit toutes écoles confondues (console Super Admin), une page à la fois — appelle la
    /// fonction SECURITY DEFINER `get_global_audit_logs` (migrations AddPlatformAdminViews puis
    /// ExtendGlobalAuditLogsFilters pour les 5 filtres) via FromSqlRaw. Encapsulé ici (comme
    /// SetOriginalConcurrencyToken/ExecuteInTransactionAsync) : FromSqlRaw exige le package EF Core
    /// Relational, volontairement absent de SamaEcole.Application (règle #1 — seul SamaEcole.Persistence
    /// référence un provider/l'infrastructure relationnelle). Chaque filtre à `null` désactive sa
    /// condition côté SQL (voir la fonction) — même convention que GetAuditLogsQuery côté tenant.
    /// </summary>
    Task<IReadOnlyList<GlobalAuditLogEntry>> GetGlobalAuditLogsAsync(
        int limit, int offset, string? module, bool? success, Guid? schoolId,
        DateTimeOffset? dateFrom, DateTimeOffset? dateTo, CancellationToken cancellationToken);

    /// <summary>
    /// Vérification PUBLIQUE d'un certificat de mutation scanné depuis son QR (Volume 1 §23.5) —
    /// appelle la fonction SECURITY DEFINER `verify_mutation_certificate` (migration
    /// AddStateIntegrationModule) via FromSqlRaw, même encapsulation que
    /// <see cref="GetGlobalAuditLogsAsync"/>.
    ///
    /// Renvoie <c>null</c> quand aucun certificat ne porte ce code : l'appelant traduit ça en
    /// « inconnu », jamais en erreur. Le résultat ne contient AUCUNE donnée de l'élève.
    /// </summary>
    Task<MutationCertificateVerification?> VerifyMutationCertificateAsync(
        string verificationCode, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Positionne le jeton de concurrence (xmin) ATTENDU par le client sur une entité déjà suivie,
    /// pour que le prochain SaveChangesAsync refuse en 409 si la ligne a changé entre-temps
    /// (AGENTS.md règle #5). Encapsule l'API de suivi d'EF Core : Application n'a pas à connaître xmin.
    /// </summary>
    void SetOriginalConcurrencyToken<TEntity>(TEntity entity, uint expectedVersion) where TEntity : class;

    /// <summary>
    /// Exécute <paramref name="operation"/> dans UNE seule transaction (la crée si aucune n'est
    /// déjà ouverte). Indispensable dès qu'un matricule est généré : il doit l'être dans la même
    /// transaction que l'insertion, sans quoi un échec d'enregistrement laisserait un numéro
    /// consommé — donc un trou dans la numérotation (AGENTS.md règle #3).
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exécute <paramref name="query"/> et retourne une liste vide — plutôt que de laisser remonter
    /// une exception 500 brute — si la table sous-jacente n'existe pas encore (migration EF pas
    /// encore appliquée sur cet environnement). Un avertissement est loggé côté serveur. Encapsulé ici
    /// pour la même raison que <see cref="GetGlobalAuditLogsAsync"/> : seul SamaEcole.Persistence a le
    /// droit de connaître Npgsql (AGENTS.md règle #1). Réservé aux écrans où une liste vide est un état
    /// légitime et sans risque (ex. un module dont l'écran d'accueil ne doit jamais planter si la base
    /// n'a pas encore migré) — ne pas l'utiliser pour masquer une vraie panne de lecture.
    /// </summary>
    Task<IReadOnlyList<T>> ToListOrEmptyOnMissingTableAsync<T>(
        IQueryable<T> query, CancellationToken cancellationToken);
}
````


#### `src/SamaEcole.Application/Common/Exceptions/BusinessRuleException.cs`

````csharp
namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand une règle métier interdit une opération sur une ressource par ailleurs valide et
/// existante (ex. supprimer une classe encore liée à des élèves, annuler une inscription déjà
/// encaissée). Traduite en HTTP 409 par SamaEcole.Web : c'est l'ÉTAT de la ressource qui bloque
/// l'opération, pas la forme de la requête (ValidationException, 422) ni une écriture concurrente
/// (ConcurrencyConflictException, également 409 mais pour une cause différente).
///
/// <paramref name="code"/> est OPTIONNEL : sans lui, l'API renvoie le code générique
/// <c>BUSINESS_RULE_VIOLATION</c> (comportement historique, inchangé). Avec lui, l'API renvoie ce
/// code STABLE — utile quand le client doit distinguer plusieurs refus 409 pour réagir
/// différemment (ex. <c>RESET_UNAVAILABLE_LIVE_MODE</c> vs <c>ALREADY_LIVE</c>).
/// </summary>
public class BusinessRuleException(string message, string? code = null) : Exception(message)
{
    /// <summary>Code d'erreur stable destiné au client, ou <c>null</c> pour le code générique.</summary>
    public string? Code { get; } = code;

    /// <summary>
    /// Détail structuré renvoyé tel quel dans <c>details</c> de la réponse d'erreur (ex. les cycles et le nombre
    /// de classes qui bloquent une désactivation), ou <c>null</c> — le comportement historique, inchangé. Ne doit
    /// jamais porter de table, de contrainte ni de donnée d'un autre établissement.
    /// </summary>
    public object? Details { get; init; }
}
````


#### `src/SamaEcole.Web/Middleware/ExceptionHandlingMiddleware.cs`

````csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Subscriptions;

namespace SamaEcole.Web.Middleware;

/// <summary>
/// Traduit toute exception applicative dans le format d'erreur normalisé de
/// docs/Volume_4_API_Design.md §0.4. AGENTS.md règle #9 : jamais d'exception brute renvoyée au client.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    /// <summary>Mêmes conventions que les contrôleurs : camelCase, énumérations en chaînes (voir Program.cs › AddJsonOptions).</summary>
    private static readonly JsonSerializerOptions PreviewJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Le client a coupé la connexion (onglet fermé, navigation, ou gestionnaire de
            // téléchargement qui intercepte le fetch d'un PDF). Plus personne pour lire une réponse :
            // tenter d'en écrire une relèverait, et journaliser une stack trace en Error noierait les
            // vraies erreurs sous le bruit des annulations. On sort en silence (trace Debug seulement).
            logger.LogDebug("Requête annulée par le client : {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        // La réponse a déjà commencé à partir (en-têtes + une partie du corps envoyés) : impossible
        // d'y écrire une erreur normalisée — `Response.StatusCode = …` lèverait « response has already
        // started ». C'est le cas typique d'un `FileResult` volumineux dont le rendu casse en cours
        // d'écriture. On journalise et on coupe net la connexion : le client verra un transfert
        // incomplet (à retenter) plutôt qu'un corps tronqué présenté comme complet.
        if (context.Response.HasStarted)
        {
            logger.LogError(
                exception,
                "Exception APRÈS le début de la réponse ({Method} {Path}, {Written} octet(s) déjà écrits). " +
                "Connexion coupée — aucune erreur normalisée ne peut plus être renvoyée. TraceId: {TraceId}",
                context.Request.Method, context.Request.Path, context.Response.Headers.ContentLength, traceId);
            context.Abort();
            return;
        }

        var (statusCode, code, message, details) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.UnprocessableEntity,
                "VALIDATION_ERROR",
                "Une ou plusieurs erreurs de validation se sont produites.",
                (object?)validationEx.Errors),

            // Doublon refusé par la base : la ligne existe déjà. Doit précéder le cas
            // ConcurrencyConflictException ci-dessous — dont il hérite — sinon il ne serait jamais
            // atteint, et l'utilisateur relirait « modifié par une autre personne » alors que
            // personne n'a rien modifié. Code distinct pour que le client puisse les différencier.
            DuplicateRecordException duplicateEx => (
                HttpStatusCode.Conflict,
                "DUPLICATE_RECORD",
                duplicateEx.Message,
                null),

            ConcurrencyConflictException concurrencyEx => (
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT",
                concurrencyEx.Message,
                null),

            // Règle métier qui bloque une opération à cause de l'état actuel de la ressource (ex.
            // suppression d'une classe encore liée à des élèves, annulation d'une inscription déjà
            // encaissée) — pas une écriture concurrente, mais un conflit avec l'état existant.
            BusinessRuleException businessRuleEx => (
                HttpStatusCode.Conflict,
                businessRuleEx.Code ?? "BUSINESS_RULE_VIOLATION",
                businessRuleEx.Message,
                businessRuleEx.Details),

            // Modification directe INTERCEPTÉE (profil, cycles, module non souscrit) : elle change l'abonnement, donc elle passe par
            // une demande que le Super Admin valide. 409 : la saisie est valide, c'est l'état de l'abonnement qui s'y oppose.
            // `details` porte l'aperçu (changements, tarif ajusté, corps de la demande) pour la modale universelle de l'interface.
            SubscriptionChangeRequiredException changeEx => (
                HttpStatusCode.Conflict,
                "SUBSCRIPTION_CHANGE_REQUIRED",
                changeEx.Message,
                // Sérialisé comme le reste de l'API (camelCase, énumérations en chaînes) : ce middleware écrit avec les options par
                // défaut de System.Text.Json, qui renverraient « HasChanges » et des cycles en nombres.
                (object?)JsonSerializer.SerializeToElement(changeEx.Preview, PreviewJsonOptions)),

            // Quota d'élèves de la souscription (Onboarding & Pricing SaaS) : 422 — c'est le plan de l'école
            // qui refuse, pas l'état d'une ressource. `details` porte les chiffres (effectif, plafond,
            // tolérance) pour que l'écran les affiche sans second appel.
            QuotaExceededException quotaEx => (
                HttpStatusCode.UnprocessableEntity,
                quotaEx.Denial == StudentAdmissionDenial.SubscriptionNotActive
                    ? "SUBSCRIPTION_NOT_ACTIVE"
                    : "STUDENT_QUOTA_EXCEEDED",
                quotaEx.Message,
                (object?)new
                {
                    currentStudentCount = quotaEx.Quota.CurrentStudentCount,
                    maxStudentLimit = quotaEx.Quota.MaxStudentLimit,
                    softQuotaLimit = quotaEx.Quota.SoftQuotaLimit
                }),

            // Échec d'authentification (ticket JGK-A04). Message volontairement générique : il ne doit
            // jamais permettre de distinguer un e-mail inconnu d'un mot de passe faux.
            InvalidCredentialsException credentialsEx => (
                HttpStatusCode.Unauthorized,
                "INVALID_CREDENTIALS",
                credentialsEx.Message,
                null),

            // Blocage progressif anti-force-brute (docs/Volume_7_Security.md §2). Contrairement à
            // InvalidCredentialsException ci-dessus, révéler ce statut n'ouvre pas d'énumération : le
            // verrou ne se déclenche qu'après plusieurs échecs déjà commis sur CE compte.
            AccountLockedException lockedEx => (
                (HttpStatusCode)StatusCodes.Status429TooManyRequests,
                "ACCOUNT_LOCKED",
                lockedEx.Message,
                (object?)new { retryAfterSeconds = lockedEx.RetryAfterSeconds }),

            // Refus de portée dont le message a été rédigé POUR l'écran (classe non assignée, dossier
            // hors périmètre, créneau d'un collègue) : on le renvoie tel quel, avec l'action
            // corrective — docs/Volume_4_API_Design.md §22. Doit précéder le cas UnauthorizedAccessException
            // ci-dessous, dont il hérite.
            ForbiddenException forbiddenEx => (
                HttpStatusCode.Forbidden,
                "FORBIDDEN",
                forbiddenEx.Message,
                null),

            // Garde interne « ne devrait jamais arriver » (« Tenant is required. », « Utilisateur
            // courant inconnu. »…) : message aplati, il ne doit pas fuir de vocabulaire technique.
            UnauthorizedAccessException => (
                HttpStatusCode.Forbidden,
                "FORBIDDEN",
                "Accès refusé.",
                null),

            OperationCanceledException => (
                (HttpStatusCode)499, // Client Closed Request
                "CLIENT_CLOSED_REQUEST",
                "La requête a été annulée par le client.",
                null),

            NotFoundException => (
                HttpStatusCode.NotFound,
                "NOT_FOUND",
                exception.Message,
                null),

            KeyNotFoundException => (
                HttpStatusCode.NotFound,
                "NOT_FOUND",
                exception.Message,
                null),

            // Ticket JGK-I05 : l'agrégateur de paiement (PayDunya…) a refusé ou est injoignable. 502 —
            // le serveur, agissant comme passerelle, a reçu une réponse invalide de l'amont.
            PaymentProviderException paymentEx => (
                HttpStatusCode.BadGateway,
                "PAYMENT_PROVIDER_ERROR",
                paymentEx.Message,
                null),

            // Envoi manuel d'un bulletin par WhatsApp refusé par Meta (jeton expiré, fenêtre de 24 h,
            // modèle non approuvé, /media absent…). 502, même logique que l'agrégateur de paiement :
            // le serveur passerelle a reçu une réponse invalide de Meta. Le message est déjà rédigé
            // pour l'utilisateur et sans secret (voir WhatsAppDeliveryException).
            WhatsAppDeliveryException whatsAppEx => (
                HttpStatusCode.BadGateway,
                "WHATSAPP_DELIVERY_ERROR",
                whatsAppEx.Message,
                null),

            // Ticket JGK-I06, docs/Volume_7_Security.md §12bis : signature de webhook absente/invalide.
            InvalidWebhookSignatureException webhookEx => (
                HttpStatusCode.Unauthorized,
                "INVALID_WEBHOOK_SIGNATURE",
                webhookEx.Message,
                null),

            _ => (
                HttpStatusCode.InternalServerError,
                "INTERNAL_ERROR",
                "Une erreur inattendue s'est produite.",
                null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Erreur non gérée. TraceId: {TraceId}", traceId);
        }

        // Le conflit d'écriture est le seul cas où le message envoyé au client a été volontairement
        // DÉPOUILLÉ de sa cause technique (table, contrainte). Sans cette trace, plus personne côté
        // serveur ne saurait quelle contrainte a réellement cédé, et le catalogue de messages
        // deviendrait impossible à compléter — d'où ce journal, corrélé par le même TraceId que la
        // réponse renvoyée à l'utilisateur.
        if (exception is ConcurrencyConflictException conflict)
        {
            logger.LogWarning(
                "Conflit d'écriture ({Code}) sur {TechnicalDetail}. TraceId: {TraceId}",
                code, conflict.TechnicalDetail, traceId);
        }

        if (exception is AccountLockedException accountLockedEx)
        {
            context.Response.Headers.RetryAfter = accountLockedEx.RetryAfterSeconds.ToString();
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { code, message, details, traceId };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
````


### 14.2 Interfaces des générateurs de documents

#### `src/SamaEcole.Application/Common/Interfaces/IPaymentReceiptPdfGenerator.cs`

````csharp
using SamaEcole.Application.Finance;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un reçu de PAIEMENT en PDF (ticket JGK-F02). Pendant de <see cref="IReceiptPdfGenerator"/> (reçu
/// d'inscription) : même moteur QuestPDF, même référence de design, même mention obligatoire constante.
/// <paramref name="logo"/> porte les octets déjà validés du logo (via <see cref="ISchoolLogoProvider"/>)
/// ou <c>null</c> — la récupération réseau reste hors du générateur, qui demeure pur et synchrone.
/// </summary>
public interface IPaymentReceiptPdfGenerator
{
    byte[] Generate(PaymentReceiptDto receipt, byte[]? logo);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IPayslipPdfGenerator.cs`

````csharp
using SamaEcole.Application.Finance.Queries.GetPayslip;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend le Bulletin de Paie (A4) en PDF — détail des cotisations sociales sénégalaises (IPRES
/// employé/employeur, CSS, VRS/CFCE, BRS) et du net à payer. Comme les autres documents officiels, la
/// mise en page est une préoccupation d'infrastructure (QuestPDF) ; la donnée vient de l'Application.
/// </summary>
public interface IPayslipPdfGenerator
{
    byte[] Generate(PayslipDto payslip, byte[]? logo);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/ITaxDeclarationPdfGenerator.cs`

````csharp
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la Déclaration Fiscale mensuelle (A4) en PDF — état synthétique des charges sociales
/// (IPRES, CSS, VRS, BRS) et de la TVA (collectée/déductible/nette) de la période. Comme les autres
/// documents officiels, la mise en page est une préoccupation d'infrastructure (QuestPDF) ; la donnée
/// vient de l'Application.
/// </summary>
public interface ITaxDeclarationPdfGenerator
{
    byte[] Generate(TaxDeclarationDto declaration, byte[]? logo);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IFinancialCommitmentPdfGenerator.cs`

````csharp
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend l'engagement financier en PDF (A4) — même contrat que les autres générateurs de documents officiels.</summary>
public interface IFinancialCommitmentPdfGenerator
{
    byte[] Generate(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage);
}
````


#### `src/SamaEcole.Application/Common/Interfaces/IReceiptPdfGenerator.cs`

````csharp
using SamaEcole.Application.Enrollments;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un reçu d'inscription en PDF (ticket JGK-E02). L'implémentation (SamaEcole.Infrastructure,
/// QuestPDF) reproduit fidèlement docs/design-references/receipt-reference.png — la mise en page est
/// une préoccupation d'infrastructure, la donnée (<see cref="EnrollmentReceiptDto"/>) vient de
/// l'Application. Le document est déterministe : mêmes données figées ⇒ même reçu, réimpression comprise.
///
/// <paramref name="logo"/> porte les octets déjà récupérés et validés du logo de l'établissement
/// (via <see cref="ISchoolLogoProvider"/>), ou <c>null</c> si l'école n'en a pas / s'il est
/// injoignable : dans ce cas l'en-tête retombe sur l'emplacement réservé de la maquette. La
/// récupération réseau reste hors du générateur, qui demeure une fonction pure et synchrone de ses
/// entrées.
/// </summary>
public interface IReceiptPdfGenerator
{
    byte[] Generate(EnrollmentReceiptDto receipt, byte[]? logo);
}
````


### 14.3 Exports Excel (ClosedXML)

#### `src/SamaEcole.Infrastructure/Files/RevenueReportExcelGenerator.cs`

````csharp
using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Export comptable de la consolidation des revenus : une feuille de synthèse, puis une feuille par
/// axe d'analyse (Cycle, Classe, Mode de paiement).
///
/// Les montants portent un FORMAT NUMÉRIQUE Excel (« # ##0 ») et non une chaîne déjà mise en forme :
/// un comptable doit pouvoir sommer, filtrer et croiser ces colonnes dans son propre tableur — un
/// montant écrit « 150 000 FCFA » serait du texte, inexploitable en formule.
/// </summary>
public class RevenueReportExcelGenerator : IRevenueReportExcelGenerator
{
    private const string AmountFormat = "# ##0";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(RevenueConsolidationDto report, string schoolName)
    {
        using var workbook = new XLWorkbook();

        BuildSummarySheet(workbook, report, schoolName);
        BuildBreakdownSheet(workbook, "Par cycle", "Cycle", report.ByCycle);
        BuildBreakdownSheet(workbook, "Par classe", "Classe", report.ByClassroom);
        BuildBreakdownSheet(workbook, "Par mode de paiement", "Mode de paiement", report.ByPaymentMethod);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(
        XLWorkbook workbook, RevenueConsolidationDto report, string schoolName)
    {
        var sheet = workbook.Worksheets.Add("Synthèse");

        sheet.Cell(1, 1).Value = schoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Consolidation des revenus du {report.From:dd/MM/yyyy} au {report.To:dd/MM/yyyy}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        sheet.Cell(4, 1).Value = "Total encaissé (FCFA)";
        sheet.Cell(4, 1).Style.Font.Bold = true;
        sheet.Cell(4, 2).Value = report.TotalCollected;
        sheet.Cell(4, 2).Style.NumberFormat.Format = AmountFormat;

        sheet.Cell(5, 1).Value = "Nombre de versements";
        sheet.Cell(5, 1).Style.Font.Bold = true;
        sheet.Cell(5, 2).Value = report.PaymentCount;

        sheet.Columns(1, 2).AdjustToContents();
    }

    private static void BuildBreakdownSheet(
        XLWorkbook workbook, string sheetName, string labelHeader, IReadOnlyList<RevenueLine> lines)
    {
        var sheet = workbook.Worksheets.Add(sheetName);

        string[] headers = [labelHeader, "Montant (FCFA)", "Versements"];
        for (var col = 0; col < headers.Length; col++)
        {
            var header = sheet.Cell(1, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = HeaderFill;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var row = i + 2;
            sheet.Cell(row, 1).Value = lines[i].Label;
            sheet.Cell(row, 2).Value = lines[i].Amount;
            sheet.Cell(row, 2).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(row, 3).Value = lines[i].PaymentCount;
        }

        // Ligne de total : une VRAIE formule SUM plutôt qu'une valeur calculée en C#. Le comptable qui
        // filtre ou corrige une ligne voit le total suivre, au lieu d'un nombre figé qui contredirait
        // soudain les lignes au-dessus.
        if (lines.Count > 0)
        {
            var totalRow = lines.Count + 2;
            sheet.Cell(totalRow, 1).Value = "TOTAL";
            sheet.Cell(totalRow, 1).Style.Font.Bold = true;
            sheet.Cell(totalRow, 2).FormulaA1 = $"SUM(B2:B{lines.Count + 1})";
            sheet.Cell(totalRow, 2).Style.Font.Bold = true;
            sheet.Cell(totalRow, 2).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(totalRow, 3).FormulaA1 = $"SUM(C2:C{lines.Count + 1})";
            sheet.Cell(totalRow, 3).Style.Font.Bold = true;
        }

        sheet.Columns(1, 3).AdjustToContents();
    }
}
````


#### `src/SamaEcole.Infrastructure/Files/DebtorAgingExcelGenerator.cs`

````csharp
using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Export comptable des débiteurs (Étape 5) : une feuille, une ligne par élève débiteur, triée du
/// retard le plus ancien au plus récent. Même moule que RevenueReportExcelGenerator — montants en
/// FORMAT NUMÉRIQUE Excel, jamais une chaîne déjà mise en forme.
/// </summary>
public class DebtorAgingExcelGenerator : IDebtorAgingExcelGenerator
{
    private const string AmountFormat = "# ##0";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(DebtorAgingReportDto report, string schoolName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Débiteurs");

        sheet.Cell(1, 1).Value = schoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Liste des débiteurs au {report.GeneratedOn:dd/MM/yyyy}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers = ["Matricule", "Élève", "Classe", "Solde dû (FCFA)", "Jours de retard", "Téléphone tuteur"];
        const int headerRow = 4;
        for (var col = 0; col < headers.Length; col++)
        {
            var header = sheet.Cell(headerRow, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = HeaderFill;
        }

        for (var i = 0; i < report.Debtors.Count; i++)
        {
            var row = headerRow + 1 + i;
            var debtor = report.Debtors[i];

            sheet.Cell(row, 1).Value = debtor.Matricule;
            sheet.Cell(row, 2).Value = debtor.StudentFullName;
            sheet.Cell(row, 3).Value = debtor.ClassroomName;
            sheet.Cell(row, 4).Value = debtor.RemainingBalance;
            sheet.Cell(row, 4).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(row, 5).Value = debtor.DaysOverdue;
            sheet.Cell(row, 6).Value = debtor.GuardianPhone ?? string.Empty;
        }

        if (report.Debtors.Count > 0)
        {
            var totalRow = headerRow + 1 + report.Debtors.Count;
            sheet.Cell(totalRow, 3).Value = "TOTAL";
            sheet.Cell(totalRow, 3).Style.Font.Bold = true;
            sheet.Cell(totalRow, 4).FormulaA1 = $"SUM(D{headerRow + 1}:D{totalRow - 1})";
            sheet.Cell(totalRow, 4).Style.Font.Bold = true;
            sheet.Cell(totalRow, 4).Style.NumberFormat.Format = AmountFormat;
        }

        sheet.Columns(1, 6).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
````


### 14.4 Contrôleurs REST

#### `src/SamaEcole.Web/Controllers/FinanceController.cs`

````csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Finance.Commands.UnlinkFeeCategoryDependencies;
using SamaEcole.Application.Finance.Queries.GetFeeCategoryDeletionImpact;
using SamaEcole.Application.Finance.Commands.RestoreFeeCategory;
using SamaEcole.Application.Finance.Queries.GetDeletedFeeCategories;
using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;
using SamaEcole.Application.Finance.Commands.ApplyStandardFee;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.DeleteClassFee;
using SamaEcole.Application.Finance.Commands.DeleteFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Application.Finance.Commands.CancelPayment;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Application.Finance.Commands.UpdateClassFee;
using SamaEcole.Application.Finance.Queries.GetClassFees;
using SamaEcole.Application.Finance.Queries.GetFeeCategories;
using SamaEcole.Application.Finance.Queries.GetFeeHistory;
using SamaEcole.Application.Finance.Queries.GetFinanceDashboard;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using SamaEcole.Application.Finance.Queries.GetPaymentReceiptPdf;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Finance.Queries.GetPayments;
using SamaEcole.Application.Finance.Queries.GetStudentBalance;
using SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Finance.Commands.OpenCashierSession;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Application.Finance.Queries.GetCurrentCashierSession;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using SamaEcole.Application.Finance.Commands.CloseEmployeeContract;
using SamaEcole.Application.Finance.Commands.CancelFichePaie;
using SamaEcole.Application.Finance.Commands.PayFichePaie;
using SamaEcole.Application.Finance.Commands.RegenerateFichePaie;
using SamaEcole.Application.Finance.Queries.GetEmployeeContracts;
using SamaEcole.Application.Finance.Queries.GetFichePaies;
using SamaEcole.Application.Finance.Queries.GetPayslipPdf;
using SamaEcole.Application.Finance.Queries.GetTaxDeclarations;
using SamaEcole.Application.Finance.Queries.GetTaxDeclarationPdf;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Application.Finance.Queries.GetDuesNoticePdf;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using SamaEcole.Application.Finance.Queries.GetWorkCertificatePdf;
using SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitmentPdf;
using SamaEcole.Application.Finance.Commands.CreateTeacherHourRecord;
using SamaEcole.Application.Finance.Queries.GetTeacherHourRecords;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheetPdf;
using SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;
using SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;
using SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;
using SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;
using SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;
using SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;
using SamaEcole.Application.Finance.Queries.GetDebtorReminderBatches;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-F01 — paramétrage des frais (/finance/fees, /finance/fee-categories). Contrôleur
/// mince : aucune logique métier ici (AGENTS.md règle #8). L'école vient du JWT, jamais d'un
/// paramètre (règle #10).
///
/// LECTURE ouverte à tout utilisateur de l'école : le secrétariat compose les montants dus à
/// l'inscription, la finance encaisse — tous ont besoin de VOIR le barème. CRÉATION d'une catégorie,
/// application d'un montant standard (Option 1), MODIFICATION/SUPPRESSION d'une ligne de barème ou
/// d'une catégorie : Directeur toujours ; Finance UNIQUEMENT si le Directeur de SON école a
/// explicitement activé la délégation correspondante (SchoolSettings.AllowFinanceToModifyFees /
/// AllowFinanceToDeleteFees, matrice d'autorisation "Photoshop") — voir
/// CanModifyFeesHandler/CanDeleteFeesHandler (SamaEcole.Web.Authorization). Fermé par défaut : la
/// règle #4 tient la Finance à l'écart de la fixation des montants tant que ce choix n'a pas été fait
/// explicitement.
///
/// Créer une catégorie / appliquer un standard PARTAGENT la délégation "modifier" (pas une bascule
/// distincte) : les deux façonnent le même objet — le barème d'établissement — au même titre qu'un
/// ajustement classe par classe (Option 2). Un Directeur qui délègue "modifier les frais" délègue donc
/// aussi la mise en place initiale du barème, jamais la SUPPRESSION (délégation séparée, plus lourde
/// de conséquences).
/// </summary>
[ApiController]
[Route("api/v1/finance")]
[Authorize]
[RequireModule(SchoolModule.Finance)]
public class FinanceController(ISender mediator, ILogger<FinanceController> logger) : ControllerBase
{
    public record UpdateFeeRequest(decimal Amount, uint RowVersion);

    public record UpdateFeeCategoryRequest(bool IsOptional);

    // ------------------------------------------------------------------ Catégories

    [HttpGet("fee-categories")]
    [ProducesResponseType<IReadOnlyList<FeeCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeCategoriesQuery(), cancellationToken));

    /// <summary>
    /// Feature D — autonomie Finance. Matrice d'autorisation "Photoshop", même délégation que
    /// UpdateFee ci-dessous (AllowFinanceToModifyFees) : Directeur toujours, Finance seulement si
    /// délégué. Remplace l'ancien [Authorize(Roles = Directeur)] codé en dur.
    /// </summary>
    [HttpPost("fee-categories")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
    [ProducesResponseType<CreateFeeCategoryResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateFeeCategoryCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(ListCategories), new { id = result.Id }, result);
    }

    /// <summary>
    /// Frais optionnels — bascule le caractère facultatif d'une catégorie (uniforme, tenue de sport).
    /// Même délégation que la création : Directeur toujours, Finance seulement si déléguée.
    /// </summary>
    [HttpPut("fee-categories/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCategory(
        Guid id, [FromBody] UpdateFeeCategoryRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpdateFeeCategoryCommand(id, request.IsOptional), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Supprime (soft delete) une catégorie ET, en cascade, tout son barème — matrice d'autorisation
    /// "Photoshop" : Directeur toujours, Finance seulement si SchoolSettings.AllowFinanceToDeleteFees
    /// est activé pour cette école (CanDeleteFeesHandler).
    /// </summary>
    [HttpDelete("fee-categories/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteFeeCategoryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Ce qui empêche ou accompagne la suppression d'une catégorie de frais (spec 2026-10-17, §3) : blocage financier ou liste précise des
    /// liens à rompre. Lecture seule.
    /// </summary>
    [HttpGet("fee-categories/{id:guid}/deletion-impact")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FeeCategoryDeletionImpact(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeCategoryDeletionImpactQuery(id), cancellationToken));

    /// <summary>
    /// Rupture EXPLICITE des liens de la catégorie (archivage ou détachement, tracé). Ne supprime pas l'élément.
    /// 409 : blocage financier ou lien non rompable.
    /// </summary>
    [HttpPost("fee-categories/{id:guid}/unlink-dependencies")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkFeeCategoryDependencies(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UnlinkFeeCategoryDependenciesCommand(id), cancellationToken));

    /// <summary>Corbeille : catégories de frais supprimées. Même autorisation que la suppression.</summary>
    [HttpGet("fee-categories/deleted")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType<IReadOnlyList<DeletedFeeCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDeletedCategories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDeletedFeeCategoriesQuery(), cancellationToken));

    /// <summary>Restaure une catégorie de frais supprimée (ses lignes de barème ne sont pas réactivées).</summary>
    [HttpPost("fee-categories/{id:guid}/restore")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreCategory(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreFeeCategoryCommand(id), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Barème

    [HttpGet("fees")]
    [ProducesResponseType<IReadOnlyList<ClassFeeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFees(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassFeesQuery(), cancellationToken));

    /// <summary>
    /// Option 1 — applique un montant standard à toutes les classes d'une catégorie, en un geste.
    /// Feature D : même délégation que CreateCategory/UpdateFee (AllowFinanceToModifyFees).
    /// </summary>
    [HttpPost("fees/apply-standard")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
    [ProducesResponseType<ApplyStandardFeeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyStandard(
        [FromBody] ApplyStandardFeeCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>
    /// Option 2 — ajuste le montant d'une classe (exception), avec verrouillage optimiste. Matrice
    /// d'autorisation "Photoshop" : Directeur toujours, Finance seulement si
    /// SchoolSettings.AllowFinanceToModifyFees est activé pour cette école (CanModifyFeesHandler) —
    /// remplace l'ancien [Authorize(Roles = Directeur)] codé en dur.
    /// </summary>
    [HttpPut("fees/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
    [ProducesResponseType<UpdateClassFeeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateFee(
        Guid id, [FromBody] UpdateFeeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateClassFeeCommand(id, request.Amount, request.RowVersion), cancellationToken));

    /// <summary>
    /// Supprime (soft delete) une ligne de barème, avec le même verrouillage optimiste que la
    /// modification. Matrice d'autorisation "Photoshop" : Directeur toujours, Finance seulement si
    /// SchoolSettings.AllowFinanceToDeleteFees est activé pour cette école (CanDeleteFeesHandler).
    /// `rowVersion` en query string : une suppression, contrairement à une modification, n'a pas de
    /// corps de requête à transporter avec elle.
    /// </summary>
    [HttpDelete("fees/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteFee(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteClassFeeCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    [HttpGet("fees/{id:guid}/history")]
    [ProducesResponseType<IReadOnlyList<FeeHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FeeHistory(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeHistoryQuery(id), cancellationToken));

    // ------------------------------------------------------------------ Caisse (JGK-F02)

    // ENCAISSEMENT (JGK-F02 + inscription au guichet) : Directeur, Finance ET Secrétariat. Le Secrétariat
    // tient sa propre caisse — il ouvre une session, encaisse (à l'inscription ou ici), la clôture avec
    // comptage physique. La règle #4 n'interdit pas au Secrétariat d'encaisser : elle sépare qui FIXE le
    // dû (Secrétariat/Directeur, jamais Finance) de la santé financière AGRÉGÉE (liste globale des
    // paiements, dashboards, trésorerie), qui reste réservée à Directeur/Finance ci-dessous.
    private const string PaymentWriters = "Directeur,Finance,Secretariat";

    /// <summary>Opérations de caisse (session + encaissement), ouvertes à quiconque tient une caisse.</summary>
    private const string CashierRoles = "Directeur,Finance,Secretariat";

    /// <summary>
    /// Point d'entrée de l'écran caisse : à partir d'un élève trouvé par recherche, l'inscription et le
    /// solde de l'année active. LECTURE ouverte à tout rôle de l'école, comme le reçu.
    /// </summary>
    [HttpGet("students/{studentId:guid}/balance")]
    [ProducesResponseType<StudentBalanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StudentBalance(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentBalanceQuery(studentId), cancellationToken));

    /// <summary>
    /// Recherche élève DÉDIÉE à la caisse (GET /finance/students/search?q=...) : mêmes résultats que
    /// la recherche générique (nom/matricule), mais chaque ligne porte déjà le solde de l'inscription
    /// active — la caissière voit tout de suite qu'un élève vient d'être inscrit par le secrétariat
    /// sans avoir encore rien versé, sans devoir ouvrir sa fiche pour le découvrir.
    /// </summary>
    [HttpGet("students/search")]
    [ProducesResponseType<IReadOnlyList<CashierStudentSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchStudentsForCashier([FromQuery] string q, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SearchStudentsForCashierQuery(q ?? string.Empty), cancellationToken));

    /// <summary>
    /// Liste paginée et filtrée des encaissements de l'établissement (GET /finance/payments). Réservé
    /// à Directeur et Finance, comme Dashboard/TreasuryDashboard : contrairement à un reçu individuel
    /// (StudentBalance/PaymentReceipt, dont le tenant vient du JWT et qui ne révèle qu'un paiement
    /// précis), cette liste expose tous les encaissements de l'école — montants, méthodes, élèves.
    /// </summary>
    [HttpGet("payments")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<PaginatedPayments>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListPayments(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] string? method = null, [FromQuery] Guid? classroomId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(
            new GetPaymentsQuery
            {
                Page = page,
                PageSize = pageSize,
                Search = search,
                Method = method,
                ClassroomId = classroomId
            }, cancellationToken));

    /// <summary>Encaisse un versement sur une inscription, avec verrou optimiste et reçu officiel (règles #4, #5).</summary>
    [HttpPost("payments")]
    [Authorize(Roles = PaymentWriters)]
    [ProducesResponseType<RecordPaymentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordPayment(
        [FromBody] RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(PaymentReceipt), new { id = result.PaymentId }, result);
    }

    /// <summary>
    /// Annule un versement saisi par erreur (spec 2026-10-17, §2.4). Le paiement et son numéro de reçu sont
    /// conservés, marqués « annulé » avec leur motif ; le montant est rendu au solde de l'inscription.
    /// Directeur, et Finance tant que le Directeur ne lui a pas retiré ce droit (SchoolSettings.AllowFinanceToCancelPayments,
    /// vrai par défaut — le comptable corrige sans attendre la direction). 409 si le paiement est déjà
    /// annulé, ou si le délai de grâce de 48 h après l'encaissement est dépassé (PaymentCancellationPolicy) : le Directeur
    /// peut alors passer une CONTRE-PASSATION en renvoyant <c>reversal: true</c> (écriture inverse datée d'aujourd'hui, 403 pour les autres rôles).
    /// </summary>
    [HttpPost("payments/{id:guid}/cancel")]
    [Authorize(Policy = FinancePolicies.CanCancelPayments)]
    [ProducesResponseType<CancelPaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelPayment(
        Guid id, [FromBody] CancelPaymentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new CancelPaymentCommand(id, request.Reason, request.Outcome, request.Reversal), cancellationToken));

    // LECTURE du reçu ouverte à tout rôle de l'école : le tenant vient du JWT, un paiement d'une autre
    // école est introuvable (404), jamais servi.
    [HttpGet("payments/{id:guid}/receipt")]
    [ProducesResponseType<PaymentReceiptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PaymentReceipt(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPaymentReceiptQuery(id), cancellationToken));

    [HttpGet("payments/{id:guid}/receipt/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PaymentReceiptPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetPaymentReceiptPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le contenu PDF généré est vide pour le paiement {PaymentId}", id);
                return NotFound(new { message = "Le document PDF de reçu est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Recu-{result.ReceiptNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du reçu PDF pour le paiement {PaymentId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération du reçu PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Tableau de bord (JGK-F04)

    /// <summary>
    /// Encaissé jour/mois/année, solde dû et taux de recouvrement sur l'année scolaire active. Réservé
    /// à Directeur et Finance : le Secrétariat compose le montant dû à l'inscription mais n'a pas besoin
    /// de voir la santé financière agrégée de l'établissement (règle #4).
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<FinanceDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFinanceDashboardQuery(), cancellationToken));

    [HttpGet("daily-cash-register/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DailyCashRegisterPdf(
        [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        try
        {
            var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var result = await mediator.Send(new GetDailyCashRegisterPdfQuery(targetDate), cancellationToken);
            
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le contenu PDF généré est vide pour le journal de caisse du {Date}", targetDate);
                return NotFound(new { message = "Le document PDF du journal de caisse est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Journal_Caisse_{targetDate:yyyy-MM-dd}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du journal de caisse PDF.");
            return Problem(
                detail: "Une erreur interne est survenue lors de la génération du document.", 
                title: "Erreur de génération", 
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Caisse Sessions

    /// <summary>
    /// La session ouverte de l'utilisateur courant, ou 200 avec un corps vide s'il n'en a aucune —
    /// c'est ce que /caisse interroge à l'ouverture pour savoir s'il faut proposer d'ouvrir une session
    /// ou afficher le statut de celle déjà ouverte (ticket JGK — câblage écran, 27/08/2026).
    /// </summary>
    [HttpGet("sessions/current")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType<CurrentCashierSessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentSession(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCurrentCashierSessionQuery(), cancellationToken));

    [HttpPost("sessions/open")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenSession([FromBody] OpenCashierSessionCommand command, CancellationToken cancellationToken)
    {
        var sessionId = await mediator.Send(command, cancellationToken);
        return Ok(sessionId);
    }

    public record CloseCashierSessionRequest(decimal ActualCashAmount, string? DiscrepancyReason = null);

    public record CancelPaymentRequest(
        string Reason, PaymentCancellationOutcome Outcome = PaymentCancellationOutcome.KeptBySchool, bool Reversal = false);

    /// <summary>Le comptage physique (JGK-F09) est obligatoire ; le motif d'écart ne l'est que si un écart est réellement constaté (422 sinon).</summary>
    [HttpPost("sessions/{id}/close")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType<CloseCashierSessionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseSession(
        Guid id, [FromBody] CloseCashierSessionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CloseCashierSessionCommand(id, request.ActualCashAmount, request.DiscrepancyReason),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions/{id}/closing-report")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    public async Task<IActionResult> GetClosingReportPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDailyClosingReportPdfQuery(id), cancellationToken);

        // `inline` : le rapport de clôture s'ouvre dans la modale d'aperçu (impression / téléchargement
        // au choix depuis l'en-tête), au lieu d'atterrir directement dans les téléchargements.
        return this.InlinePdf(result.Content, result.FileName);
    }

    // ------------------------------------------------------------------ Module Comptabilité & Fiscalité (JGK)

    [HttpGet("treasury")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<TreasuryDashboardDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TreasuryDashboard(
        [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTreasuryDashboardQuery(startDate, endDate), cancellationToken));

    [HttpGet("employee-contracts")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<EmployeeContractDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeContracts(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetEmployeeContractsQuery(), cancellationToken));

    [HttpPost("employee-contracts")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEmployeeContract([FromBody] CreateEmployeeContractCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetEmployeeContracts), new { id }, id);
    }

    public record UpdateEmployeeContractRequest(
        decimal BaseSalary,
        decimal HourlyRate,
        decimal TransportAllowance,
        string Reason,
        uint RowVersion,
        PayoutMethod PayoutMethod = PayoutMethod.Cash,
        string? PayoutAccountReference = null,
        bool? ApplyStatutoryDeductions = null);

    /// <summary>Augmentation de salaire ou révision du taux horaire (Volume 1 §14.1) — la seule voie de modification d'un contrat ACTIF.</summary>
    [HttpPatch("employee-contracts/{id:guid}")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<UpdateEmployeeContractResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateEmployeeContract(
        Guid id, [FromBody] UpdateEmployeeContractRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateEmployeeContractCommand(
                id, request.BaseSalary, request.HourlyRate, request.TransportAllowance, request.Reason, request.RowVersion,
                request.PayoutMethod, request.PayoutAccountReference, request.ApplyStatutoryDeductions),
            cancellationToken);

        return Ok(result);
    }

    public record CloseEmployeeContractRequest(DateOnly EndDate, string Reason, uint RowVersion);

    /// <summary>Clôture définitive d'un contrat (Volume 1 §14.1) — jamais une suppression, voir CloseEmployeeContractCommand.</summary>
    [HttpPost("employee-contracts/{id:guid}/close")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<CloseEmployeeContractResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseEmployeeContract(
        Guid id, [FromBody] CloseEmployeeContractRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CloseEmployeeContractCommand(id, request.EndDate, request.Reason, request.RowVersion),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("payroll")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<FichePaieListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayroll([FromQuery] int? month, [FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFichePaiesQuery(month, year), cancellationToken));

    [HttpPost("payroll")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> GeneratePayroll([FromBody] SamaEcole.Application.Finance.Commands.GenerateFichePaie.GenerateFichePaieCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GeneratePayroll), new { id }, id);
    }

    public record CancelFichePaieRequest(string Reason, uint RowVersion);

    /// <summary>Annule une fiche de paie erronée sans la supprimer ; contre-passe le décaissement si elle était payée (Volume 1 §14.5).</summary>
    [HttpPost("payroll/{id:guid}/cancel")]
    [Authorize(Policy = FinancePolicies.CanCancelPayslips)]
    [ProducesResponseType<CancelFichePaieResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelPayroll(Guid id, [FromBody] CancelFichePaieRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CancelFichePaieCommand(id, request.Reason, request.RowVersion), cancellationToken));

    public record RegenerateFichePaieRequest(string Reason, uint RowVersion, decimal? HoursWorked = null, decimal? TransportAllowance = null);

    /// <summary>Annule la fiche erronée puis en génère une nouvelle pour la même période, sur les termes actuels du contrat (Volume 1 §14.5).</summary>
    [HttpPost("payroll/{id:guid}/regenerate")]
    [Authorize(Policy = FinancePolicies.CanCancelPayslips)]
    [ProducesResponseType<RegenerateFichePaieResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegeneratePayroll(Guid id, [FromBody] RegenerateFichePaieRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new RegenerateFichePaieCommand(id, request.Reason, request.RowVersion, request.HoursWorked, request.TransportAllowance),
            cancellationToken));

    public record PayFichePaieRequest(PaymentMethod PaymentMethod, DateOnly Date, uint RowVersion);

    /// <summary>Règle le net d'une fiche : décaissement « Salaires » en trésorerie + bulletin valant reçu (Volume 1 §14.6).</summary>
    [HttpPost("payroll/{id:guid}/pay")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<PayFichePaieResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PayPayroll(Guid id, [FromBody] PayFichePaieRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new PayFichePaieCommand(id, request.PaymentMethod, request.Date, request.RowVersion), cancellationToken));

    [HttpGet("payroll/{id:guid}/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayslipPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetPayslipPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le bulletin de paie PDF généré est vide pour la fiche {FichePaieId}", id);
                return NotFound(new { message = "Le bulletin de paie PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Bulletin-{result.PayslipNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du bulletin de paie PDF pour {FichePaieId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération du bulletin de paie PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("tax-declarations")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<TaxDeclarationListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaxDeclarations([FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTaxDeclarationsQuery(year), cancellationToken));

    [HttpPost("tax-declaration")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> GenerateTaxDeclaration([FromBody] SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration.GenerateTaxDeclarationCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GenerateTaxDeclaration), new { id }, id);
    }

    [HttpGet("tax-declarations/{id:guid}/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaxDeclarationPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetTaxDeclarationPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La déclaration fiscale PDF générée est vide pour {TaxDeclarationId}", id);
                return NotFound(new { message = "La déclaration fiscale PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Declaration-Fiscale-{result.DeclarationNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la déclaration fiscale PDF pour {TaxDeclarationId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la déclaration fiscale PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Module Documents administratifs (cahier des charges élite)

    // Sommation pour impayés : refuse (409, BusinessRuleException) pour un compte à jour — voir GetDuesNoticeQueryHandler.
    [HttpGet("enrollments/{enrollmentId:guid}/dues-notice")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<DuesNoticeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DuesNotice(Guid enrollmentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDuesNoticeQuery(enrollmentId), cancellationToken));

    [HttpGet("enrollments/{enrollmentId:guid}/dues-notice/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DuesNoticePdf(Guid enrollmentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetDuesNoticePdfQuery(enrollmentId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La sommation PDF générée est vide pour l'inscription {EnrollmentId}", enrollmentId);
                return NotFound(new { message = "La sommation PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Sommation-{result.NoticeNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (SamaEcole.Application.Common.Exceptions.BusinessRuleException)
        {
            throw; // Laisse le middleware d'exception le gérer (409)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la sommation PDF pour {EnrollmentId}", enrollmentId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la sommation PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Attestation de travail : document RH interne (comme le bulletin de paie), pas de bandeau M.E.N.
    [HttpGet("employee-contracts/{contractId:guid}/work-certificate")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<WorkCertificateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WorkCertificate(Guid contractId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetWorkCertificateQuery(contractId), cancellationToken));

    [HttpGet("employee-contracts/{contractId:guid}/work-certificate/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WorkCertificatePdf(Guid contractId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetWorkCertificatePdfQuery(contractId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("L'attestation de travail PDF générée est vide pour le contrat {ContractId}", contractId);
                return NotFound(new { message = "L'attestation de travail PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Attestation-Travail-{result.CertificateNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de l'attestation de travail PDF pour {ContractId}", contractId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de l'attestation de travail PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Engagement financier : trace écrite d'un échéancier, ne touche jamais TotalDue/AmountPaid (règle #4).
    [HttpPost("commitments")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateFinancialCommitment([FromBody] CreateFinancialCommitmentCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetFinancialCommitment), new { id }, id);
    }

    [HttpGet("commitments/{id:guid}")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<FinancialCommitmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFinancialCommitment(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFinancialCommitmentQuery(id), cancellationToken));

    [HttpGet("commitments/{id:guid}/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFinancialCommitmentPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetFinancialCommitmentPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("L'engagement financier PDF généré est vide pour {CommitmentId}", id);
                return NotFound(new { message = "L'engagement financier PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Engagement-{result.CommitmentNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de l'engagement financier PDF pour {CommitmentId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de l'engagement financier PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Fiche de suivi des heures (Vacataire) : détail jour par jour, distinct du calcul de paie lui-même.
    [HttpPost("employee-contracts/{contractId:guid}/hour-records")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateHourRecord(Guid contractId, [FromBody] CreateHourRecordRequest request, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(new CreateTeacherHourRecordCommand(contractId, request.Date, request.Hours, request.Note), cancellationToken);
        return CreatedAtAction(nameof(GetHourRecords), new { contractId }, id);
    }

    public record CreateHourRecordRequest(DateOnly Date, decimal Hours, string? Note);

    [HttpGet("employee-contracts/{contractId:guid}/hour-records")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<TeacherHourRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHourRecords(Guid contractId, [FromQuery] int? month, [FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTeacherHourRecordsQuery(contractId, month, year), cancellationToken));

    [HttpGet("employee-contracts/{contractId:guid}/hour-records/sheet")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<HourRecordSheetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHourRecordSheet(Guid contractId, [FromQuery] int month, [FromQuery] int year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHourRecordSheetQuery(contractId, month, year), cancellationToken));

    /// <summary>
    /// Suggestion d'heures pour la paie du vacataire (ticket JGK-K01) — consultative, n'écrit rien.
    /// La Direction pré-remplit HoursWorked avec cette valeur côté client puis soumet
    /// POST /finance/payroll (GenerateFichePaieCommand) sans aucun changement de contrat sur celle-ci.
    /// </summary>
    [HttpGet("employee-contracts/{contractId:guid}/suggested-hours")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<SuggestedPayrollHoursDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSuggestedPayrollHours(
        Guid contractId, [FromQuery] int month, [FromQuery] int year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSuggestedPayrollHoursQuery(contractId, month, year), cancellationToken));

    [HttpGet("employee-contracts/{contractId:guid}/hour-records/sheet/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHourRecordSheetPdf(Guid contractId, [FromQuery] int month, [FromQuery] int year, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetHourRecordSheetPdfQuery(contractId, month, year), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La fiche d'heures PDF générée est vide pour le contrat {ContractId}", contractId);
                return NotFound(new { message = "La fiche d'heures PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Fiche-Heures-{result.SheetNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la fiche d'heures PDF pour le contrat {ContractId}", contractId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la fiche d'heures PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Échéanciers personnalisés (Étape 5)

    [HttpPost("enrollments/{enrollmentId:guid}/installment-plan")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateFeeInstallmentPlan(
        Guid enrollmentId, [FromBody] CreateFeeInstallmentPlanRequest request, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(
            new CreateFeeInstallmentPlanCommand(enrollmentId, request.Reason, request.Installments),
            cancellationToken);

        // Pas de route "GET plan par id" dédiée : le plan se consulte via le solde de l'élève
        // (GetStudentBalanceQuery), qui l'intègre déjà — un Location vers une autre ressource
        // (studentId, pas enrollmentId) serait trompeur.
        return StatusCode(StatusCodes.Status201Created, id);
    }

    public record CreateFeeInstallmentPlanRequest(string? Reason, IReadOnlyList<CreateFeeInstallmentLine> Installments);

    [HttpPost("classrooms/{classroomId:guid}/installment-plan")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<ApplyFeeInstallmentPlanToClassroomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyFeeInstallmentPlanToClassroom(
        Guid classroomId, [FromBody] ApplyFeeInstallmentPlanToClassroomRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ApplyFeeInstallmentPlanToClassroomCommand(classroomId, request.Reason, request.Template),
            cancellationToken));

    public record ApplyFeeInstallmentPlanToClassroomRequest(string? Reason, IReadOnlyList<InstallmentTemplateLine> Template);

    // ------------------------------------------------------------------ Recouvrement — lots de relance (Étape 5)

    [HttpGet("dues-reminder-batches")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<DebtorReminderBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDebtorReminderBatches(
        [FromQuery] DebtorReminderBatchStatus? status, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDebtorReminderBatchesQuery(status), cancellationToken));

    /// <summary>
    /// Force le recalcul immédiat des brouillons (normalement calculés chaque nuit par
    /// DebtorAgingHostedService) — utile pour ne pas attendre le tour suivant après un réglage du
    /// seuil de retard, par exemple.
    /// </summary>
    [HttpPost("dues-reminder-batches/recompute")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecomputeDebtorReminderBatches(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GenerateDebtorReminderBatchesCommand(), cancellationToken));

    [HttpPost("dues-reminder-batches/{id:guid}/send")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<SendDebtorReminderBatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendDebtorReminderBatch(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SendDebtorReminderBatchCommand(id), cancellationToken));

    [HttpPost("dues-reminder-batches/{id:guid}/dismiss")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DismissDebtorReminderBatch(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DismissDebtorReminderBatchCommand(id), cancellationToken);
        return NoContent();
    }
}

````


#### `src/SamaEcole.Web/Controllers/DisbursementsController.cs`

````csharp
using SamaEcole.Application.Features.Disbursements;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

[ApiController]
[Route("api/v1/finance/disbursements")]
[Authorize(Roles = $"{nameof(Role.Directeur)},Finance,{nameof(Role.SuperAdmin)}")]
[RequireModule(SchoolModule.Finance)]
public class DisbursementsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DisbursementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDisbursements(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] DisbursementCategory? category,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new GetDisbursementsQuery(startDate, endDate, category), cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDisbursement(
        [FromBody] CreateDisbursementCommand command,
        CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetDisbursements), new { id }, id);
    }

    /// <summary>
    /// ANNULE un décaissement par une contre-écriture (montants opposés, référence à l'original) : l'écriture
    /// d'origine n'est jamais supprimée. 409 si le décaissement est déjà annulé ou s'il est lui-même une annulation.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDisbursement(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteDisbursementCommand(id), cancellationToken);
        return NoContent();
    }
}
````


#### `src/SamaEcole.Web/Controllers/FinancialReportsController.cs`

````csharp
using SamaEcole.Application.Finance.Queries.GetDebtorAgingExport;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidationExcel;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Rapports financiers avancés — consolidation des revenus et exports comptables.
///
/// Contrôleur SÉPARÉ de FinanceController, et non quelques actions de plus dedans : tout ce qui est
/// ici est verrouillé par <see cref="Feature.AdvancedFinancialReports"/> (formules Standard et
/// Premium), alors que le module Finance de base reste accessible à toutes les formules. Le
/// verrouillage porté par la CLASSE rend cette frontière lisible — et impossible à oublier sur une
/// action ajoutée plus tard.
///
/// Directeur et Finance uniquement, comme le tableau de bord financier : c'est la santé financière
/// agrégée de l'établissement.
/// </summary>
[ApiController]
[Route("api/v1/finance/reports")]
[Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Finance)}")]
[RequireFeature(Feature.AdvancedFinancialReports)]
[RequireModule(SchoolModule.Finance)]
public class FinancialReportsController(ISender mediator) : ControllerBase
{
    [HttpGet("revenue")]
    [ProducesResponseType<RevenueConsolidationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueConsolidation(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRevenueConsolidationQuery { From = from, To = to }, cancellationToken));

    /// <summary>Même rapport, au format comptable téléchargeable (.xlsx).</summary>
    [HttpGet("revenue/excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueConsolidationExcel(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var file = await mediator.Send(
            new GetRevenueConsolidationExcelQuery { From = from, To = to }, cancellationToken);

        return File(
            file.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.FileName);
    }

    /// <summary>Liste des débiteurs (Étape 5), avec le nombre de jours de retard — Volume 1 §7.5.</summary>
    [HttpGet("debtor-aging")]
    [ProducesResponseType<DebtorAgingReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDebtorAgingReport(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDebtorAgingReportQuery(), cancellationToken));

    /// <summary>Même rapport, au format comptable téléchargeable (.xlsx).</summary>
    [HttpGet("debtor-aging/excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDebtorAgingExcel(CancellationToken cancellationToken)
    {
        var file = await mediator.Send(new GetDebtorAgingExportQuery(), cancellationToken);

        return File(
            file.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.FileName);
    }
}
````


#### `src/SamaEcole.Web/Controllers/PagesController.cs (lignes 140–280 sur 291)`

````csharp
    public IActionResult Subjects() => View("~/Views/Subjects/Index.cshtml");

    // JGK-G01/G02 : écran de saisie des notes (Devoir/Composition) par classe, matière et trimestre.
    [PageModule(SchoolModule.Pedagogy)]
    [HttpGet("/notes")]
    public IActionResult Grades() => View("~/Views/Grades/Index.cshtml");

    // Module Cahier de texte / Journal de classe (ticket JGK-P04) — ClassJournalController garde
    // l'accès (écriture Enseignant sur sa propre séance planifiée, lecture ouverte en plus à
    // Directeur/Secrétariat/Surveillant) et la RLS isole.
    [PageModule(SchoolModule.Pedagogy)]
    [HttpGet("/cahier-de-texte")]
    public IActionResult ClassJournal() => View("~/Views/ClassJournal/Index.cshtml");

    [HttpGet("/frais")]
    public IActionResult Fees() => View("~/Views/Fees/Index.cshtml");

    [HttpGet("/caisse")]
    public IActionResult Caisse() => View("~/Views/Caisse/Index.cshtml");

    [HttpGet("/parametres")]
    public IActionResult Settings() => View("~/Views/Settings/Index.cshtml");

    // JGK-R02 : rapport d'assiduité détaillé par classe et par élève. Comme les autres pages, gabarit
    // [AllowAnonymous] côté vue (le JWT ne voyage pas en navigation) — c'est ReportsController qui garde
    // l'accès (Directeur/Secrétariat/Super Admin) et la RLS qui isole.
    [PageModule(SchoolModule.Pedagogy)]
    [HttpGet("/rapports/assiduite")]
    public IActionResult AttendanceReport() => View("~/Views/Reports/Attendance.cshtml");

    // Module Examens officiels (CFEE/BFEM/BAC) — ExamsController garde l'accès (Directeur/Secrétariat
    // sur tout le module ; lecture des dossiers ouverte en plus à l'Enseignant, borné à ses classes
    // assignées, ticket JGK-J08) et la RLS isole.
    [PageModule(SchoolModule.Pedagogy)]
    [HttpGet("/examens")]
    public IActionResult Exams() => View("~/Views/Exams/Index.cshtml");

    // Module Intégration étatique (SIMEN / Planète / STATEDUC) — StateIntegrationController garde
    // l'accès (Directeur sur tout ; Secrétariat en plus sur IEN et certificats de mutation) et la RLS
    // isole. Gabarit anonyme comme le reste, l'API porte la garde.
    [HttpGet("/integration-etatique")]
    public IActionResult StateIntegration() => View("~/Views/StateIntegration/Index.cshtml");

    // Vérification PUBLIQUE d'un certificat de mutation (scan du QR imprimé sur la pièce). Anonyme :
    // c'est l'école d'accueil, extérieure à la plateforme, qui arrive ici. L'API sous-jacente
    // (GET /api/v1/state-integration/certificates/verify/{token}) ne révèle aucune donnée d'élève.
    // Le format de cette URL est fixé par GenerateStudentMutationCertificateCommandHandler
    // (BuildVerificationUrl) : ne pas le changer sans migrer les certificats déjà émis.
    [HttpGet("/verifier/mutation/{token}")]
    public IActionResult VerifyMutation(string token)
    {
        ViewData["Token"] = token;
        return View("~/Views/StateIntegration/VerifyMutation.cshtml");
    }

    // JGK-F05 : consolidation des revenus + export comptable .xlsx. Même gabarit anonyme que
    // ci-dessus — l'accès réel est gardé par FinancialReportsController, qui cumule
    // [Authorize(Directeur, Finance)] ET [RequireFeature(AdvancedFinancialReports)].
    // Évolution N°7 : rapports institutionnels (rapport de rentrée IEF, normes d'âge). InstitutionalController
    // garde l'accès (Directeur/Secrétariat) et la RLS isole — gabarit anonyme comme le reste.
    [PageModule(SchoolModule.Pedagogy)]
    [HttpGet("/rapports/institutionnels")]
    public IActionResult InstitutionalReports() => View("~/Views/Reports/Institutional.cshtml");

    // Évolution N°7 : programmes nationaux (référentiel des chapitres) et avancement pointé au cahier de texte.
    // SyllabusController garde l'accès (écriture Directeur, tableau Directeur/Secrétariat), la RLS isole.
    [PageModule(SchoolModule.Pedagogy)]
    [HttpGet("/programmes")]
    public IActionResult Syllabus() => View("~/Views/Syllabus/Index.cshtml");

    [HttpGet("/rapports/financiers")]
    public IActionResult FinancialReport() => View("~/Views/Reports/Financial.cshtml");

    // JGK-I03 : espace Super Admin de revue des demandes d'inscription self-service. Comme les autres
    // pages, [AllowAnonymous] côté vue (le JWT ne voyage pas en navigation) — c'est
    // AdminRegistrationRequestsController qui garde l'accès (Roles = SuperAdmin) et la RLS qui isole.
    [HttpGet("/admin/inscriptions")]
    public IActionResult RegistrationRequests() => View("~/Views/Admin/RegistrationRequests.cshtml");

    // Console Super Admin (refonte plateforme) — gabarits [AllowAnonymous] au même titre que le reste
    // de ce contrôleur : c'est SchoolsController (Roles = SuperAdmin) et les futures routes
    // plateforme équivalentes qui gardent l'accès aux données, jamais la page elle-même. Le lien
    // d'entrée dans _Layout.cshtml et l'atterrissage par défaut (wwwroot/js/auth.js,
    // defaultLandingForRole) pointent vers /admin.
    [HttpGet("/admin")]
    public IActionResult SuperAdminDashboard() => View("~/Views/SuperAdmin/Dashboard.cshtml");

    [HttpGet("/admin/etablissements")]
    public IActionResult SuperAdminSchools() => View("~/Views/SuperAdmin/Schools.cshtml");

    [HttpGet("/admin/facturation")]
    public IActionResult SuperAdminBilling() => View("~/Views/SuperAdmin/Billing.cshtml");

    // Module Tarification, Réductions & Offres Promotionnelles — PromoCodesController (Roles =
    // SuperAdmin) garde l'accès aux données, comme le reste de cette console.
    [HttpGet("/admin/tarification")]
    public IActionResult SuperAdminPricing() => View("~/Views/SuperAdmin/Pricing.cshtml");

    // Changements d'abonnement : demandes des Directeurs (profil, cycles, tranche, modules) et écoles en dépassement de quota —
    // AdminSubscriptionChangeRequestsController / AdminOverQuotaSchoolsController (Roles = SuperAdmin) gardent l'accès aux données.
    [HttpGet("/admin/changements-abonnement")]
    public IActionResult SuperAdminSubscriptionChanges() => View("~/Views/SuperAdmin/SubscriptionChanges.cshtml");

    [HttpGet("/admin/securite")]
    public IActionResult SuperAdminSecurity() => View("~/Views/SuperAdmin/Security.cshtml");

    // Les années scolaires ne sont plus une entrée de menu à part : elles vivent désormais dans les
    // Paramètres (onglet dédié). On redirige l'ancienne adresse pour ne casser aucun lien existant.
    [HttpGet("/annees-scolaires")]
    public IActionResult SchoolYears() => RedirectToAction(nameof(Settings), new { tab = "annees-scolaires" });

    // ------------------------------------------------------------------ Module Comptabilité & Fiscalité (JGK)

    [HttpGet("/tresorerie")]
    public IActionResult Treasury() => View("~/Views/Treasury/Index.cshtml");

    [HttpGet("/paie")]
    public IActionResult Payroll() => View("~/Views/Payroll/Index.cshtml");

    [HttpGet("/fiscalite")]
    public IActionResult Taxes() => View("~/Views/Taxes/Index.cshtml");

    // ------------------------------------------------------------------ Centre d'aide

    // Guide d'utilisation intégré. Contenu 100 % statique (wwwroot/js/help.js) : aucune donnée
    // d'établissement n'y transite, donc aucun appel d'API à garder ici — et la page reste
    // consultable quand le serveur d'API est injoignable, c'est-à-dire précisément quand
    // l'utilisateur cherche de l'aide. Ouverte à tous les rôles, y compris au Super Admin.
    [HttpGet("/aide")]
    public IActionResult Help() => View("~/Views/Help/Index.cshtml");

    // ------------------------------------------------------------------ Vitrine publique (marketing)

    // Page vitrine B2B : présente le logiciel aux directeurs/gérants d'établissement. Contenu statique, SAUF la grille
    // tarifaire privée, rendue d'après la grille en base (ViewComponent PrivatePricing) : un prix modifié par le Super
    // Admin y apparaît tout de suite. Aucun appel API côté navigateur.
    [HttpGet("/vitrine")]
    public IActionResult LandingB2B() => View("~/Views/Home/LandingB2B.cshtml");

    // Catalogue commercial détaillé des 14 modules. Séparé de /vitrine (qui reste orientée
    // conversion) pour que la présentation ne redevienne pas une longue page de documentation.
````


### 14.5 Câblage (injection de dépendances)

#### `src/SamaEcole.Infrastructure/DependencyInjection.cs`

````csharp
using System.Net.Http;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Notifications;
using SamaEcole.Application.Registration;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Infrastructure.Caching;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.Infrastructure.Files;
using SamaEcole.Infrastructure.Finance;
using SamaEcole.Infrastructure.Media;
using SamaEcole.Infrastructure.Multitenancy;
using SamaEcole.Infrastructure.Notifications;
using SamaEcole.Infrastructure.Payments;
using SamaEcole.Infrastructure.Security;
using SamaEcole.Infrastructure.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SamaEcole.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Sous-section « Sms:Queue » (voir SmsQueueSettings et .env.example).</summary>
    private const string SmsQueueSettingsSection = "Sms:Queue";

    /// <summary>Sous-section « Finance:DebtorAging » (voir DebtorAgingSettings et .env.example).</summary>
    private const string DebtorAgingSettingsSection = "Finance:DebtorAging";

    /// <summary>Sous-section « Subscriptions:Lifecycle » (voir SubscriptionLifecycleSettings et .env.example).</summary>
    private const string SubscriptionLifecycleSettingsSection = "Subscriptions:Lifecycle";

    /// <summary>Section « Wave » : compte marchand de la plateforme (voir WaveMerchantSettings).</summary>
    private const string WaveSettingsSection = "Wave";

    /// <summary>Sous-section « Kpi:Cache » (voir KpiCacheSettings et .env.example).</summary>
    private const string KpiCacheSettingsSection = "Kpi:Cache";

    /// <param name="isDevelopment">
    /// Vient de <c>IHostEnvironment.IsDevelopment()</c> (résolu dans Program.cs, seul endroit qui
    /// connaît l'environnement d'hébergement) : jamais dérivé d'une variable de configuration
    /// modifiable, pour qu'aucun déploiement Staging/Production mal configuré ne puisse activer
    /// DevPaymentService (voir sa condition d'enregistrement plus bas).
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Premier consommateur réel de ITenantCacheKeyFactory : cache des agrégats KPI (dashboards
        // Finance/Directeur) derrière IMemoryCache. Le remplacement futur par Redis (IDistributedCache,
        // Volume_6_Dev_Guide.md) se limite à une nouvelle implémentation de IKpiCacheService — aucun
        // handler ne dépend de IMemoryCache directement.
        services.AddScoped<ITenantCacheKeyFactory, TenantCacheKeyFactory>();

        services.AddMemoryCache();
        var kpiCacheSettings = configuration.GetSection(KpiCacheSettingsSection).Get<KpiCacheSettings>()
                                ?? new KpiCacheSettings();
        services.AddSingleton(kpiCacheSettings);
        services.AddScoped<IKpiCacheService, MemoryKpiCacheService>();

        // Authentification (ticket JGK-A04).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Mot de passe initial du Directeur (ticket JGK-B01).
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();

        // Référence de suivi d'une demande d'inscription self-service (ticket JGK-I01).
        services.AddSingleton<IRegistrationReferenceGenerator, RegistrationReferenceGenerator>();

        // Réglages du flux d'inscription self-service (section « Registration » — lien de connexion,
        // alerte Super Admin). Même idiome que StateIntegrationSettings plus bas.
        var registrationSettings = configuration
            .GetSection("Registration")
            .Get<RegistrationSettings>() ?? new RegistrationSettings();
        services.AddSingleton(registrationSettings);

        services.AddSingleton<IQrCodeService, SamaEcole.Infrastructure.Services.QrCodeService>();


        // Envoi d'e-mails transactionnels (ticket JGK-G03, incl. le mot de passe provisoire du
        // Directeur — JGK-B01). Development : LoggingEmailSender, qui journalise en clair (pratique en
        // local, voir sa doc). Hors Development : SmtpEmailSender réel, et l'absence de configuration
        // SMTP fait ÉCHOUER LE DÉMARRAGE plutôt que de retomber silencieusement sur l'adaptateur qui
        // journalise les mots de passe — même logique que RlsGuard / PayDunyaOptions.IsConfigured.
        var smtpOptions = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        if (isDevelopment)
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else if (smtpOptions.IsConfigured)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            // Ni Development, ni SMTP configuré : la garde fait échouer le démarrage, SAUF renonciation
            // explicite (Smtp__AllowUnconfigured=true), qui bascule alors sur un adaptateur qui refuse
            // d'envoyer sans jamais journaliser de corps de message. Program.cs émet l'avertissement
            // correspondant (EmailSenderGuard.DescribeDegradedMode).
            EmailSenderGuard.EnsureEmailSenderIsConfigured(smtpOptions, isDevelopment);
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }

        // WhatsApp — même règle de choix que les SMS, et pour la même raison : une configuration
        // absente ne fait PAS échouer le démarrage (le canal est optionnel), mais elle ne doit pas
        // non plus laisser croire qu'un bulletin est parti. LoggingWhatsAppSender journalise en clair
        // « NON ENVOYÉ », et SmsServiceGuard avertit au démarrage hors Development.
        var whatsAppOptions = configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>()
                              ?? new WhatsAppOptions();
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));

        if (!isDevelopment && whatsAppOptions.IsConfigured)
        {
            services.AddHttpClient<IWhatsAppSender, HttpWhatsAppSender>(client =>
                client.Timeout = TimeSpan.FromSeconds(30));
        }
        else
        {
            services.AddSingleton<IWhatsAppSender, LoggingWhatsAppSender>();
        }

        // Notifications SMS (offre Premium). Contrairement au SMTP, une configuration absente ne fait
        // PAS échouer le démarrage : les SMS sont optionnels et rien n'est exposé sans eux — mais
        // retomber silencieusement sur l'adaptateur qui journalise ferait croire à une école Premium
        // que ses parents sont alertés. D'où l'avertissement explicite de SmsServiceGuard.
        var smsOptions = configuration.GetSection(SmsOptions.SectionName).Get<SmsOptions>() ?? new SmsOptions();
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));

        if (!isDevelopment && smsOptions.IsConfigured)
        {
            // AddHttpClient plutôt qu'un HttpClient construit à la main : gestion du pool de
            // connexions et du recyclage DNS, comme pour tout appel sortant durable.
            services.AddHttpClient<ISmsService, HttpSmsService>(client =>
                client.Timeout = TimeSpan.FromSeconds(15));
        }
        else
        {
            services.AddSingleton<ISmsService, LoggingSmsService>();
        }

        // Lecteur d'accusés de réception. Enregistré dans TOUS les environnements, y compris
        // Development : il refuse de lui-même tout appel tant qu'aucun secret n'est configuré
        // (IsWebhookSecretConfigured), il n'y a donc pas de porte ouverte à refermer ici.
        services.AddSingleton<ISmsDeliveryReceiptReader, HmacSmsDeliveryReceiptReader>();

        // Réglages du dépilage de la file. Instance SINGLETON plutôt qu'IOptions : le service hébergé
        // en a besoin dès son démarrage, avant toute portée de requête, et ces valeurs ne changent
        // pas à chaud.
        var queueSettings = configuration.GetSection(SmsQueueSettingsSection).Get<SmsQueueSettings>()
                            ?? new SmsQueueSettings();
        services.AddSingleton(queueSettings);

        // Le dépilage ne démarre QUE s'il est activé (les tests fonctionnels le coupent : un worker
        // qui bascule un message de Pending à Sent au milieu d'une assertion rendrait la suite non
        // déterministe). En Development il tourne comme en production, avec LoggingSmsService en
        // guise de fournisseur — c'est ce qui rend la file observable en local.
        if (queueSettings.Enabled)
        {
            services.AddHostedService<SmsQueueHostedService>();
        }

        // Calcul quotidien des lots de relance de débiteurs (Étape 5 — recouvrement semi-automatique).
        // Même garde que la file SMS : les tests fonctionnels le coupent (Finance__DebtorAging__Enabled=false)
        // pour qu'un lot brouillon créé pendant l'exécution d'un test ne rende pas son résultat non
        // déterministe.
        var debtorAgingSettings = configuration.GetSection(DebtorAgingSettingsSection).Get<DebtorAgingSettings>()
                                   ?? new DebtorAgingSettings();
        services.AddSingleton(debtorAgingSettings);

        if (debtorAgingSettings.Enabled)
        {
            services.AddHostedService<DebtorAgingHostedService>();
        }

        // Alertes d'expiration 30/15/7 jours et passage automatique en lecture seule (ticket JGK-B03).
        // Même garde que les deux workers ci-dessus : les tests fonctionnels le coupent
        // (Subscriptions__Lifecycle__Enabled=false) pour qu'un abonnement basculé en ReadOnly pendant
        // l'exécution d'un test ne rende pas son résultat non déterministe.
        var subscriptionLifecycleSettings = configuration.GetSection(SubscriptionLifecycleSettingsSection)
            .Get<SubscriptionLifecycleSettings>() ?? new SubscriptionLifecycleSettings();
        services.AddSingleton(subscriptionLifecycleSettings);

        if (subscriptionLifecycleSettings.Enabled)
        {
            services.AddHostedService<SubscriptionLifecycleHostedService>();
        }

        // Police arabe (module Coran/Franco-Arabe) — enregistrée UNE SEULE FOIS ici, avant tout PDF,
        // plutôt que répliquée dans le constructeur statique de chaque *PdfGenerator.cs comme
        // QuestPDF.Settings.License l'est aujourd'hui (voir PdfFonts pour le raisonnement complet).
        SamaEcole.Infrastructure.Documents.PdfFonts.EnsureRegistered();

        // Génération PDF des reçus (inscription JGK-E02, paiement JGK-F02) et certificat d'inscription (Axe 2). Sans état : des singletons suffisent.
        services.AddSingleton<IReceiptPdfGenerator, ReceiptPdfGenerator>();
        services.AddSingleton<IPaymentReceiptPdfGenerator, PaymentReceiptPdfGenerator>();
        services.AddSingleton<IDailyCashRegisterPdfGenerator, DailyCashRegisterPdfGenerator>();
        services.AddSingleton<IDailyClosingReportPdfGenerator, SamaEcole.Infrastructure.Documents.DailyClosingReportPdfGenerator>();
        services.AddSingleton<ISchoolCardPdfGenerator, SamaEcole.Infrastructure.Documents.SchoolCardPdfGenerator>();
        services.AddSingleton<IEnrollmentCertificatePdfGenerator, EnrollmentCertificatePdfGenerator>();

        // Module Documents administratifs (cahier des charges élite) — nouveaux documents officiels,
        // même moteur QuestPDF que ci-dessus, sans état.
        services.AddSingleton<IExeatCertificatePdfGenerator, ExeatCertificatePdfGenerator>();

        // Module Internat/Daara : bulletin coranique (suivi de mémorisation par Hizb), bilingue français/arabe.
        services.AddSingleton<IHizbReportPdfGenerator, HizbReportPdfGenerator>();
        // Module Internat, modèle Pavillon/Lit (lot E) : fiche individuelle d'un pensionnaire et registre d'un pavillon.
        services.AddSingleton<IBoarderSheetPdfGenerator, BoarderSheetPdfGenerator>();
        services.AddSingleton<IDormitoryRegisterPdfGenerator, DormitoryRegisterPdfGenerator>();
        services.AddSingleton<IMonthlyAttendanceSummaryPdfGenerator, MonthlyAttendanceSummaryPdfGenerator>();
        services.AddSingleton<IDisciplinaryPvPdfGenerator, DisciplinaryPvPdfGenerator>();
        services.AddSingleton<IDuesNoticePdfGenerator, DuesNoticePdfGenerator>();
        services.AddSingleton<IWorkCertificatePdfGenerator, WorkCertificatePdfGenerator>();
        services.AddSingleton<IParentNoticePdfGenerator, ParentNoticePdfGenerator>();
        services.AddSingleton<IFinancialCommitmentPdfGenerator, FinancialCommitmentPdfGenerator>();
        services.AddSingleton<IHourRecordSheetPdfGenerator, HourRecordSheetPdfGenerator>();

        // Billet d'entrée en classe A5 (module Surveillance) — même moteur QuestPDF, sans état.
        services.AddSingleton<IEntryTicketPdfGenerator, EntryTicketPdfGenerator>();
        services.AddSingleton<IExitTicketPdfGenerator, ExitTicketPdfGenerator>();

        // Bulletin de paie A4 (module Comptabilité & Fiscalité) — même moteur QuestPDF, sans état.
        services.AddSingleton<IPayslipPdfGenerator, PayslipPdfGenerator>();

        // Déclaration fiscale mensuelle A4 (module Comptabilité & Fiscalité) — même moteur QuestPDF, sans état.
        services.AddSingleton<ITaxDeclarationPdfGenerator, TaxDeclarationPdfGenerator>();

        // Bulletin de notes PDF (ticket JGK-G03) — même moteur QuestPDF, même convention.
        services.AddSingleton<IReportCardPdfGenerator, ReportCardPdfGenerator>();
        services.AddSingleton<IGradeSheetPdfGenerator, GradeSheetPdfGenerator>();

        // Bulletins de classe fusionnés en un seul PDF, pour l'impression en lot — même moteur QuestPDF.
        services.AddSingleton<IClassBulletinsPdfGenerator, ClassBulletinsPdfGenerator>();
        services.AddSingleton<IClassDeliberationPdfGenerator, ClassDeliberationPdfGenerator>();

        // Rapport d'assiduité PDF (ticket JGK-R03) — même moteur QuestPDF, sans état.
        services.AddSingleton<IAttendanceReportPdfGenerator, AttendanceReportPdfGenerator>();

        // Export PDF des élèves (Volume_7_Security.md §15, matrice Élèves) — même moteur QuestPDF, sans état.
        services.AddSingleton<IStudentsExportPdfGenerator, StudentsExportPdfGenerator>();
        services.AddSingleton<ITeachersExportPdfGenerator, TeachersExportPdfGenerator>();

        // Module Inventaire : fiche d'inventaire global (A4 paysage) et fiche de décharge de matériel
        // (A5 paysage, charte ReceiptTheme). Sans état, comme tous les générateurs ci-dessus.
        services.AddSingleton<IInventoryReportPdfGenerator, InventoryReportPdfGenerator>();
        services.AddSingleton<IDischargeNotePdfGenerator, DischargeNotePdfGenerator>();

        // Import/export de la feuille de notes au format Excel large (ClosedXML, Volume_2_SDS.md
        // « bibliothèques »). Sans état : un singleton suffit, comme les générateurs de documents ci-dessus.
        services.AddSingleton<IGradeSheetImportParser, GradeSheetImportParser>();
        services.AddSingleton<IGradeSheetExcelGenerator, GradeSheetExcelGenerator>();

        // Export comptable de la consolidation des revenus (rapports financiers avancés) — même
        // bibliothèque, aucune nouvelle dépendance.
        services.AddSingleton<IRevenueReportExcelGenerator, RevenueReportExcelGenerator>();

        // Export comptable des débiteurs (Étape 5 — recouvrement) — même bibliothèque, aucune nouvelle
        // dépendance.
        services.AddSingleton<IDebtorAgingExcelGenerator, DebtorAgingExcelGenerator>();

        // Module Examens officiels : relevé d'inscription (.xlsx, ClosedXML) et fiches de candidature /
        // convocations PDF (QuestPDF, charte OfficialHeaderComponent). Sans état, comme le reste.
        services.AddSingleton<IExamRegistrationExcelGenerator, ExamRegistrationExcelGenerator>();
        services.AddSingleton<IExamCandidateFormPdfGenerator, ExamCandidateFormPdfGenerator>();
        services.AddSingleton<IExamConvocationPdfGenerator, ExamConvocationPdfGenerator>();
        services.AddSingleton<IExamSynthesisPdfGenerator, ExamSynthesisPdfGenerator>();
        services.AddSingleton<IRollBookPdfGenerator, RollBookPdfGenerator>();

        // ---------------------------------------------- Module Intégration étatique (SIMEN, §23)
        //
        // Réglages liés depuis la section « StateIntegration » d'appsettings. Aucun secret n'y
        // transite : la clé d'API du SIMEN, le jour où elle existera, suivra le chemin protégé des
        // identifiants de l'agrégateur de paiement.
        var stateIntegrationSettings = configuration
            .GetSection("StateIntegration")
            .Get<StateIntegrationSettings>() ?? new StateIntegrationSettings();
        services.AddSingleton(stateIntegrationSettings);

        // Générateurs sans état, comme tous les autres documents du projet.
        services.AddSingleton<IPlaneteExportSerializer, PlaneteExportSerializer>();
        services.AddSingleton<IStateducReportPdfGenerator, StateducReportPdfGenerator>();
        services.AddSingleton<IStateducReportExcelGenerator, StateducReportExcelGenerator>();
        services.AddSingleton<IIefReportPdfGenerator, IefReportPdfGenerator>();
        services.AddSingleton<IIefReportExcelGenerator, IefReportExcelGenerator>();
        services.AddSingleton<IStudentMutationCertificatePdfGenerator, StudentMutationCertificatePdfGenerator>();
        services.AddSingleton<ISkillsBookletPdfGenerator, SkillsBookletPdfGenerator>();

        // Relais SIMEN : l'implémentation livrée REFUSE chaque appel, explicitement — aucune API
        // publique n'existe à ce jour. Elle sera remplacée par HttpSimenBridgeService le jour où le
        // ministère en ouvrira une. Voir UnavailableSimenBridgeService pour le raisonnement.
        services.AddSingleton<ISimenBridgeService, SamaEcole.Infrastructure.Services.UnavailableSimenBridgeService>();

        // Import d'élèves par fichier CSV/Excel (même bibliothèque ClosedXML, aucune nouvelle dépendance).
        services.AddSingleton<IStudentImportFileParser, StudentImportFileParser>();
        services.AddSingleton<IStudentImportTemplateGenerator, StudentImportTemplateGenerator>();

        // Import du corps professoral par fichier CSV/Excel — même patron que l'import d'élèves.
        services.AddSingleton<ITeacherImportFileParser, TeacherImportFileParser>();
        services.AddSingleton<ITeacherImportTemplateGenerator, TeacherImportTemplateGenerator>();

        // Récupération du logo de l'établissement pour le reçu (JGK-E02). Client HTTP dédié :
        //  * garde anti-SSRF au moment de la connexion (l'URL vient du Directeur — cf. SsrfSafeConnect) ;
        //  * aucune redirection auto : une 3xx pourrait rebondir d'une URL publique vers un service interne ;
        //  * timeout court : le logo ne doit jamais retarder l'émission d'un reçu.
        services.AddHttpClient(HttpSchoolLogoProvider.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SamaEcole-Recu/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = SsrfSafeConnect.ConnectAsync
            });
        services.AddSingleton<ISchoolLogoProvider, HttpSchoolLogoProvider>();

        // Paiement d'abonnement (ticket JGK-I05) — agrégateur PayDunya derrière IPaymentService
        // (agnostique, docs/Volume_1_Cahier_des_Charges.md §11.6 : « le choix définitif reste à
        // valider »). Client HTTP dédié, sans garde SSRF ici : l'URL est une constante de configuration
        // (PayDunyaOptions.ApiBaseUrl), jamais une adresse fournie par un utilisateur.
        var payDunyaSection = configuration.GetSection(PayDunyaOptions.SectionName);
        services.Configure<PayDunyaOptions>(payDunyaSection);
        services.Configure<SubscriptionPricingOptions>(configuration.GetSection(SubscriptionPricingOptions.SectionName));

        // Compte marchand Wave de la plateforme (paiement d'abonnement direct, confirmé à la main par le Super
        // Admin) : le lien est public, il vit dans appsettings.json (section « Wave »).
        services.AddSingleton(configuration.GetSection(WaveSettingsSection).Get<WaveMerchantSettings>()
            ?? new WaveMerchantSettings());

        // Toujours enregistré (même hors Development) : DevPaymentSimulationController en dépend pour
        // afficher son faux guichet, et sa propre garde IWebHostEnvironment.IsDevelopment() suffit à en
        // interdire l'usage ailleurs — un DevPaymentService inutilisé ne coûte rien.
        services.AddSingleton<DevPaymentService>();

        // Bascule Dev-only : Environment=Development ET clés absentes/au sentinel "REMPLACER" (les DEUX
        // conditions, jamais l'une sans l'autre — un compte marchand configuré en Development doit
        // continuer à passer par le vrai PayDunya, et aucune configuration Staging/Production ne doit
        // jamais pouvoir déclencher l'auto-confirmation, même par erreur). Sans elle, l'écran
        // /abonnement/paiement reste bloqué pour quiconque n'a pas de compte PayDunya (sandbox ou prod)
        // sous la main en local.
        var payDunyaOptions = payDunyaSection.Get<PayDunyaOptions>() ?? new PayDunyaOptions();
        if (isDevelopment && !payDunyaOptions.IsConfigured)
        {
            services.AddSingleton<IPaymentService>(provider => provider.GetRequiredService<DevPaymentService>());
        }
        else
        {
            services.AddHttpClient(PayDunyaPaymentService.HttpClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PayDunyaOptions>>().Value;
                client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            services.AddScoped<IPaymentService, PayDunyaPaymentService>();
        }

        services.AddSingleton<ISubscriptionPricingProvider, ConfiguredSubscriptionPricingProvider>();

        return services;
    }
}
````


### 14.6 Formules d'abonnement donnant accès aux exports

#### `src/SamaEcole.Domain/Enums/Feature.cs`

````csharp
namespace SamaEcole.Domain.Enums;

/// <summary>
/// Fonctionnalité soumise à la formule d'abonnement de l'école (contrôle d'accès par formule).
///
/// N'y figurent QUE des fonctionnalités nouvelles ou explicitement premium : le socle métier
/// (élèves, classes, notes, inscriptions, encaissement, bulletins, documents) reste accessible à
/// TOUTES les formules. Verrouiller après coup une fonctionnalité déjà livrée retirerait un accès
/// à des écoles qui l'utilisent aujourd'hui — ce n'est pas une décision technique, elle appartient
/// au commerce ; ajouter un membre ici suffira le jour où elle sera prise.
/// </summary>
public enum Feature
{
    /// <summary>Notifications SMS aux parents (retards, absences, impayés, reçus).</summary>
    SmsNotifications,

    /// <summary>Rapports financiers consolidés (par cycle/classe/mode de paiement) et exports comptables.</summary>
    AdvancedFinancialReports,

    /// <summary>Groupe scolaire : plusieurs établissements pilotés depuis un même compte.</summary>
    MultiSchool
}

/// <summary>
/// SEULE source de vérité de la matrice formule → fonctionnalités, partagée par l'API
/// (FeatureAuthorizationHandler) et l'interface (GetSchoolFeaturesQuery, qui alimente les badges
/// « Premium » côté Alpine). Sans elle, le serveur et le client finiraient par diverger et
/// l'utilisateur verrait un bouton actif que l'API refuse.
/// </summary>
public static class PlanFeatures
{
    private static readonly Feature[] StandardFeatures = [Feature.AdvancedFinancialReports];

    private static readonly Feature[] PremiumFeatures =
        [Feature.AdvancedFinancialReports, Feature.SmsNotifications, Feature.MultiSchool];

    public static IReadOnlyList<Feature> For(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Premium => PremiumFeatures,
        SubscriptionPlan.Standard => StandardFeatures,

        // Primaire — formule d'entrée : le socle métier uniquement (voir la remarque de Feature).
        _ => []
    };

    public static bool Includes(SubscriptionPlan plan, Feature feature) => For(plan).Contains(feature);

    /// <summary>Formule la plus basse qui inclut <paramref name="feature"/> — pour le message « Passez à … ».</summary>
    public static SubscriptionPlan MinimumPlanFor(Feature feature) =>
        Includes(SubscriptionPlan.Standard, feature) ? SubscriptionPlan.Standard : SubscriptionPlan.Premium;
}
````


---

*Fin du document principal. Voir l'annexe A pour les écrans, le JavaScript, les PDF/Excel et les migrations, et l'annexe B pour les tests.*
