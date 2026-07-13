# JANGALEKAT — Dépôt complet pour IA codeuses

Ce package contient **tout ce qu'il faut** pour démarrer le développement de Jangalekat avec Claude Code, Cursor ou Antigravity, en plus des 12 volumes de documentation fonctionnelle/technique déjà produits.

## Ce qui a été ajouté et pourquoi

| Fichier / dossier | Rôle | Comble quel manque |
|---|---|---|
| `AGENTS.md` | Instructions maîtresses, courtes et actionnables, pour tout agent de code | Les 12 volumes expliquent le *quoi*/*pourquoi* mais aucun n'était conçu pour être lu en 2 minutes par un agent avant chaque tâche |
| `CLAUDE.md` | Raccorde Claude Code à `AGENTS.md` | Claude Code lit nativement `CLAUDE.md`, pas `AGENTS.md` |
| `GEMINI.md` | Raccorde Antigravity à `AGENTS.md` | Idem pour Antigravity |
| `.cursor/rules/*.mdc` | Mêmes règles, scopées par type de fichier pour Cursor | Cursor applique des règles différentes selon le fichier ouvert |
| `openapi.yaml` | Contrat API machine-lisible complet | Le Volume 4 décrit les routes en prose/tableaux — un agent (et un générateur de code) travaille bien mieux à partir d'un contrat structuré |
| `docs/ERD.md` | Diagramme entité-association visuel (Mermaid) | Le Volume 3 mentionnait qu'un ERD devait exister, sans le fournir |
| `docs/seed-data.json` | Données de test réalistes, deux écoles distinctes | Nécessaire pour tester concrètement l'isolation multi-tenant et faire tourner l'application dès le premier jour |
| `docs/BACKLOG_TICKETS.md` | ~25 tickets prêts à l'emploi, dépendances explicites | Le PRD donne des user stories, pas des tickets unitaires exploitables un par un par un agent |
| `docs/REPO_STRUCTURE.md` + arborescence `src/`/`tests/`/`tools/` réelle | Squelette de dépôt Clean Architecture déjà en place, avec un README par projet | Un agent qui part d'une page blanche invente sa propre structure ; un squelette fixe évite les divergences |
| `docker-compose.yml` | PostgreSQL + Redis prêts pour le développement local | Le Volume 9 décrit l'infra cible en prose, pas en fichier exécutable |
| `.env.example` | Modèle de toutes les variables de configuration | Évite qu'un agent invente des noms de variables incohérents d'une session à l'autre |
| `.editorconfig` | Conventions de nommage C# automatisées | Le Volume 6 donne les règles en prose ; un `.editorconfig` les impose sans consommer de contexte à chaque session |
| `.gitignore` | Exclusions standards .NET/secrets | Évite qu'un agent committe `.env` ou des `bin/`/`obj/` |
| `.github/workflows/ci.yml` | Pipeline CI réel (build, tests, test d'isolation multi-tenant obligatoire, image Docker) | Le Volume 9 décrit le processus en prose, pas en YAML exécutable |

## Mise à jour — squelette de code réel + process de contribution

En complément du tableau ci-dessus, ont été ajoutés :

| Fichier / dossier | Rôle | Comble quel manque |
|---|---|---|
| `Jangalekat.sln` + 9 `.csproj` réels | Solution .NET complète (5 projets `src`, 3 `tests`, 1 `tools`), références entre couches correctes | Un agent qui part d'une page blanche invente sa propre structure de projets ; celle-ci est déjà tranchée et cohérente avec `Volume_2_SDS.md` |
| Module `Students/CreateStudent` complet (`Domain` + `Application` + `Persistence` + `Web` + test unitaire) | Pattern de référence de bout en bout | Sans exemple concret, chaque agent réinvente sa propre façon d'implémenter Command/Handler/Validator/Controller — source d'incohérence entre tickets |
| `ApplicationDbContext` avec Global Query Filter générique | Applique automatiquement `SchoolId + IsDeleted` à toute entité `ITenantEntity` | Évite qu'un agent oublie le filtre sur une nouvelle table — un seul endroit à maintenir |
| `ExceptionHandlingMiddleware` | Traduit toute exception en réponse HTTP normalisée (`Volume_4_API_Design.md` §0.4) | Sans lui, chaque contrôleur gérerait ses erreurs différemment |
| `Dockerfile` (`Jangalekat.Web`) | Build réel de l'image, référencé par `docker-compose.yml` et `ci.yml` | Ces deux fichiers le référençaient déjà sans qu'il existe |
| `CONTRIBUTING.md` + `.github/PULL_REQUEST_TEMPLATE.md` | Process de contribution, checklist de Definition of Done par PR | Manquait pour cadrer le travail des agents PR par PR, au-delà des règles techniques d'`AGENTS.md` |

**Limite assumée** : le SDK .NET et l'accès à NuGet ne sont pas disponibles dans l'environnement qui a généré ce package — les `.csproj`/`.sln` sont donc écrits à la main (format standard, non généré par `dotnet new`) et n'ont pas été compilés ici. Première étape recommandée chez toi : `dotnet restore && dotnet build` pour confirmer que tout compile, avant de lancer le premier ticket.

## Ce qui reste volontairement en dehors de ce package

- **Volumes 10 à 13** (Plan de continuité, Manuels utilisateurs, Guide admin, Roadmap détaillée) : utiles à l'équipe humaine et à l'exploitation, mais sans valeur pour un agent qui écrit du code — les lui donner diluerait son contexte utile.
- **Maquettes visuelles réelles (Figma)** : le Volume 5 UI/UX reste en prose/tableaux. Utile pour affiner le frontend plus tard, non bloquant pour démarrer le backend et une UI fonctionnelle basique.
- **Code métier réel** (Handlers MediatR, entités EF Core, vues Razor) : volontairement non généré ici. C'est le travail que `docs/BACKLOG_TICKETS.md` découpe pour être confié, ticket par ticket, aux agents de code eux-mêmes — c'est tout l'intérêt de leur fournir cette documentation plutôt que d'écrire le code à leur place.

## Comment démarrer concrètement

1. Initialiser un dépôt Git à partir de ce dossier (`git init && git add . && git commit -m "chore: documentation complète + squelette de solution .NET"`).
2. `dotnet restore && dotnet build` pour vérifier que la solution compile chez toi (SDK .NET 9 requis).
3. `cp .env.example .env` et renseigner les valeurs locales.
4. `docker compose up -d` pour lancer PostgreSQL + Redis (+ l'API si tu veux tester le conteneur).
5. Ouvrir le dépôt dans Claude Code, Cursor ou Antigravity — l'outil détectera automatiquement `CLAUDE.md`/`.cursor/rules`/`GEMINI.md`, qui renvoient tous vers `AGENTS.md`.
6. `JGK-A01` est déjà largement couvert par le squelette fourni (solution + `ApplicationDbContext` + DI) — le premier ticket réellement à donner à un agent est **`JGK-A02`** (première migration EF Core) ou directement **`JGK-A03`** (RLS + test d'isolation) si tu préfères valider le multi-tenant en priorité.
7. Avancer ticket par ticket en respectant l'ordre de dépendances donné en fin de backlog, et la checklist de `CONTRIBUTING.md` pour chaque PR.
