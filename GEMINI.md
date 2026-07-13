# GEMINI.md — Jangalekat (Antigravity)

Antigravity : le fichier de référence de ce projet est **`AGENTS.md`** à la racine du dépôt. Lis-le intégralement avant toute tâche — il contient la stack, les commandes, les règles non négociables et l'index des volumes de documentation dans `/docs`.

Si `AGENTS.md` et ce fichier semblent en désaccord sur un point, `AGENTS.md` fait foi ; ce fichier n'existe que pour être détecté nativement par Antigravity.

Rappel des trois règles les plus fréquemment violées par les agents sur ce type de projet (voir `AGENTS.md` pour la liste complète) :

1. PostgreSQL uniquement — jamais SQLite/MySQL, jamais de code multi-SGBD.
2. Toute requête sur une table métier doit être filtrée par `SchoolId` (RLS + Global Query Filter EF Core) — jamais l'un sans l'autre.
3. Aucun mode hors-ligne, aucune synchronisation multi-postes — obsolète, voir `docs/Volume_0_Vision_Architecture.md` §0.13.
