# Soft Delete global et index uniques — Plan d’implémentation

> **Pour les agents d’exécution :** suivre ce plan tâche par tâche avec `superpowers:executing-plans`, en laissant une revue entre les lots. Chaque étape utilise les cases à cocher pour suivre l’avancement.

**Objectif :** harmoniser la suppression logique, la restauration volontaire, l’unicité des données actives, la traçabilité des relations et la protection des registres immuables sur la plateforme.

**Architecture :** conserver `AuditableEntity`, le filtre EF Core tenant + suppression et les policies PostgreSQL RLS comme socle. Introduire des index partiels pour les identités réutilisables, des conflits métier explicites lorsqu’une identité n’existe qu’à l’état supprimé, et des commandes de restauration typées par domaine. Maintenir les écritures comptables, les reçus, l’audit et le stock append-only ; traiter `TeacherSubject` comme une association révocable.

**Technologies :** .NET 9, C#, EF Core 9, Npgsql, PostgreSQL 16, MediatR, xUnit, Testcontainers PostgreSQL, ASP.NET Core MVC/API.

**Conception validée :** `docs/superpowers/specs/2026-10-01-soft-delete-index-uniques-design.md`

## Contraintes globales

- PostgreSQL est le seul moteur de base de données.
- Toute table métier tenant conserve `SchoolId`, une policy RLS et le filtre global EF Core.
- L’application utilise `sama_ecole_app` (`NOSUPERUSER`, `NOBYPASSRLS`, non-propriétaire des tables) ; les migrations s’exécutent avec le rôle propriétaire.
- Toute suppression de donnée métier modifiable renseigne `IsDeleted`, `DeletedAt`, `DeletedBy` ; aucune suppression physique depuis un parcours utilisateur.
- Paiements, reçus/historique financier, événements d’audit et mouvements de stock ne sont pas supprimables ; leur correction est compensatrice.
- Une restauration est explicite, tenant-scoped et refuse une identité déjà utilisée par une ligne active.
- Les conflits d’écriture et d’état métier sont des HTTP 409 normalisés, jamais un 500 ni un écrasement silencieux.
- Toute nouvelle migration est additive ; ne pas modifier une migration déjà appliquée.
- Les tests des modules Finance, Notes et multi-tenant doivent couvrir la modification correspondante.

---

## Carte des fichiers et livrables

| Zone | Fichiers existants concernés | Livrables prévus |
|---|---|---|
| Conventions et filtre global | `AGENTS.md`, `src/SamaEcole.Domain/Common/AuditableEntity.cs`, `src/SamaEcole.Persistence/ApplicationDbContext.cs` | Inventaire exhaustif avec classe de cycle de vie par entité et index |
| Suppressions et relations | handlers `Delete*`, `UpdateTeacherCommandHandler.cs`, `TeacherSubjectConfiguration.cs`, contrôleurs/API et écrans existants | suppression/restauration alignées avec les règles de domaine |
| Index | `src/SamaEcole.Persistence/Configurations/*Configuration.cs` | index partiels actifs pour les identités réutilisables ; unicité historique conservée pour les registres immuables |
| Migrations | `src/SamaEcole.Persistence/Migrations/` et `ApplicationDbContextModelSnapshot.cs` | migration `HarmonizeSoftDeleteUniqueIndexes` et migration de retrait des grants `DELETE` obsolètes |
| Erreurs API | `BusinessRuleException.cs`, `ExceptionHandlingMiddleware.cs`, contrôleurs des domaines concernés | code HTTP 409 stable `ARCHIVED_ENTITY_EXISTS` avec action de restauration |
| Tests d’intégration | `tests/SamaEcole.IntegrationTests/Common/RlsTestDatabase.cs` et tests par domaine | tests PostgreSQL avec rôle applicatif, RLS, indices, restauration et immuabilité |
| Documentation | `docs/superpowers/specs/2026-10-01-soft-delete-index-uniques-design.md` | `docs/architecture/soft-delete-index-inventory.md` et notes de migration |

Les fichiers non suivis préexistants dans le checkout (dont `.claude/settings.local.json` et la migration vide `20260930161254_AddProfileEtablissementToSchoolSettings`) sont hors périmètre : les lire si nécessaire pour ordonner les migrations, mais ne pas les modifier, supprimer ou inclure dans un commit PR1.

---

## Tâche 1 — Inventorier les cycles de vie, suppressions et contraintes

**Fichiers :**

