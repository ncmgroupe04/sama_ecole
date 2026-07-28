# SamaEcole.Tools

Scripts/outils internes. Pas de dépendance vers SamaEcole.Web.

## `migrate`

Applique les migrations EF Core en attente, puis rend la main.

```bash
ConnectionStrings__Migrations="Host=…;Username=sama_ecole;Password=…" \
  dotnet run --project tools/SamaEcole.Tools -- migrate

# ou, chaîne explicite
dotnet run --project tools/SamaEcole.Tools -- migrate --connection "Host=…"
```

Codes retour : `0` = succès (y compris « aucune migration en attente »), `1` = échec. **Le déploiement doit s'arrêter sur un code non nul** — démarrer le web derrière un schéma partiellement migré est le scénario que cette commande existe pour éviter.

### Pourquoi une commande séparée, et pas `MigrateAsync()` au démarrage de SamaEcole.Web

Les migrations exigent le rôle **propriétaire** (`sama_ecole`) : créer ou altérer une table et poser une policy RLS demandent des droits que le rôle applicatif (`sama_ecole_app`) n'a pas.

Or PostgreSQL **exempte le propriétaire d'une table de ses policies RLS**. Faire tourner l'application web avec ce rôle appliquerait bien les migrations… et désactiverait l'isolation multi-tenant *sans aucun message d'erreur* : un directeur verrait les élèves d'un autre établissement. C'est exactement ce que `RlsGuard` refuse au démarrage (AGENTS.md règle #2, ticket JGK-A03).

Deux processus distincts permettent d'injecter le secret propriétaire dans un conteneur qui vit quelques secondes, et jamais dans celui qui sert les requêtes.

### Place dans le déploiement

L'ordre de `docs/Volume_9_Deployment_Operations.md` §85-86 reste inchangé :

1. Sauvegarde automatique de la base de production
2. `migrate` — conteneur éphémère, rôle propriétaire
3. Démarrage de l'application web — rôle applicatif, `RlsGuard` actif

Cette commande n'assume pas l'étape 1 : elle applique des migrations, elle ne sauvegarde rien.
