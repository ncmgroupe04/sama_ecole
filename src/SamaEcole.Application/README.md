# SamaEcole.Application

Logique métier applicative. Dépend uniquement de `SamaEcole.Domain`. Aucune dépendance à EF Core, ASP.NET, ou tout framework d'infrastructure — seulement des interfaces.

- Un sous-dossier par module (`Students/`, `Finance/`, `Grades/`...), chacun contenant ses `Commands/` (écriture, MediatR) et `Queries/` (lecture, MediatR).
- `Common/Interfaces/` — contrats implémentés par `Infrastructure`/`Persistence` (ex. `IApplicationDbContext`, `ITenantProvider`, `ICurrentUserService`).
- `Common/Behaviors/` — pipeline MediatR transverse (validation FluentValidation, logging, gestion de la concurrence optimiste → 409).

Référence : docs/Volume_2_SDS.md, docs/BACKLOG_TICKETS.md.