- Lire : `AGENTS.md`, `docs/Volume_3_DDS.md`, `docs/Volume_4_API_Design.md`, `docs/Volume_7_Security.md`, `docs/Volume_8_Test_Strategy.md`.
- Lire : `src/SamaEcole.Domain/Entities/`, `src/SamaEcole.Persistence/Configurations/`, `src/SamaEcole.Persistence/Migrations/`.
- Lire : `src/SamaEcole.Application/` pour les commandes d’écriture et suppressions.
- Créer : `docs/architecture/soft-delete-index-inventory.md`.

- [ ] Recenser chaque entité persistée et la classer : référence modifiable, opérationnelle supprimable sous condition, registre immuable, technique éphémère ou read model.
- [ ] Recenser les appels `Remove`, `RemoveRange`, `ExecuteDelete`, SQL `DELETE`, suppressions en cascade et grants `DELETE`. Distinguer code applicatif, migration, test et maintenance technique.
- [ ] Recenser chaque index unique/contrainte unique et préciser portée, identité métier, réutilisabilité après suppression, filtre actuel et test couvrant la règle.
- [ ] Repérer les parcours `IgnoreQueryFilters()` ; confirmer que chacun est soit sans tenant, soit borné explicitement au `SchoolId` courant et compatible avec la RLS.
- [ ] Repérer les agrégats dont la suppression a un effet sur des reçus, paiements, écritures d’audit, mouvements de stock, bulletins ou autres données historiques ; écrire pour chacun la règle `bloquer` ou `désactiver le parent`.
- [ ] Classer les tables et commandes qui requièrent une restauration utilisateur ; chaque identité unique supprimée devra offrir une action de restauration ou une navigation vers celle-ci.
- [ ] Documenter les doublons actifs préexistants qui empêcheraient la création d’un index partiel. Aucun doublon ne sera fusionné ou effacé automatiquement.

**Vérification :** l’inventaire contient une ligne pour chaque entité, index unique, commande de suppression et table ayant un grant `DELETE`. Revue manuelle croisée avec `ApplicationDbContextModelSnapshot.cs` et les migrations historiques avant de passer à la tâche 2.

## Tâche 2 — Révoquer les associations sans effacer leur historique

**Fichiers :**

- Modifier : `src/SamaEcole.Application/Teachers/Commands/UpdateTeacher/UpdateTeacherCommandHandler.cs`.
- Modifier : `src/SamaEcole.Persistence/Configurations/TeacherSubjectConfiguration.cs`.
- Créer : migration additive `RevokeDeleteOnTeacherSubjects` dans `src/SamaEcole.Persistence/Migrations/` et son fichier designer.
- Modifier : `tests/SamaEcole.IntegrationTests/Teachers/TeacherSubjectUnassignmentTests.cs`.
- Ajouter : tests d’intégration de lecture historique des bulletins pour une association révoquée dans le dossier `tests/SamaEcole.IntegrationTests/Teachers/` ou `ReportCards/`, au voisinage des tests existants concernés.

- [ ] Écrire d’abord le test d’intégration qui retire une matière et prouve que la ligne existe encore avec `IsDeleted = true`, `DeletedAt` et `DeletedBy` renseignés.
- [ ] Modifier `UpdateTeacherCommandHandler` pour appeler `SoftDelete(actorId)` sur chaque association décochée et ne pas réinsérer une association déjà active.
- [ ] Utiliser la clé unique partielle `(TeacherId, SubjectId) WHERE NOT "IsDeleted"` afin qu’une association révoquée puisse être recréée sans rendre actif son ancien historique.
- [ ] Créer une migration qui révoque `DELETE` au rôle `sama_ecole_app` sur `teacher_subjects`. Ne pas modifier `20260830045159_GrantDeleteOnTeacherSubjects` : elle peut être appliquée en production.
- [ ] Mettre à jour les commentaires de configuration et le test existant : son résultat attendu devient « association active absente, tombstone conservé », non « ligne disparue ».
- [ ] Ajouter un test qui prouve que la révocation ne retire pas la matière des bulletins ou consultations historiques de la période où l’association était active.
- [ ] Ajouter un test sous le rôle applicatif qui vérifie que `DELETE` sur `teacher_subjects` est refusé par PostgreSQL.

**Commandes de vérification :**

```powershell
dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~TeacherSubjectUnassignmentTests|FullyQualifiedName~TeacherSubjectHistoryTests"
dotnet test tests/SamaEcole.IntegrationTests --filter "Category=MultiTenant&FullyQualifiedName~TeacherSubject"
```

