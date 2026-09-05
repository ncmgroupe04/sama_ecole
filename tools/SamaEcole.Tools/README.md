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

## `seed-superadmin`

Crée le tout premier compte Super Admin d'un déploiement — sans lui, aucun chemin n'existe pour en
créer un : `CreateUserCommandValidator` exclut délibérément ce rôle des rôles assignables via l'API
(anti-escalade de privilège), et `DbSeeder` (seul autre endroit qui sème ce rôle) est réservé à
Development, avec un mot de passe public codé en dur.

```bash
ConnectionStrings__Migrations="Host=…;Username=sama_ecole;Password=…" \
  dotnet run --project tools/SamaEcole.Tools -- seed-superadmin --email admin@exemple.sn --password "…"

# nom complet optionnel (par défaut "Super Admin"), chaîne de connexion explicite possible
dotnet run --project tools/SamaEcole.Tools -- seed-superadmin \
  --email admin@exemple.sn --password "…" --name "Prénom Nom" --connection "Host=…"
```

Même rôle **propriétaire** requis que `migrate`, pour la même raison : `users` est sous RLS, et les
fonctions SECURITY DEFINER du chemin de login (`provision_school_director` compris) n'ont aucune
variante Super Admin. Le mot de passe suit la même politique que toute création de compte humaine
(Volume_7 §2 — 8 caractères minimum, majuscule, minuscule, chiffre, caractère spécial) ; la commande
le rejette sinon, avant tout accès à la base.

Idempotente par e-mail : si le compte existe déjà, la commande ne fait rien et sort en `0` — jamais de
doublon, jamais un mot de passe déjà en usage écrasé par une relance accidentelle.

Sur Cloud Run, cette commande s'exécute par un Job dédié qui réutilise l'image de `migrate` avec des
arguments de conteneur différents (voir `docs/Volume_9_Deployment_Operations.md`, section « Premier
compte Super Admin »).
