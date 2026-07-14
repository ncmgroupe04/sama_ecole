#!/bin/bash
# Ticket JGK-A03 — rôle applicatif dédié, soumis à la Row-Level Security.
#
# POSTGRES_USER (sama_ecole) est PROPRIÉTAIRE de la base et des tables. PostgreSQL exempte
# systématiquement un superutilisateur, un rôle BYPASSRLS et le propriétaire d'une table de ses
# policies RLS : y connecter l'application rendrait l'isolation multi-tenant silencieusement
# inopérante. On le réserve donc aux migrations.
#
# L'application, elle, se connecte avec ce rôle-ci : ni superutilisateur, ni BYPASSRLS, propriétaire
# d'aucune table — donc pleinement soumis aux policies.
#
# Ce script n'est exécuté par l'image postgres que sur un volume VIERGE (initdb). Sur une base déjà
# initialisée : `docker compose down -v` pour la recréer, ou jouer ce SQL à la main.
set -euo pipefail

APP_USER="${DB_APP_USER:-sama_ecole_app}"
APP_PASSWORD="${DB_APP_PASSWORD:-changeme_app}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v app_user="$APP_USER" \
     -v app_password="$APP_PASSWORD" \
     -v app_db="$POSTGRES_DB" \
     -v owner="$POSTGRES_USER" <<-'EOSQL'
    DO $$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user') THEN
            EXECUTE format(
                'CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS',
                :'app_user', :'app_password');
        END IF;
    END
    $$;

    GRANT CONNECT ON DATABASE :"app_db" TO :"app_user";
    GRANT USAGE ON SCHEMA public TO :"app_user";

    -- Les tables n'existent pas encore (les migrations tourneront plus tard) : ce default privilege
    -- garantit que celles créées ENSUITE par le propriétaire seront accessibles au rôle applicatif,
    -- sans avoir à repasser un GRANT après chaque migration.
    ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_user";
EOSQL

echo "Rôle applicatif '$APP_USER' prêt (NOSUPERUSER, NOBYPASSRLS, propriétaire d'aucune table)."