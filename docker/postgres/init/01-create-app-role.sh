#!/bin/bash
# Ticket JGK-A03 — rôle applicatif dédié, soumis à la Row-Level Security (AGENTS.md règle #2).
#
# POSTGRES_USER (sama_ecole) est PROPRIÉTAIRE de la base et des tables. PostgreSQL exempte
# systématiquement de ses policies RLS un superutilisateur, un rôle BYPASSRLS ET le propriétaire
# d'une table : y brancher l'application rendrait l'isolation multi-tenant silencieusement
# inopérante. Ce rôle-là est donc réservé aux MIGRATIONS (conteneur `migrate`, `dotnet ef`).
#
# L'application, elle, se connecte avec le rôle créé ici : ni superutilisateur, ni BYPASSRLS,
# propriétaire d'aucune table — donc pleinement soumis aux policies. `RlsGuard` refuse le démarrage
# si ce n'est pas le cas.
#
# Ce script est joué par l'image postgres UNIQUEMENT sur un volume vierge (initdb) — voir le montage
# `./docker/postgres/init` dans docker-compose.yml. Sur une base déjà initialisée :
# `docker compose down -v` pour la recréer, ou rejouer ce SQL à la main.
#
# En Staging/Production (PostgreSQL managé, pas de docker-entrypoint-initdb.d), cette création de
# rôle est une étape de provisionnement à exécuter UNE FOIS avant la première migration — voir
# docs/Volume_9_Deployment_Operations.md §3bis.
#
# Ce script NE POSE AUCUN GRANT sur les tables ni les séquences : c'est la migration
# EnableRowLevelSecurity, puis CHAQUE migration ultérieure, qui accordent au rôle applicatif
# exactement les droits voulus, table par table (SELECT, INSERT, UPDATE — jamais DELETE sur une
# table à donnée métier historisée, règle #6). Un `ALTER DEFAULT PRIVILEGES` global ici
# ré-accorderait en douce des droits — dont DELETE — sur toute table future, court-circuitant ce
# contrôle. On s'en abstient délibérément.
set -euo pipefail

APP_USER="${DB_APP_USER:-sama_ecole_app}"
APP_PASSWORD="${DB_APP_PASSWORD:-changeme_app}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v app_db="$POSTGRES_DB" <<-EOSQL
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$APP_USER') THEN
            EXECUTE format(
                'CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS',
                '$APP_USER', '$APP_PASSWORD');
        END IF;
    END
    \$\$;

    -- Se connecter à la base et voir le schéma : le strict nécessaire. Les droits sur les objets
    -- (tables, séquences, fonctions) sont posés par les migrations, jamais ici.
    GRANT CONNECT ON DATABASE :"app_db" TO "$APP_USER";
    GRANT USAGE ON SCHEMA public TO "$APP_USER";
EOSQL

echo "Rôle applicatif '$APP_USER' prêt (NOSUPERUSER, NOBYPASSRLS, propriétaire d'aucune table, aucun GRANT de table)."
