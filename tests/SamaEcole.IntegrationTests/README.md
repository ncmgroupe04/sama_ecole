# SamaEcole.IntegrationTests

Tests contre une vraie instance PostgreSQL (via Testcontainers ou la base `sama_ecole_test` du docker-compose).

**Catégorie obligatoire `MultiTenant`** : voir docs/Volume_8_Test_Strategy.md §5. Ces tests doivent prouver qu'aucune requête, avec ou sans oubli du filtre côté C#, ne peut retourner une donnée d'une autre école (la RLS PostgreSQL doit bloquer même en cas d'erreur applicative).
