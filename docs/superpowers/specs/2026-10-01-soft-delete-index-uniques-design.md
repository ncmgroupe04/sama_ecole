# Conception — Soft Delete global et index uniques

**Date :** 2026-10-01
**Statut :** conception validée par le commanditaire ; revue du présent document attendue avant implémentation.
**Périmètre de livraison :** PR1 — harmonisation du Soft Delete et des contraintes d’unicité.
**Travail séparé :** PR2 — frais optionnels (tenues/uniformes), hors périmètre de cette conception.

## 1. Décisions validées

1. Les suppressions des données métier modifiables sont logiques : elles renseignent `IsDeleted`, `DeletedAt` et `DeletedBy`.
2. Un nouvel enregistrement ne réactive jamais silencieusement un enregistrement supprimé. Lorsqu’un doublon supprimé est trouvé, l’application renvoie un conflit explicite et propose une restauration.
3. La restauration est une action volontaire. Elle échoue avec un conflit clair si une autre ligne active occupe déjà la même identité unique.
4. Les index d’unicité des identités réutilisables sont des index uniques partiels PostgreSQL limités aux lignes actives : `WHERE "IsDeleted" = false` (ou un prédicat métier additionnel déjà nécessaire).
5. Paiements, reçus/historique financier, journal d’audit et mouvements de stock ne sont pas supprimables logiquement ni physiquement depuis l’application. Les corrections financières et de stock se font par contre-écriture ou mouvement inverse.
6. L’association enseignant-matière est révocable : son retrait est enregistré comme suppression logique afin de conserver la relation historique nécessaire aux bulletins et consultations historiques.
7. Les filtres globaux EF Core et la sécurité RLS PostgreSQL restent tous deux obligatoires pour les données tenant. La RLS n’est pas remplacée par le filtre EF et ne dépend pas de l’état `IsDeleted`.

## 2. Objectifs et limites

La PR1 doit rendre cohérents le cycle de vie des données métier modifiables, les règles d’unicité, les parcours de restauration et les protections multi-tenant. Elle ne doit ni effacer ni réécrire silencieusement des données existantes.

La demande initiale dit « toute entité créée ou saisie par un utilisateur ». Cette règle s’applique aux données métier modifiables, après classification explicite. Elle ne signifie pas qu’un journal comptable, une preuve d’audit, un reçu émis, un mouvement de stock, un jeton technique ou une projection de lecture doit devenir supprimable. Les exceptions ci-dessus sont intentionnelles et doivent apparaître dans l’inventaire de la PR.

Ne font pas partie de la PR1 :

- `IsOptional`, le choix des frais à l’inscription, ou le calcul des montants des tenues/uniformes — PR2 dédiée ;
- une purge périodique des tombstones ou l’effacement de données métier en base ;
- la réécriture rétroactive de reçus, paiements, mouvements de stock ou journaux ;
- toute déduplication automatique de données lors d’une migration.

## 3. Modèle de cycle de vie

### 3.1 Suppression logique

Toute commande de suppression utilisateur concernant une entité classée « modifiable et supprimable » doit appeler le mécanisme partagé du domaine pour établir `IsDeleted = true`, l’horodatage UTC `DeletedAt` et l’acteur `DeletedBy`. Une répétition de la même demande est idempotente ou renvoie le résultat métier déjà supprimé ; elle ne crée pas une seconde suppression physique.

Les lectures ordinaires ne retournent pas les tombstones. Les listes, sélecteurs, recherches, exports et validations de références doivent donc ignorer les entités supprimées, sauf parcours de corbeille/restauration explicitement autorisé.

Une suppression en cascade depuis l’interface ne doit jamais entraîner un `DELETE` physique. Les enfants modifiables qui doivent disparaître avec un parent sont supprimés logiquement dans la même transaction, selon des règles d’agrégat définies par domaine. Les paiements, reçus, événements d’audit et mouvements de stock sont conservés. Si une donnée immuable référence le parent, la règle du domaine décide explicitement si la suppression logique est bloquée ou si le parent est seulement désactivé ; elle ne supprime ni ne modifie l’écriture immuable.

### 3.2 Restauration et recréation

La création recherche d’abord une identité active, puis une identité identique supprimée dans le tenant courant :

- identité active trouvée : conflit de doublon existant, inchangé ;
- identité supprimée trouvée : conflit fonctionnel `ARCHIVED_ENTITY_EXISTS`, avec message indiquant que l’élément existe dans les éléments supprimés et une action de restauration ; aucune nouvelle ligne n’est créée ;
- aucune identité trouvée : création normale.

