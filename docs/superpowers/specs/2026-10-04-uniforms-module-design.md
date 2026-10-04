# Module Tenues (uniformes et tenues de sport) — Spécification

**Date :** 04/10/2026
**Statut :** arbitrages validés par le propriétaire le 04/10/2026 (§2) ; à relire avant la fusion.
**Périmètre :** suivre, pour chaque élève et chaque année, **quelle tenue il doit recevoir, dans quelle taille, si elle a été facturée, et si elle a été remise** — en s'appuyant sur ce qui existe déjà : le barème de frais optionnels (PR #51) pour la facturation, et le module Inventaire pour le stock. Le module n'introduit **aucun second système de stock** et **n'écrit jamais dans Finance**.

## 1. Contexte : ce qui existe, ce qui manque

| Besoin | Existant | Manque (objet de ce module) |
|---|---|---|
| Facturer une tenue | `FeeCategory.IsOptional` + `EnrollmentFeeLine` : la famille coche « Uniforme » ou « Tenue de sport » à l'inscription (PR #51) | — |
| Stock | Inventaire : lots, `StockLedger`, journal `stock_movements` append-only, catégorie « Tenues » en exemple | **Les tailles** : un lot n'a aucune variante |
| Remise | `ItemAssignment` = prêt avec retour | Une tenue est **donnée**, pas prêtée ; aucun lien facturé ↔ remis ; aucune taille par élève |
| Suivi | — | « À remettre », « besoin par taille contre stock » |

Deux faits structurants, constatés dans le code :

- **Aucune commande ne modifie `TotalDue` après l'inscription** (seul `CreateEnrollment` l'écrit). `Payment` exige un `EnrollmentId` et plafonne le versement au solde. Une tenue achetée **en cours d'année** n'est donc pas facturable aujourd'hui : c'est un ticket Finance distinct (§2, décision 1).
- `PaymentCategory.Uniformes` et `FournituresVente` existent dans l'enum mais ne sont lus nulle part. Ce module ne les utilise pas.

## 2. Décisions (validées le 04/10/2026)

