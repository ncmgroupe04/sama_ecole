## Ticket

<!-- Ex. JGK-D01 — lien vers docs/BACKLOG_TICKETS.md -->

## Description

<!-- Que fait cette PR, en 2-3 phrases -->

## Checklist (Definition of Done — voir CONTRIBUTING.md)

- [ ] Le scope du diff correspond exactement au ticket référencé ci-dessus
- [ ] `dotnet build` sans nouveau warning
- [ ] `dotnet test` passe (unitaires + intégration)
- [ ] Si nouvelle table `ITenantEntity` : test `Category=MultiTenant` ajouté et vert
- [ ] Si Notes/Paiements/Frais impactés : cas de conflit `409` testé
- [ ] Aucune règle d'`AGENTS.md` contournée sans justification explicite ci-dessous
- [ ] Aucun secret/`.env` committé
- [ ] Documentation (`docs/Volume_X`) mise à jour si le comportement change

## Dérogations éventuelles à une règle d'architecture

<!-- Laisser "Aucune" si non applicable — ne jamais laisser une dérogation implicite -->