La restauration cible un identifiant précis, vérifie le tenant courant, contrôle les dépendances et les contraintes d’unicité actives, puis remet `IsDeleted = false` et efface les champs courants de suppression. Elle est atomique. Si un élément actif réutilise déjà la même identité, la restauration échoue avec un conflit `ACTIVE_ENTITY_CONFLICT` ; l’utilisateur doit résoudre le conflit par les opérations normales de renommage, suppression ou sélection de l’élément à conserver. Il n’y a aucune fusion de données implicite.

Le conflit de création et la restauration doivent utiliser le format d’erreur API normalisé déjà adopté par le projet. Le message destiné à l’utilisateur reste compréhensible et ne révèle aucune donnée d’un autre établissement.

### 3.3 Données immuables et relations révocables

Les paiements, leurs reçus/preuves, l’audit et les mouvements de stock sont append-only pour les opérations utilisateur. Aucun endpoint de suppression/restauration n’est exposé pour ces données. Une erreur se corrige par une écriture compensatrice qui référence l’écriture d’origine.

Une relation enseignant-matière peut être révoquée pour l’avenir. La révocation marque l’association comme supprimée et conserve son historique. Les consultations historiques (notamment les bulletins) doivent pouvoir résoudre l’association qui était en vigueur à la période considérée ; elles ne doivent pas dépendre uniquement des sélecteurs actifs. La PR1 doit identifier ces lectures et s’assurer que leurs requêtes historiques incluent l’historique nécessaire sans ouvrir les données d’un autre tenant.

Les tables techniques éphémères (par exemple jetons à durée de vie limitée) ne reçoivent pas de parcours corbeille destiné aux utilisateurs. Toute opération de nettoyage technique éventuelle reste hors parcours métier, documentée et limitée aux seules tables techniques autorisées.

## 4. Politique des index uniques

### 4.1 Identités réutilisables

Pour une identité unique dont l’enregistrement peut être supprimé puis restauré, la contrainte PostgreSQL porte sur les seules lignes actives. Exemple EF Core :

```csharp
builder.HasIndex(x => new { x.SchoolId, x.Name })
    .IsUnique()
    .HasFilter("\"IsDeleted\" = false");
```

Les colonnes de clé ne contiennent pas `IsDeleted`. Cette forme autorise plusieurs tombstones historiques pour une même identité, tout en garantissant une seule identité active. Les prédicats additionnels nécessaires (valeur non nulle, statut actif, portée optionnelle) sont conservés et combinés à `NOT "IsDeleted"`.

La portée tenant (`SchoolId`) reste dans l’index pour les identités propres à un établissement. Les index globaux, tels que les identifiants de compte ou de transaction, restent globaux s’ils le sont déjà par règle métier.

### 4.2 Unicité historique et non réutilisable

Les numéros de reçu, références de transaction, clés d’idempotence consommées, codes de vérification et autres identifiants immuables conservent une unicité permanente lorsque le contrat métier le requiert. Le statut de suppression ne libère jamais ces identifiants.

Un index unique ne doit pas être modifié sur la seule base de la présence de `IsDeleted` dans l’entité : chaque index est classé selon l’identité et la durée de vie métier qu’il protège. Les index non uniques et les index partiels dont le filtre porte sur un autre état (par exemple une seule session active) sont revus séparément, pas convertis mécaniquement.

## 5. Tenant, Query Filters et RLS

- Le filtre global tenant existant continue à imposer `SchoolId == tenant courant` et `!IsDeleted` aux entités tenant.
- Les règles RLS PostgreSQL restent actives et doivent couvrir toutes les tables tenant conformément à `TenantTables` et aux migrations existantes.
- Les opérations de corbeille/restauration qui doivent voir les tombstones contournent seulement le filtre EF nécessaire, puis appliquent explicitement le `SchoolId` du contexte tenant et une autorisation de rôle. La RLS demeure la seconde barrière.
- Une requête ne doit jamais accepter un `SchoolId` contrôlé par le client pour élargir son périmètre.
- Les scénarios multi-tenant couvrent les lectures ordinaires, la recherche d’un doublon supprimé, la corbeille et la restauration. Ils doivent prouver qu’aucun identifiant provenant d’un autre établissement n’est détectable ou restaurable.

## 6. Migration et déploiement

La migration est additive et explicite. Avant de créer un nouvel index partiel unique, le travail repère les doublons actifs qui violeraient cette contrainte. Le rapport de précontrôle comprend la table, les colonnes d’identité et le nombre/l’identifiant des lignes en conflit ; il n’expose pas de contenu sensible inutile.