| # | Question | Décision |
|---|---|---|
| 1 | Facturation | **La V1 ne traite que les tenues facturées à l'inscription** (frais optionnel coché). Une remise « non facturée » reste possible, avec **motif obligatoire** (≤ 200 caractères) et marquage `NotBilled`. Les achats complémentaires en cours d'année relèvent d'un **ticket Finance séparé** (correction historisée de `TotalDue`, règle #4) : hors périmètre. |
| 2 | Verrou de remise | **Avertir sans bloquer.** Un badge « Solde non réglé » s'affiche quand `TotalDue − AmountPaid > 0` sur l'inscription ; la remise reste autorisée. Le niveau inscription est volontaire : la ventilation par catégorie (`PaymentBreakdown`) est facultative, donc non fiable. |
| 3 | Fabrication sur mesure | V1 = **synthèse des besoins et des manques** seulement (`GET /api/v1/uniforms/needs`). Aucune entité « commande fournisseur ». |
| 4 | Quantité par frais | `UniformItem.QuantityIncluded` (défaut 1) : nombre d'unités couvertes par la ligne de frais. Une ligne de dotation au-delà de ce nombre n'est pas « facturée » et exige un motif. |
| 5 | Stock | **Réutilisation de l'Inventaire** : une variante (taille) = un lot `InventoryItem`. `StockLedger` reste l'unique chemin d'écriture de `QuantityAvailable` ; `stock_movements` reste append-only. |
| 6 | Mouvement de remise | `Sortie` (total et disponible baissent), **pas** `Attribution` (qui garde le bien au patrimoine). Un retour ou un échange passe par `Entree` (mouvement inverse). |
| 7 | Réservation de stock | Aucune. « En rupture » est **calculé** (demandé > disponible), jamais stocké. |
| 8 | Intégration à l'inscription | **Aucune** en V1 : `CreateEnrollment` n'est pas modifié (règle #3, transaction d'inscription). Les dotations se créent depuis la fiche élève, à partir des lignes de frais déjà facturées. |

## 3. Modèle de données

Trois tables tenant, toutes : `SchoolId`, Global Query Filter **et** policy RLS, suppression logique, `GRANT SELECT, INSERT, UPDATE` (jamais `DELETE`), FK **composites tenant-safe** `(SchoolId, X)` en RESTRICT (technique de `student_subject_exemptions`), ajoutées à `reset_school_data` et `delete_school_year`.

### `uniform_items` — modèle de tenue
`Name` (≤ 100), `Kind` (`Uniforme` / `TenueSport` / `Accessoire`), `Gender` (`Mixte` / `Garcon` / `Fille`), `Cycle?` (`CycleType`, filtre de proposition), `FeeCategoryId?` (le frais qui le facture), `QuantityIncluded` (≥ 1), `Notes` (≤ 300), `xmin`.
Unique partiel `(SchoolId, Name, Gender) WHERE NOT IsDeleted`.

### `uniform_variants` — taille
`UniformItemId`, `SizeLabel` (≤ 30, texte libre : « 10 ans », « M », « 38 »), `SortOrder`, `InventoryItemId` (le lot qui porte le stock).
Uniques partiels `(SchoolId, UniformItemId, SizeLabel)` et `(SchoolId, InventoryItemId)`. À la création, le lot est créé dans la catégorie « Tenues » de l'école (créée si absente), nommé « {Modèle} — {Taille} », `IsConsumable = true` (donc non prêtable), quantité 0 : le stock s'alimente par l'écran Inventaire existant.

### `student_uniforms` — dotation
`StudentId`, `EnrollmentId`, `SchoolYearId` (copié de l'inscription), `UniformItemId`, `UniformVariantId?` (null = taille à confirmer), `Quantity` (> 0), `Status` (`Requested` / `Delivered` / `Returned` / `Cancelled`), `DeliveredOn`, `DeliveredByUserId`, `DeliveryStockMovementId`, `ReturnStockMovementId`, `BillingStatus` (`Billed` / `NotBilled`), `FeeLineId?` (preuve de facturation), `NotBilledReason?`, libellés figés `ItemLabel`, `SizeLabel`, `StudentLabel` (une fiche réimprimée plus tard reste identique), `xmin`.
- `CHECK Quantity > 0`.
- `CHECK Status IN ('Delivered','Returned') ⇒ DeliveredOn, DeliveredByUserId, DeliveryStockMovementId, UniformVariantId non nuls`.
- `CHECK Status = 'Returned' ⇒ ReturnStockMovementId et ReturnReason non nuls`.
- `CHECK Status IN ('Delivered','Returned') AND BillingStatus = 'NotBilled' ⇒ NotBilledReason non nul` ; `CHECK BillingStatus = 'Billed' ⇒ FeeLineId non nul`.
- Unique partiel `(SchoolId, EnrollmentId, UniformItemId) WHERE Status IN ('Requested','Delivered') AND NOT IsDeleted` : une ligne active par élève et par modèle ; la quantité porte le nombre d'exemplaires.
- Index `(SchoolId, SchoolYearId, Status)` et `(SchoolId, UniformVariantId, Status)`.

## 4. Règles métier

**Facturation (`UniformBillingCheck`, pure).** Une ligne est `Billed` si et seulement si : le modèle a un `FeeCategoryId`, l'inscription porte une `EnrollmentFeeLine` de cette catégorie, et `Quantity ≤ QuantityIncluded`. Sinon `NotBilled`, et la remise exige un motif. Le contrôle est calculé **à la remise** et figé sur la ligne.

**Remise (`UniformDeliveryPolicy`, pure + handler).** Statut `Requested`, taille renseignée, stock suffisant. Le handler suit le lot `InventoryItem` (verrou `xmin`), appelle `StockLedger.Apply(Sortie)`, enregistre le mouvement et passe la ligne à `Delivered` **dans une seule transaction**. Concurrence : deux remises sur la dernière unité → 409 pour la seconde ; stock insuffisant → 422.

**Retour / échange.** `Delivered → Returned`, motif obligatoire, mouvement `Entree`. Un échange = retour + nouvelle ligne.

**Annulation.** Seulement depuis `Requested` (`Cancelled`) ; jamais d'effacement.

**Badge de solde.** Les lectures exposent `HasUnpaidBalance` et `BalanceDue` de l'inscription, sans effet sur l'autorisation.

**Cohérence tenant.** Chaque handler charge inscription, modèle, taille et lot par les `DbSet` filtrés (404 sinon) : une FK ne vérifie pas l'école.

## 5. API — `/api/v1/uniforms`

Aucun contrôle de formule d'abonnement ni `RequireModule` (comme l'Inventaire). L'école vient du JWT (règle #10).

| Méthode | Route | Rôles | Rôle de la route |
|---|---|---|---|
| `GET` | `/items` | lecture : tout le personnel authentifié | Catalogue, tailles et stock disponible |
| `POST` `PUT` `DELETE` | `/items`, `/items/{id}` | Directeur, Secrétariat | Modèle ; `DELETE` = archivage (refusé si une dotation active l'utilise) |
| `POST` `PUT` `DELETE` | `/items/{id}/variants`, `/variants/{id}` | Directeur, Secrétariat | Tailles ; crée le lot Inventaire |
| `GET` | `/students/{studentId}` | Directeur, Secrétariat, Finance, Surveillant | Dotations de l'élève, facturé ou non, badge de solde |
| `POST` `PUT` `DELETE` | `/requests`, `/requests/{id}` | Directeur, Secrétariat, Surveillant | Besoins ; `PUT` sous `xmin` (409) ; `DELETE` = annulation |
| `POST` | `/requests/{id}/deliver`, `/requests/{id}/return` | Directeur, Secrétariat, Surveillant | Remise et retour, audités (`IAuditableRequest`) |
| `GET` | `/pending` | Directeur, Secrétariat, Surveillant | À remettre, filtrable par classe et modèle |
| `GET` | `/needs` | Directeur, Secrétariat | Besoin par modèle × taille contre stock, manques calculés |
| `GET` | `/requests/{id}/discharge/pdf`, `/classrooms/{id}/sizes/pdf` | Directeur, Secrétariat, Surveillant | Fiche de remise (A5), feuille de tailles par classe |

Erreurs au format normalisé (Volume 4 §0.4) : 404 hors tenant, 409 conflit `xmin` ou doublon actif, 422 stock insuffisant ou règle de gestion.

## 6. Multi-tenant et sécurité

- Les trois tables sont des `ITenantEntity` : `RlsCoverageTests` les vérifie sans liste figée ; la migration les ajoute à `TenantTables`.
- Droits `SELECT, INSERT, UPDATE` seulement : le test hérité de `RevokeDeleteOnBusinessTables` échoue sinon.
- `student_uniforms` porte `SchoolYearId` : ajoutée à `delete_school_year` et à `reset_school_data`, **avant** `enrollment_fee_lines`, `enrollments`, `stock_movements`, `inventory_items` et `fee_categories`, dont elle dépend.
- Un caractère personnel minimal (nom, taille) : aucun motif médical ni donnée sensible dans ce module.
- Tests obligatoires (AGENTS.md) : isolation (lecture, écriture, références croisées entre écoles) et concurrence de remise.

## 7. Hors périmètre V1

Facturation en cours d'année (ticket Finance) ; saisie des tailles dans le formulaire d'inscription ; reprise des tailles de l'année précédente ; commande fournisseur ; réservation de stock ; SMS « tenue prête » ; export `.xlsx`.

## 8. Points de vigilance

1. `Gender` des modèles se confronte à `Student.Gender` (`"M"`/`"F"`) pour la **proposition**, jamais comme contrainte.
2. Un élève qui a besoin d'un même modèle en deux tailles tient en deux unités d'une même ligne — non supporté : une seule taille par ligne (rare ; à rouvrir si le terrain le demande).
3. Un lot de tenue reste modifiable depuis l'écran Inventaire (ajustement après recomptage) ; seul l'archivage d'un lot référencé par une taille est refusé (garde ajoutée à `DeleteInventoryItem`).