Résultat attendu : toutes les associations révoquées restent interrogeables par le chemin historique tenant-scoped, sont exclues du chemin actif normal, et le rôle applicatif n’a plus le droit SQL de suppression physique.

## Tâche 3 — Standardiser la suppression et la restauration explicite

**Fichiers :**

- Modifier au besoin : `src/SamaEcole.Domain/Common/AuditableEntity.cs`.
- Ajouter les contrats de restauration et de corbeille dans les vertical slices de `src/SamaEcole.Application/` correspondant aux entités classées restaurables dans l’inventaire.
- Modifier les handlers de création correspondants (notamment `CreateBuildingCommandHandler`, `CreateClassroomCommandHandler`, `CreateFeeCategoryCommandHandler`, `CreateInventoryCategoryCommandHandler`, `CreateMentionCommandHandler`, `CreateRoomCommandHandler`, `CreateSchoolYearCommandHandler` et les handlers de terme/association listés par l’inventaire).
- Modifier : `src/SamaEcole.Web/Middleware/ExceptionHandlingMiddleware.cs` uniquement si le mapping existant de `BusinessRuleException` ne permet pas le code stable requis.
- Modifier les contrôleurs, vues Razor et scripts JavaScript des écrans concernés ; commencer par les parcours existants de `BuildingsController` / `Views/Buildings/Index.cshtml` / `wwwroot/js/buildings.js`, puis appliquer les mêmes contrats aux autres domaines concernés.
- Ajouter : tests d’intégration par domaine sous `tests/SamaEcole.IntegrationTests/` et tests d’endpoints sous `tests/SamaEcole.FunctionalTests/`.

- [ ] Écrire un test par type d’identité réutilisable : lorsqu’une ligne active porte l’identité, une création renvoie le conflit de doublon actif ; lorsqu’une ligne supprimée seule porte l’identité, la création renvoie un 409 avec `ARCHIVED_ENTITY_EXISTS` et n’insère aucune ligne.
- [ ] Ajouter à `AuditableEntity` une opération `Restore()` qui remet `IsDeleted` à `false` et `DeletedAt`/`DeletedBy` à `null`, ou utiliser un mécanisme partagé équivalent compatible avec les setters privés.
- [ ] Créer des opérations typées de consultation des éléments supprimés et de restauration, tenant-scoped, autorisées uniquement aux rôles de gestion déjà responsables de l’entité. Ne pas introduire de commande polymorphe recevant un nom de type arbitraire.
- [ ] Pour lire un tombstone, employer `IgnoreQueryFilters()` puis filtrer explicitement sur l’ID et `SchoolId == tenant courant` ; la RLS reste active. Refuser 404 pour un ID hors tenant sans révéler son existence.
- [ ] Vérifier les dépendances et l’index actif avant restauration. Si une autre ligne active utilise déjà la même identité, renvoyer `ACTIVE_ENTITY_CONFLICT` et ne modifier aucune des deux lignes.
- [ ] Faire rechercher les identités actives puis supprimées par chaque handler de création concerné. En cas de course concurrente, laisser l’index actif arbitrer et conserver la traduction SQL unique existante en 409.
- [ ] Ajouter sur l’écran concerné un message compréhensible « Cet élément existe dans les éléments supprimés » avec un lien/bouton de restauration ; après restauration réussie, rafraîchir les listes et sélecteurs actifs.
- [ ] Tester qu’une restauration par un rôle non autorisé est refusée et qu’un autre tenant ne peut ni lister ni restaurer le tombstone.

**Contrat d’erreur :** utiliser le mapping `BusinessRuleException(message, code)` existant, qui retourne déjà HTTP 409 dans `ExceptionHandlingMiddleware`. Codes attendus : `ARCHIVED_ENTITY_EXISTS` à la création et `ACTIVE_ENTITY_CONFLICT` à la restauration.

**Commandes de vérification :** tests ciblés des handlers et endpoints modifiés, puis `dotnet test tests/SamaEcole.IntegrationTests --filter "Category=MultiTenant"`.

## Tâche 4 — Convertir les index d’identité en index partiels

**Fichiers connus au démarrage, à confirmer contre l’inventaire :**

