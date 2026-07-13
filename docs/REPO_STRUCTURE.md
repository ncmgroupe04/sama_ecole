# Jangalekat — Structure du dépôt

Cette arborescence est déjà créée physiquement dans le dépôt (dossiers + README par projet). Un agent de code doit placer chaque fichier au bon endroit dès le départ — ne pas créer une structure parallèle.

```
jangalekat/
├── AGENTS.md                          # Fichier maître — à lire en premier
├── CLAUDE.md                          # Importe AGENTS.md (Claude Code)
├── GEMINI.md                          # Pointeur vers AGENTS.md (Antigravity)
├── openapi.yaml                       # Contrat API — source de vérité machine-lisible
├── docker-compose.yml                 # Postgres + Redis pour le développement
├── .env.example                       # Modèle de configuration (copier en .env)
├── .editorconfig
├── .gitignore
│
├── .cursor/
│   └── rules/                         # Règles Cursor scopées par type de fichier
│       ├── 000-core.mdc
│       ├── backend.mdc
│       ├── database.mdc
│       └── frontend.mdc
│
├── .github/
│   └── workflows/
│       └── ci.yml                     # Build, tests, isolation multi-tenant, image Docker
│
├── docs/                              # Les 12 volumes + artefacts complémentaires
│   ├── 00_README_Reorganisation.md
│   ├── Volume_0_Vision_Architecture.md
│   ├── Volume_1_Cahier_des_Charges.md
│   ├── Volume_1.5_PRD.md
│   ├── Volume_2_SDS.md
│   ├── Volume_3_DDS.md
│   ├── Volume_4_API_Design.md
│   ├── Volume_5_UIUX_Design.md
│   ├── Volume_6_Dev_Guide.md
│   ├── Volume_7_Security.md
│   ├── Volume_8_Test_Strategy.md
│   ├── Volume_9_Deployment_Operations.md
│   ├── ERD.md                         # Diagramme entité-association (Mermaid)
│   ├── seed-data.json                 # Jeu de données de test
│   ├── BACKLOG_TICKETS.md             # Tickets prêts à l'emploi
│   ├── REPO_STRUCTURE.md              # Ce fichier
│   └── design-references/             # Reçu, bulletin, dashboard — à reproduire à l'identique (README.md + images)
│
├── Jangalekat.sln                     # Solution — déjà initialisée, prête pour `dotnet restore`
├── CONTRIBUTING.md                    # Process de contribution (branches, commits, DoD)
│
├── src/
│   ├── Jangalekat.Domain/             # Entités, enums — zéro dépendance externe
│   │   ├── Entities/                  # School.cs, Student.cs (pattern de référence)
│   │   ├── Enums/                     # Role, EntityStatus, EnrollmentType, PaymentMethod...
│   │   └── Common/                    # AuditableEntity, ITenantEntity
│   ├── Jangalekat.Application/        # Commands/Queries MediatR, règles métier
│   │   ├── Common/Interfaces/         # IApplicationDbContext, ITenantProvider, IMatriculeGenerator...
│   │   ├── Common/Behaviors/          # ValidationBehavior (pipeline MediatR)
│   │   ├── Common/Exceptions/         # ValidationException (422), ConcurrencyConflictException (409)
│   │   └── Students/Commands/CreateStudent/   # Pattern de référence complet — à dupliquer pour chaque ticket
│   ├── Jangalekat.Infrastructure/      # Identity, email, PDF, résolution tenant
│   │   └── Multitenancy/              # TenantProvider, CurrentUserService (lecture des claims JWT)
│   ├── Jangalekat.Persistence/         # EF Core + PostgreSQL uniquement
│   │   ├── Configurations/            # SchoolConfiguration, StudentConfiguration
│   │   ├── Migrations/                # Vide intentionnellement — voir README.md du dossier
│   │   └── Seed/                      # DbSeeder (à compléter avec docs/seed-data.json)
│   └── Jangalekat.Web/                 # API + Razor — orchestration uniquement
│       ├── Controllers/               # StudentsController (pattern de référence)
│       ├── Middleware/                # ExceptionHandlingMiddleware (format d'erreur normalisé)
│       ├── Views/                     # Vues Razor (à créer au fil des tickets)
│       ├── Styles/input.css           # Source Tailwind CSS (Décision D-13) — jamais wwwroot/css/site.css directement
│       ├── tailwind.config.js         # Thème étendu = design system du Volume 5 (couleurs, typo, breakpoints)
│       ├── package.json               # Scripts build:css / watch:css
│       ├── Program.cs                 # JWT, DI des 3 couches, Swagger, pipeline, MVC + vues
│       └── Dockerfile                 # Build CSS (Node) puis build .NET, en deux étapes
│
└── tests/
    ├── Jangalekat.UnitTests/           # Ex. CreateStudentCommandValidatorTests
    ├── Jangalekat.IntegrationTests/     # Catégorie obligatoire : MultiTenant (StudentIsolationTests, squelette à compléter)
    └── Jangalekat.FunctionalTests/
```

## État du squelette

Tous les fichiers `.csproj`/`.sln` et les interfaces/contrats sont écrits et corrects. Un seul module métier complet sert de pattern de référence : **Students → CreateStudent** (Command, Handler, Validator, Controller, test unitaire, entité + configuration EF Core). Tout le reste du backlog (`docs/BACKLOG_TICKETS.md`) doit être développé en dupliquant fidèlement cette structure. Le test d'isolation multi-tenant (`StudentIsolationTests`) est un squelette marqué `Skip` — à implémenter dès le ticket JGK-A03.

**Non fait volontairement** : implémentation réelle de `IMatriculeGenerator`, migrations EF Core (nécessitent le SDK .NET + accès NuGet, absents de l'environnement de génération de cette documentation), Views Razor, Identity complet. C'est le travail des tickets eux-mêmes.

## Règle de dépendance (Clean Architecture)

```
Domain  ←  Application  ←  Infrastructure
                         ←  Persistence
                         ←  Web (orchestration finale, référence tout)
```

`Domain` ne référence jamais rien. `Application` ne référence que `Domain`. `Infrastructure` et `Persistence` implémentent les interfaces d'`Application`. `Web` assemble le tout via injection de dépendances. Aucune flèche ne remonte dans l'autre sens — un agent qui ajoute une référence dans ce sens doit s'arrêter et signaler le problème plutôt que de la créer.

Détail complet : `Volume_2_SDS.md` et `Volume_6_Dev_Guide.md`.
