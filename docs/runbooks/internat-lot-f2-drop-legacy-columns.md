# Runbook — Internat, lot F2 : suppression des colonnes héritées

**Statut : REPORTÉ (décision du 14/11/2026).** Ce document prépare l'opération ; la migration `DropLegacyBoardingColumns`
n'est volontairement **pas** écrite ni livrée avec la PR #134 : une migration destructive ne doit pas partir avec une PR
d'ensemble. Elle sera codée sur une branche et une PR dédiées le moment venu.

## Ce que fait F2

Supprime de la table `enrollments` les deux colonnes de l'ancien modèle d'hébergement, `BoardingStatus` (régime) et `RoomId`
(chambre), ainsi que la clé étrangère composite `(SchoolId, RoomId)` et l'index `(SchoolId, RoomId)`. Depuis le lot F1, plus
aucun code ne les lit ni ne les écrit (propriétés fantômes EF) : l'hébergement vit dans `boarding_enrollments`, les lits et les
pavillons. Aucun changement visible pour les utilisateurs.

Aussi dans F2 : retrait des propriétés fantômes de `EnrollmentConfiguration`, suppression de `LegacyBoardingColumnsGuardTests`,
et adaptation des tests qui sèment encore ces colonnes (`BoardingReconciliationTests`, `BoardingBackfillMigrationTests`, aides de
semis des tests d'inscription) : ils devront migrer le schéma jusqu'à la version précédente, semer en SQL, puis appliquer la
migration testée.

## Conditions de lancement (toutes obligatoires)

1. F1 qualifié (suites unitaires, intégration, fonctionnelles, JS) **et recette navigateur du formulaire d'inscription** (élève
   interne) faite.
2. F1 en production depuis au moins une livraison complète, sans incident d'hébergement (proposition : 2 à 4 semaines).
3. **Porte de sécurité à zéro**, en préproduction ET en production (requête ci-dessous).
4. Sauvegarde de la base faite ; export des deux colonnes conservé (requête ci-dessous).
5. **Toutes les instances sont en F1 ou plus** et les anciennes sont arrêtées : une instance antérieure mappe encore les
   colonnes, et chacune de ses requêtes sur `enrollments` échouerait après F2 (panne générale de l'inscription). Pas de
   déploiement progressif pour F2.
6. Fenêtre calme : hors campagne d'inscriptions (par exemple vacances de fin d'année).

## Porte de sécurité

À exécuter AVANT F2 avec le rôle propriétaire (la RLS ne s'applique pas à lui), sur chaque base :

```sql
-- Doit renvoyer 0 ligne : aucune inscription active en régime Interne/Demi-pensionnaire sans séjour actif correspondant.
SELECT e."SchoolId", e."Id", e."BoardingStatus", e."RoomId"
FROM enrollments e
JOIN school_years y ON y."Id" = e."SchoolYearId" AND y."SchoolId" = e."SchoolId"
WHERE y."IsActive"
  AND NOT e."IsDeleted"
  AND e."Status" <> 'Cancelled'
  AND e."BoardingStatus" <> 'Externe'
  AND NOT EXISTS (
        SELECT 1 FROM boarding_enrollments be
        WHERE be."EnrollmentId" = e."Id" AND be."IsActive" AND NOT be."IsDeleted");
```

Un résultat non nul signale soit une réconciliation manquée, soit une **ancienne instance qui écrit encore les colonnes** :
ne pas poursuivre, corriger d'abord.

## Export de précaution

```sql
\copy (SELECT "Id","SchoolId","BoardingStatus","RoomId" FROM enrollments) TO 'enrollments_boarding_legacy.csv' CSV HEADER
```

## Déroulé

1. Annoncer l'arrêt, arrêter **toutes** les instances.
2. Sauvegarde + export ; porte de sécurité à zéro.
3. Appliquer la migration `DropLegacyBoardingColumns` (rôle propriétaire `sama_ecole`, jamais le rôle applicatif).
4. Démarrer la version F2 ; contrôler une inscription (Externe puis Interne) et l'écran Internat › Occupation.
5. Retour arrière si besoin : `Down()` recrée les colonnes, la FK et l'index et **re-dérive** les valeurs depuis les séjours actifs
   (approximation documentée, pas une restitution exacte).

## Vérifications après F2

- `reset_school_data` et `delete_school_year` réussissent sur une école avec séjours, sorties et pointages (aucune purge ne touche
  les colonnes supprimées ; un test d'intégration doit le prouver avant livraison).
- Aucune requête de l'application ne référence `BoardingStatus`/`RoomId` sur `enrollments` (le champ `boardingStatus`/`roomId` de
  `POST /enrollments` reste un champ de contrat d'API, il n'est plus une colonne).