- Modifier : `BuildingConfiguration.cs`, `ClassFeeConfiguration.cs`, `ClassroomConfiguration.cs`, `FeeCategoryConfiguration.cs`, `GradeConfiguration.cs`, `InventoryCategoryConfiguration.cs`, `MentionConfiguration.cs`, `ReportCardRemarkConfiguration.cs`, `RoomConfiguration.cs`, `SchoolYearConfiguration.cs`, `TermConfiguration.cs`, `UserSchoolConfiguration.cs`.
- Relire sans conversion mécanique : tous les autres `*Configuration.cs` avec `.IsUnique()`, dont `PaymentConfiguration.cs`, `EnrollmentConfiguration.cs`, `StudentConfiguration.cs`, `StudentMutationCertificateConfiguration.cs`, `ExamDossierConfiguration.cs`, `SchoolConfiguration.cs`, `UserConfiguration.cs`, `TeacherConfiguration.cs`, `TeacherAssignmentConfiguration.cs`, `ClassSubjectConfiguration.cs`, `StudentSubjectEnrollmentConfiguration.cs` et les identifiants globaux.
- Créer : migration `HarmonizeSoftDeleteUniqueIndexes` après la dernière migration déjà suivie et après toute migration locale préexistante à préserver.
- Mettre à jour par génération EF : `src/SamaEcole.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`.
- Ajouter : `tests/SamaEcole.IntegrationTests/Persistence/SoftDeleteUniqueIndexTests.cs`.

- [ ] Avant la modification des configurations, ajouter des tests PostgreSQL montrant qu’un tombstone existant n’empêche pas l’index partiel de s’appliquer et qu’une seule ligne active par identité reste autorisée.
- [ ] Remplacer les index ayant `IsDeleted` dans leur clé par une clé métier sans `IsDeleted` et le filtre `"IsDeleted" = false`. Conserver les autres conditions de filtre qui expriment une règle métier.
- [ ] Ne pas convertir les index des reçus, paiements, transactions, matricules, codes de vérification ou autres identifiants historiquement permanents sans preuve explicite dans l’inventaire.
- [ ] Implémenter le précontrôle de doublons actifs par index avec SQL PostgreSQL lisible et noms de contraintes explicites. Une collision provoque un arrêt explicite et rapporte table/colonnes/identifiants ; le SQL ne supprime, fusionne ou réécrit aucune donnée.
- [ ] Générer la migration EF sans éditer les migrations historiques. Relire son `Up`, son `Down`, son designer et le snapshot ; vérifier que les index partiels portent la bonne portée tenant et le bon prédicat.
- [ ] Vérifier le scénario de retour arrière décrit dans la conception : après plusieurs tombstones, ne pas recréer une ancienne contrainte incompatible ; documenter un retour avant par migration corrective.
- [ ] Tester qu’une collision active de préproduction fait échouer le précontrôle, tandis que plusieurs anciennes lignes supprimées n’empêchent pas la migration.

**Commandes de vérification :**

```powershell
dotnet ef migrations has-pending-model-changes -p src/SamaEcole.Persistence -s src/SamaEcole.Web
dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SoftDeleteUniqueIndexTests"
```

Résultat attendu : aucune différence de modèle non migrée et tests validant les index réels sous PostgreSQL, pas seulement les métadonnées EF.

## Tâche 5 — Protéger les écritures immuables et corriger les parcours hors règle

**Fichiers :**

- Examiner : `src/SamaEcole.Persistence/Configurations/PaymentConfiguration.cs`, `StockMovementConfiguration.cs`, `AuditLogConfiguration.cs`, `EnrollmentConfiguration.cs` et leurs migrations de grants/RLS.
- Examiner : commandes de suppression et d’annulation dans `src/SamaEcole.Application/Finance/`, `Inventory/`, `Features/Disbursements/`, `Features/Schedules/` et `ClassJournal/`.
- Créer seulement les migrations additives nécessaires pour retirer les droits `DELETE` de tables métier immuables ou pour corriger les policies applicatives concernées.
- Ajouter les tests dans `tests/SamaEcole.IntegrationTests/Finance/`, `Inventory/` et `Common/` près des tests de caisse, paiement, ledger et RLS existants.

