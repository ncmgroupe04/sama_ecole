# Conception — Suppression guidée : protection financière, contrôle des dépendances, rupture explicite des liens

**Date :** 2026-10-17
**Statut :** décisions validées par le commanditaire le 2026-10-17 (voir §10) ; en attente du feu vert explicite pour implémenter. Aucun code n'est écrit.
**Périmètre :** Élèves, Classes, Enseignants, Salles, Bâtiments, Matières, Frais, Années scolaires, Inventaire (voir §6).
**S'appuie sur :** `2026-10-01-soft-delete-index-uniques-design.md` (cycle de vie logique, exceptions financières).

## 1. Besoin exprimé

1. Tout ce qui a été ajouté doit pouvoir être supprimé, sans impasse.
2. **Un élève rattaché à au moins un paiement non annulé ne peut pas être supprimé.** Message obligatoire :
   « Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés. »
   Si l'on se rend compte d'une erreur, le paiement peut être **annulé** (§2.4) ; une fois tous ses paiements
   annulés, l'élève redevient supprimable.
3. Pour tout le reste, **ni suppression aveugle, ni rupture automatique masquée** :
   - si l'élément est lié à d'autres entités non financières, la suppression directe est **bloquée** par un
     message qui énumère précisément les liens (ex. « Cet élève est actuellement rattaché à une classe et
     à 3 matières. Vous devez rompre ces liens avant de pouvoir effectuer la suppression définitive. ») ;
   - l'utilisateur **confirme explicitement** la rupture (bouton dédié, ou retrait manuel du lien depuis
     l'écran concerné) **avant** que la suppression ne s'exécute.

## 2. Décisions de conception

### 2.1 Deux actions distinctes, jamais fusionnées côté serveur

| Action | Effet | Ce qu'elle ne fait jamais |
|---|---|---|
| **Analyse** (lecture seule) | Calcule les blocages et les liens de l'élément. | Rien n'est modifié. |
| **Rupture des liens** (commande dédiée) | Rompt les liens non financiers listés, journalise lesquels. | Ne supprime pas l'élément ; ne touche jamais à un paiement. |
| **Suppression** (commande existante, durcie) | Supprime l'élément s'il n'a plus aucun lien. | Ne rompt aucun lien : s'il en reste, elle répond 409 avec la liste. |

Le bouton « Rompre les liens et supprimer » de l'interface enchaîne les deux commandes **après** que
l'utilisateur a lu la liste et coché la confirmation. Le serveur, lui, voit deux demandes séparées, avec deux
traces d'audit. Une rupture de lien ne peut donc jamais se produire sans demande explicite, et une
suppression ne peut jamais en déclencher une en silence. Si un nouveau lien apparaît entre les deux appels,
la suppression le détecte et renvoie 409.

### 2.2 Écart assumé : suppression « définitive » = suppression logique

La demande parle de « Hard Delete ». Cette conception conserve la **suppression logique**
(`IsDeleted`, `DeletedAt`, `DeletedBy`) pour trois raisons :

1. `AGENTS.md` règle n°6 : aucune suppression physique de donnée métier.
2. Le document du 2026-10-01 (décision 1 et §3.1) l'impose et prévoit des index uniques partiels qui
   rendent le matricule, le code, etc. réutilisables dès la suppression.
3. Une suppression physique échouerait de toute façon sur les clés étrangères restantes (notes, présences,
   historique) et détruirait la preuve d'audit.