Si des doublons actifs existent, la migration ne choisit pas elle-même une ligne, ne fusionne rien et ne supprime aucune donnée. Le déploiement s’arrête avec une erreur explicite. Les conflits doivent être arbitrés et corrigés par une action métier approuvée avant la reprise de migration.

Les migrations déjà appliquées ne sont pas modifiées. Pour chaque index, la migration suivante supprime l’ancien index ciblé puis crée le nouvel index avec son nom et son filtre documentés. Les scripts générés sont revus pour confirmer le schéma, la portée, le filtre et l’absence de suppression de lignes.

**Précaution de retour arrière :** après que plusieurs tombstones d’une même identité ont été permis, l’ancien index qui incluait `IsDeleted` pourrait ne plus être recréable. Le retour arrière opérationnel doit donc privilégier un correctif en avant. Un downgrade ne doit pas promettre de rétablir une contrainte incompatible avec les données déjà valides.

## 7. Audit requis pendant l’implémentation

La PR doit joindre ou référencer un inventaire exhaustif qui classe chaque entité persistée et chaque index unique dans les catégories suivantes :

1. référence/donnée métier modifiable et supprimable ;
2. donnée opérationnelle supprimable sous conditions ou par état ;
3. historique/registre immuable ;
4. donnée technique hors corbeille utilisateur ;
5. projection/read model non éditable.

L’inventaire doit aussi lister les commandes et interfaces de suppression, les appels EF `Remove`/`RemoveRange`, les cascades de relations, les SQL directs, les traitements techniques de purge, les parcours qui ignorent les filtres, ainsi que les lectures historiques dépendant d’associations révoquées. Tout écart à cette politique est justifié explicitement dans la PR.

## 8. Stratégie de tests et critères d’acceptation

La PR1 est acceptable lorsque les tests couvrent au minimum :

- suppression logique complète (`IsDeleted`, `DeletedAt`, `DeletedBy`) et exclusion des lectures ordinaires ;
- suppression répétée sans `DELETE` physique ;
- création quand aucun doublon n’existe ;
- création avec identité active (conflit existant) et identité supprimée (`ARCHIVED_ENTITY_EXISTS` avec proposition de restauration) ;
- restauration d’une ligne supprimée ;
- conflit de restauration quand une ligne active a repris la même identité ;
- possibilité d’avoir plusieurs tombstones historiques et une seule ligne active pour l’identité ;
- maintien des identifiants permanents de reçus et de paiements ;
- impossibilité de supprimer un paiement, reçu immuable, événement d’audit ou mouvement de stock via l’application ; validation des opérations compensatrices déjà prévues par chaque domaine ;
- révocation enseignant-matière sans perte des lectures historiques des bulletins ;
- isolation tenant sur listes actives, recherche de tombstones et restauration, avec tests `Category=MultiTenant` ;
- migration sur données sans collision, et échec explicite du précontrôle lorsqu’une collision active est simulée ;
- cohérence du modèle EF et de la migration PostgreSQL, sans changement non intentionnel des index uniques historiques.

La suite complète pertinente doit être exécutée avant livraison, en particulier les tests d’intégration PostgreSQL et la catégorie multi-tenant. Aucun résultat de test n’est revendiqué par ce document de conception.

## 9. Découpage et livraison

**PR1 — Soft Delete et index uniques** : inventaire, corrections des parcours, règles de restauration/conflit, index partiels, migrations, protection des données immuables, couverture de tests et documentation des exceptions.

**PR2 — Frais optionnels** : `FeeCategory.IsOptional`, migrations dédiées, sélection à l’inscription/réinscription et gel des lignes de frais validées. Cette PR aura sa propre conception/plan d’implémentation et ses propres tests ; elle ne doit pas être mélangée aux migrations de la PR1.

Le calendrier global validé reste de 3 à 5 semaines, à confirmer après inventaire détaillé et exécution du précontrôle des données. Toute collision de production ou dépendance historique non identifiée peut modifier l’ordre et la durée des étapes, sans autoriser une déduplication silencieuse.

## 10. Vérification de cohérence

- La suppression logique ne s’applique pas aux écritures financières, reçus immuables, mouvements de stock ou événements d’audit.
- Les index partiels régissent seulement les identités réutilisables ; les identifiants historiques restent uniques.
- Une suppression ne restaure rien automatiquement et une restauration ne fusionne rien.
- Le query filter EF et la RLS sont maintenus ensemble.
- Les frais optionnels restent un chantier isolé dans la PR2.
- Le code n’a pas été modifié dans cette étape documentaire.