- [ ] Comparer les grants de toutes les tables classées immuables à l’inventaire et tester `has_table_privilege('sama_ecole_app', ..., 'DELETE')` comme rôle d’application.
- [ ] Ajouter les tests négatifs SQL pour empêcher une suppression physique de paiement, reçu/ligne de reçu, audit et mouvement de stock par le rôle applicatif.
- [ ] Vérifier que les corrections disponibles créent une annulation/contre-écriture référant l’opération d’origine et qu’aucun handler n’édite ou supprime cette écriture.
- [ ] Parcourir chaque commande utilisateur du registre de tâches : remplacer les opérations physiques sur entités métier modifiables par `SoftDelete(actorId)` ; bloquer explicitement les opérations interdites sur registres immuables.
- [ ] Vérifier le cas `DeleteDisbursementCommand` : si le décaissement est une écriture comptable confirmée, changer l’action en annulation compensatrice plutôt qu’en suppression logique ; si le domaine distingue un brouillon non comptabilisé, documenter et tester les deux états.
- [ ] Vérifier le cas `DeleteScheduleSlotCommand` et les tables techniques à rétention limitée pour s’assurer qu’aucune purge métier n’est confondue avec un nettoyage technique.
- [ ] Confirmer que les migrations existantes ne sont pas modifiées et que les nouveaux droits sont accordés/révoqués dans le bon sens lors d’une installation neuve comme d’une mise à niveau.

**Commandes de vérification :** tests d’intégration ciblés Finance/Inventory, tests de rôle applicatif et `dotnet test tests/SamaEcole.IntegrationTests --filter "Category=MultiTenant"`.

## Tâche 6 — Tester filtres, restauration et isolation de bout en bout

**Fichiers :**

- Ajouter/modifier les tests sous `tests/SamaEcole.IntegrationTests/` pour `Buildings`, `Classrooms`, `Finance`, `Grades`, `Inventory`, `Rooms`, `SchoolYears`, `Teachers` et les autres modules identifiés par l’inventaire.
- Ajouter/modifier les tests sous `tests/SamaEcole.FunctionalTests/` pour les routes de corbeille/restauration et les états UX de conflit.
- Relire : `tests/SamaEcole.IntegrationTests/Common/RlsTestDatabase.cs`, `tests/SamaEcole.IntegrationTests/Buildings/BuildingsIsolationTests.cs`, `Teachers/TeacherIsolationTests.cs`, `Finance/FeeIsolationTests.cs`, `Inventory/InventoryIsolationTests.cs`, `Grades/GradeCorrectionTests.cs`.

- [ ] Pour chaque agrégat restaurable, tester suppression, invisibilité dans listes/sélecteurs/export normaux, présence dans corbeille, restauration, puis visibilité normale.
- [ ] Tester que deux tenants peuvent avoir une même identité si l’unicité est tenant-scoped, et qu’un tenant ne peut pas inférer/restaurer la ligne supprimée de l’autre.
- [ ] Tester un scénario concurrent : deux créations de la même identité active n’aboutissent pas à deux lignes et la deuxième reçoit un 409 normalisé.
- [ ] Tester que restaurer un élément archivé avec une identité réutilisée par un élément actif retourne `ACTIVE_ENTITY_CONFLICT` sans mutation partielle.
- [ ] Tester les tables liées à des écritures immuables : la suppression logique du parent respecte la décision `bloquer` ou `désactiver` consignée dans l’inventaire et ne change aucune écriture historique.
- [ ] Exécuter les tests PostgreSQL complets et la catégorie `MultiTenant`, puis les tests fonctionnels pertinents.
- [ ] Vérifier chaque affirmation de la Definition of Done du dépôt, l’absence de secret/fichier `.env`, et que le diff ne contient que le périmètre PR1.

**Commandes de vérification avant revue PR :**

```powershell
dotnet build SamaEcole.sln --no-restore
dotnet test tests/SamaEcole.IntegrationTests --no-build --configuration Release
dotnet test tests/SamaEcole.IntegrationTests --filter "Category=MultiTenant"
dotnet test tests/SamaEcole.FunctionalTests
git diff --check
```

Le build doit terminer avec le code 0 sans nouveau warning. Chaque suite de tests doit finir avec zéro échec ; si les tests sont relancés dans un environnement PostgreSQL de CI, conserver le fichier TRX comme artefact de revue.

## Ordre de revue et sortie attendue

1. Revue et acceptation de `docs/architecture/soft-delete-index-inventory.md` avant de figer la liste finale des migrations.
2. Revue ciblée de la relation enseignant-matière et des protections des registres immuables.
3. Revue de la migration d’index et de son précontrôle avant son application en production.
4. Revue des contrats 409 et du parcours UX de restauration.
5. Revue finale du diff PR1, des résultats des suites de tests et des écarts documentés.

**Hors plan :** toute implémentation du chantier des frais optionnels appartient à la PR2 et ne doit pas être ajoutée à cette branche.