Du point de vue de l'utilisateur, l'effet est identique : l'élément disparaît de toutes les listes,
sélecteurs, recherches, exports, et libère son identité unique. Aucun écran de restauration n'est ajouté
par cette conception. **Si une suppression physique est réellement exigée, c'est une décision distincte à
prendre explicitement** (elle impose de modifier la règle n°6 et la liste de l'exception financière).

### 2.3 Protection financière (règle absolue)

- Un élément est **bloqué sans option de rupture** dès qu'une donnée financière **active** le référence :
  paiement dont le statut n'est pas « Annulé », mouvement de stock, bulletin de paie émis, etc.
- Pour un élève : au moins un `Payment` non annulé rattaché à l'une de ses inscriptions, quel que soit le
  statut de l'inscription (annulée, abandon, transfert).
- **Un paiement annulé ne bloque plus.** Il n'est jamais effacé : il reste en base, visible dans
  l'historique financier avec la mention « annulé », son motif, sa date et son auteur.
- La protection se propage (décision validée) : une classe, un enseignant, une catégorie de frais ou une
  année scolaire référencés par un paiement non annulé (ou une donnée financière immuable équivalente) sont
  bloqués de la même façon (message adapté, §4). Chaîne : paiement → inscription → élève / classe / année ;
  paiement → ventilation → catégorie de frais ; enseignant → contrat → paie (à confirmer à l'implémentation).
- Le blocage financier est évalué **avant** toute autre règle et n'affiche aucun bouton de rupture.
- Ce que l'utilisateur peut faire à la place : annuler le paiement erroné (§2.4), ou changer le statut
  d'une inscription (abandon / transfert).

### 2.4 Annulation d'un paiement (nouvelle fonction, prérequis de la suppression)

Aujourd'hui aucune route n'émet le statut `PaymentStatus.Cancelled` (le statut et tous les calculs qui
l'ignorent existent déjà). Il faut donc l'implémenter :

- `POST /api/v1/finance/payments/{id}/cancel` avec `reason` obligatoire (3 à 500 caractères). Pas de
  `rowVersion` : le paiement ne porte pas de jeton optimiste ; c'est le verrou `xmin` de l'inscription
  (retrait du montant de `AmountPaid`) qui sérialise deux annulations ou un encaissement concurrent (409).
  Implémenté en PR0 (livré, testé) ; l'écran est l'onglet « Paiements » de la fiche élève.
- Effets, dans **une transaction** : statut → `Cancelled` ; motif, date et auteur enregistrés ; le montant
  est retiré de `Enrollment.AmountPaid` (le solde dû remonte) ; caches KPI financiers invalidés.
- Le reçu et son numéro officiel restent en base et dans les listes, marqués « annulé » : la numérotation
  n'a aucun trou. Le paiement n'est ni modifié dans son montant, ni supprimé (règle n°6).
- Rôles (décision du 2026-10-18) : **Directeur et Finance** (le comptable corrige sans attendre la
  direction). Le Secrétariat encaisse mais n'annule pas.
- **Délai de grâce de 48 h** après l'encaissement (`PaymentCancellationPolicy.GracePeriod`, source unique,
  exposée aux écrans via `cancellableUntil`). Pendant ce délai, l'annulation reste possible **même si la
  session de caisse est clôturée** : une erreur du lundi remarquée le mardi se corrige. Passé 48 h, elle est
  **bloquée définitivement** (409 `PAYMENT_CANCELLATION_WINDOW_EXPIRED`) ; un paiement déjà annulé est refusé
  (409 `PAYMENT_ALREADY_CANCELLED`).
- Conséquence assumée : annuler un paiement d'une caisse déjà clôturée fait que les états de caisse
  recalculés (journal de caisse PDF, rapports) ne comptent plus ce paiement, alors que les chiffres de
  clôture figés de la session ne bougent pas. Au-delà de 48 h, la correction relèvera d'une **contre-passation**
  (écriture inverse datée du jour, sans réécrire la clôture passée) — **le registre daté ci-dessous en est la
  première brique** ; l'annulation passé les 48 h (avec écriture de régularisation) reste à livrer plus tard.
- **Destination de l'argent** (décision du 2026-10-18), enregistrée avec le motif : `KeptBySchool` (simple
  correction, défaut — les écoles remboursent très rarement), `ReusedForEnrollment` (réutilisé pour l'inscription
  de l'enfant), `RefundedToParent`. C'est une trace comptable ; elle ne crée aucun avoir automatique.
- **Registre daté des encaissements** (`PaymentLedger`, décision du 2026-10-18 : « on ne réécrit jamais une
  journée arrêtée ») — règle unique appliquée à TOUS les états (journaux de caisse, clôture, trésorerie, TVA,
  tableau de bord Finance, consolidation des recettes) :
  1. un versement compte dans la période de son encaissement ; annulé PLUS TARD, il y **reste compté** et le journal
     le signale « Annulé le … — motif (destination) » ;
  2. annulé DANS la même période, il en sort (l'erreur et sa correction se compensent) ;
  3. l'annulation d'un versement d'une période ANTÉRIEURE est une **écriture négative** comptée dans la période où
     l'annulation est faite (journal du jour, session de caisse, mois de TVA).
  Résultat : `Σ encaissements − Σ annulations` sur n'importe quelle période, rien n'est compté deux fois, rien ne
  disparaît.
- **Caisse** : l'annulation est comptée dans la session ouverte de son auteur (`CancellationSessionId`) ; sans session
  ouverte (cas du Directeur), elle figure dans les états par date mais dans aucun écart de caisse. Le total encaissé
  est net de toutes les annulations ; les **espèces attendues** ne baissent que pour une annulation
  `RefundedToParent` (une simple correction ne déplace aucune espèce).
- **Reçu** : un reçu annulé reste consultable et réimprimable, avec un bandeau et un filigrane « ANNULÉ »
  (motif, date, destination) — il ne doit jamais pouvoir passer pour un reçu valable.
- Les rapports ci-dessus conservent leur comportement historique pour une ligne sans `CancelledAt`.
- Un paiement annulé dont l'élève est ensuite archivé reste lisible : les listes financières affichent
  « élève supprimé » avec son nom, au lieu de perdre la ligne à cause du filtre global sur les élèves.

## 3. Contrat d'API

Respecte le format d'erreur normalisé (`Volume_4_API_Design.md` §0.4) ; `schoolId` jamais lu depuis la requête.

| Route | Rôle | Réponse |
|---|---|---|
| `GET /api/v1/{ressource}/{id}/deletion-impact` | Analyse | 200 : `canDelete`, `financialBlock` (booléen + message), `links[]` |
| `POST /api/v1/{ressource}/{id}/unlink-dependencies` | Rupture explicite | 200 : l'analyse mise à jour ; 409 si `financialBlock` ; 409 `DEPENDENCIES_NOT_BREAKABLE` si un lien ne se rompt pas ici (ex. matériel prêté non restitué). Pas de `rowVersion` : la rupture ne modifie pas la fiche, les inscriptions sont protégées par leur propre verrou xmin. |
| `DELETE /api/v1/{ressource}/{id}?rowVersion=` | Suppression (existante) | 204 ; **409** `FINANCIAL_RECORDS_EXIST` ou `DEPENDENCIES_EXIST` avec `links[]` |

Forme d'un lien dans la réponse :

```json
{ "kind": "Enrollment", "label": "inscription(s)", "count": 1,
  "breakable": true, "breakAction": "Annuler l'inscription et l'archiver",
  "sample": ["Terminale S2 — 2026/2027"] }
```

Rôles : identiques à la suppression actuelle (Directeur, Secrétariat pour les élèves). Aucun nouveau droit.

## 4. Messages (français, sans jargon technique)

- **Blocage financier — élève :** « Impossible de supprimer cet élève car il possède des transactions
  financières / reçus enregistrés. » (texte imposé, identique en toutes circonstances)
- Autres entités financières : « Impossible de supprimer {article} {nom} car {il/elle} est référencé(e) par
  des transactions financières / reçus enregistrés. »
- **Dépendances rompables :** « {Nom} est actuellement rattaché(e) à {liste précise avec nombres}. Vous
  devez rompre ces liens avant de pouvoir effectuer la suppression. »
  Exemple : « Cet élève est actuellement rattaché à 1 classe, 1 inscription et 3 matières. »
- La liste nomme toujours les entités et les quantités ; jamais « données liées » sans détail
  (le message générique actuel « cet élément possède des données liées » disparaît).

## 5. Interface

Un composant partagé « Suppression guidée » remplace les confirmations actuelles :

1. Au clic sur Supprimer, appel de l'analyse.
2. **Aucun lien** : confirmation simple, puis suppression.
3. **Blocage financier** : message imposé, aucun bouton de suppression ni de rupture ; lien vers le
   changement de statut d'inscription le cas échéant.
4. **Liens rompables** : liste détaillée (type, nombre, exemples), case « Je comprends que ces liens seront
   rompus », boutons « Rompre les liens et supprimer » et « Retour ». L'utilisateur peut aussi fermer la
   fenêtre et retirer les liens à la main depuis l'écran concerné, puis relancer la suppression.
5. Après succès : rafraîchissement des listes et invalidation des caches KPI déjà gérés par les handlers.

## 6. Catalogue des dépendances

Légende : **B** = bloquant financier (aucune rupture) · **R** = rupture proposée · **E** = enfant propre à
l'élément, archivé avec lui après confirmation (listé quand même dans le message).

| Élément | Liens | Type |
|---|---|---|
| **Élève** | Paiements (via inscriptions) | **B** |
| | Inscriptions sans paiement | R — inscription annulée puis archivée (le numéro de reçu d'inscription reste dans la ligne archivée) |
| | Rattachement à une classe | R — détachement |
| | Matières suivies, dispenses | R — retrait |
| | Notes | R — archivées |
| | Présences, retards, sorties anticipées, discipline, convocations, remarques de bulletin, suivi Coran, dossiers d'examen | E |
| **Classe** | Inscriptions avec paiement | **B** |
| | Élèves rattachés, inscriptions sans paiement | R — détachement / annulation + archivage |
| | Matières de la classe, affectations d'enseignants, créneaux d'emploi du temps, frais de classe, journal, feuilles de présence | R / E selon l'inventaire |
| **Enseignant** | Bulletins de paie émis | **B** |
| | Affectations, matières enseignées, créneaux, pointages | R |
| **Salle** | Créneaux d'emploi du temps, lits/affectations d'internat | R |
| **Bâtiment** | Salles | Traité ligne par ligne (pas de rupture en un clic) |
| **Matière** | Notes déjà saisies | R — archivées après confirmation, avec nombre de notes affiché |
| | Matières de classe, enseignants, dispenses, surcharges de coefficient | R |
| | Activités d'un domaine | Ligne par ligne (même raison que le code actuel : ne jamais archiver des notes en cascade sans que l'école ne le décide) |
| **Frais / catégorie** | Paiements référençant la catégorie | **B** |
| **Année scolaire** | Inscriptions avec paiement | **B** ; sinon règles actuelles de `DeleteSchoolYear` |
| **Inventaire** | Mouvements de stock | **B** (append-only) |

Ce catalogue est un point de départ : l'inventaire exhaustif est une étape d'implémentation (§8).

## 7. Garanties techniques

- **Transaction unique** par commande (rupture ou suppression) ; verrou optimiste `RowVersion` (règle n°5).
- **Audit :** chaque rupture écrit un événement listant les liens rompus (type, nombre, identifiants) et
  l'acteur ; chaque suppression écrit le sien.
- **Multi-tenant :** requêtes d'analyse bornées par le Global Query Filter + RLS ; aucun `IgnoreQueryFilters`
  sauf pour tester l'existence d'un paiement lié à une inscription déjà archivée, avec le `SchoolId` du
  tenant posé explicitement.
- **Filtre `IsDeleted` explicite** dans les comptages de liens (leçon du faux positif « élève supprimé mais
  encore rattaché » déjà corrigé dans `DeleteClassroomCommandHandler`).
- **Quota d'élèves :** un élève supprimé libère sa place dans la tranche d'effectif.
- **Garde-fou structurel :** un test parcourt le modèle EF, liste toutes les clés étrangères pointant vers
  une entité supprimable, et échoue si l'une n'est classée ni B, ni R, ni E. Toute nouvelle entité liée
  doit donc être classée pour passer la CI : c'est ce qui tient la promesse « tout est supprimable ».

## 8. Tests exigés

- Finance et multi-tenant : tests obligatoires (`AGENTS.md`, « Ne jamais faire »).
- Élève avec paiement : 409 `FINANCIAL_RECORDS_EXIST` avec le message imposé, y compris inscription
  annulée, paiement annulé ou inscription d'une année passée ; `unlink-dependencies` répond aussi 409.
- Élève sans paiement avec classe + inscription + 3 matières : `DELETE` → 409 qui liste exactement ces liens ;
  `unlink` → 200, liens rompus, paiements intacts ; `DELETE` → 204.
- Lien créé entre `unlink` et `DELETE` : la suppression répond 409.
- Concurrence : `rowVersion` périmé → 409.
- Isolation : élément d'une autre école → 404 sur les trois routes.
- Une rupture écrit bien un événement d'audit ; la suppression libère l'unicité (matricule réutilisable).
- Test de couverture des clés étrangères (§7).

## 9. Découpage de la livraison

0. **PR0 — Annulation de paiement** (§2.4) : route, écran, tests de non-régression des rapports.
1. **PR1 — socle + Élève** : analyse, rupture, composant d'interface, messages, test de couverture FK.
   *Livrée (branche `feat/guided-student-deletion`, plan `docs/superpowers/plans/2026-10-18-guided-student-deletion.md`).*
   Précisions d'implémentation : un élève ne peut jamais « ne pas avoir de classe » (`Student.ClassroomId` est obligatoire) —
   le rattachement à la classe n'est donc pas un lien à rompre, il disparaît avec la fiche archivée ; le matricule et l'IEN
   redeviennent disponibles par des index uniques partiels, mais la numérotation automatique reste un compteur sans trou
   (elle ne réémet pas d'elle-même un numéro supprimé) ; l'historique financier d'un élève supprimé reste lisible
   (« (élève supprimé) » dans les états).
2. **PR2 — Classe, Matière, Enseignant.**
   *Livrée (branche `feat/guided-deletion-classes-subjects-teachers`, plan `docs/superpowers/plans/2026-10-18-guided-deletion-classes-subjects-teachers.md`).*
   Précisions d'implémentation : socle générique (`EntityDeletionPolicy`, `EntityDeletionAnalyzer`) partagé avec les élèves ;
   classe — un paiement non annulé sur une inscription bloque, les **élèves rattachés ne se rompent pas** (à transférer ou à
   supprimer d'abord, `Student.ClassroomId` étant obligatoire) ; matière — les notes sont archivées après confirmation, les
   **activités d'un domaine ne se rompent pas** (une par une, comme avant) ; enseignant — une fiche de paie bloque, le contrat
   (avec historique et relevés d'heures) et les affectations se rompent, le matériel prêté non restitué ne se rompt pas, et les
   **matières enseignées sont intrinsèques à la fiche** (la création en exige au moins une) : archivées avec elle, annoncées dans
   la fenêtre de confirmation. Un enseignant qualifié sans affectation est donc supprimable directement.
3. **PR3 — Salle, Bâtiment, Frais, Année scolaire, Inventaire.**

Chaque PR est indépendante pour la recette ; la PR1 pose les contrats que les suivantes réutilisent.

## 10. Décisions du commanditaire (2026-10-17) et points restants

**Validées :**
- Option A : un paiement annulé ne bloque plus ; on annule le paiement puis on supprime l'élève (§2.3, §2.4).
- Implémenter la route d'annulation de paiement ET la route de suppression durcie.
- Suppression logique : l'élément disparaît de tous les écrans et listes, son matricule redevient
  réutilisable, la ligne est conservée en base avec l'auteur et la date de suppression (§2.2).
- Propagation du blocage financier aux classes, enseignants, frais et années scolaires (§2.3).

**Retenus par défaut, sauf objection (non contredits) :** points 1 et 3 ci-dessous.

**Points d'origine :**
1. **Reçu d'inscription sans paiement.** Chaque inscription porte un numéro de reçu (`Enrollment.ReceiptNumber`)
   même sans ligne de paiement. Ce numéro doit-il bloquer la suppression de l'élève (assimilé à un « reçu
   enregistré ») ? **Hypothèse retenue :** non, seul un paiement bloque ; le numéro reste dans l'inscription
   archivée. Si la réponse est oui, un élève inscrit n'est plus jamais supprimable.
2. **Propagation du blocage** aux classes, enseignants, frais et années (§2.3). Hypothèse : oui.
3. **Notes** d'un élève ou d'une matière : archivées après confirmation visible, avec le nombre affiché.
   Hypothèse : oui.
4. **Suppression physique** : confirmer que la suppression logique (§2.2) suffit.
