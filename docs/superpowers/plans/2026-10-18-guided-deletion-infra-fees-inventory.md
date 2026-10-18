# Suppression guidée des bâtiments, salles, catégories de frais et inventaire (PR3) — plan

> Exécution en ligne dans la session. Même méthode que les PR1/PR2 : test d'abord, un commit par tâche. S'appuie sur le socle générique de la PR2 (`EntityDeletionPolicy`, `EntityDeletionAnalyzer`, `DeletionDependencies`).

**Goal :** appliquer la suppression guidée (blocage financier, liens énumérés, rupture explicite) aux **bâtiments, salles, catégories de frais, catégories d'inventaire et biens d'inventaire**.

**Spec :** `docs/superpowers/specs/2026-10-17-deletion-dependency-guard-design.md` (§6).

## Décisions

1. **Bâtiment** : les salles rattachées sont un lien **non rompable** ici (comme avant : « traité ligne par ligne »).
2. **Salle** : les biens d'inventaire qui y sont rangés sont **détachés** (`RoomId` mis à vide, rien n'est archivé) — nouvelle brique `DeletionDependencies.Detach`. Les autres liens sont classés par le test de couverture du modèle EF.
3. **Catégorie de frais** : bloquée (financier) si une inscription la facture (`EnrollmentFeeLine`) ou si un paiement non annulé l'a ventilée (`PaymentBreakdown`). Les lignes du barème (`ClassFee`) se **rompent explicitement** — l'ancien handler les archivait en cascade silencieuse.
4. **Catégorie d'inventaire** : les biens rattachés sont un lien non rompable (supprimer ou changer de catégorie d'abord).
5. **Bien d'inventaire** : un prêt non restitué est non rompable ; les prêts clos (historique) sont *intrinsèques* — archivés avec le bien ; le journal de stock (`StockMovement`, append-only) est une trace immuable ignorée (comme avant : le bien s'archive, le journal reste).
6. **Interface** : `guided-delete.js` gère plusieurs ressources par page (`resource` surchargeable à l'ouverture) et une suppression sans `rowVersion` (catégorie de frais).
7. **Hors périmètre, PR suivante** : années scolaires (parcours existant à confirmation saisie + mode test/réel), lignes de barème (`ClassFee` : aucun lien, comportement inchangé), décaissements (contre-écriture), modules Internat/Daara/Tenues.

## Tâches

1. Brique `Detach` + gestion des liens ignorés dans le test de couverture.
2. Politiques des cinq éléments + tests (analyse, rupture, couverture FK, isolation).
3. Requêtes d'analyse, commandes de rupture, `DELETE` durcis, routes (Buildings, Rooms, Inventory ×2, Finance).
4. Tests fonctionnels + mise à jour des anciens tests.
5. Interface : mixin multi-ressources, pages Bâtiments & Salles, Inventaire, Frais + tests JS.
6. Recette navigateur, documentation (`openapi.yaml`, spec), PR.
