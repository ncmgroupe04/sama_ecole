# Contribuer à Jangalekat

Ce guide s'adresse aussi bien aux développeurs humains qu'aux agents de code (Claude Code, Cursor, Antigravity). Pour les règles techniques détaillées, voir `AGENTS.md` — ce document couvre le processus de contribution.

## Avant de commencer

1. Lire `AGENTS.md` (racine) — règles non négociables.
2. Prendre **un seul** ticket de `docs/BACKLOG_TICKETS.md` à la fois, en respectant l'ordre de dépendances donné en fin de fichier.
3. Vérifier que le ticket n'est pas déjà couvert par une branche/PR existante.

## Branches

Git Flow, voir `docs/Volume_6_Dev_Guide.md` §9 :

- `main` — code en production, protégé, jamais de commit direct.
- `develop` — intégration continue des tickets terminés.
- `feature/JGK-<id>-<slug>` — une branche par ticket (ex. `feature/JGK-D01-creation-eleve`).
- `hotfix/<slug>` — correctif urgent sur `main`.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/) :

```
feat(students): génération du matricule à l'enregistrement (JGK-D01)
fix(finance): corrige le calcul du solde dû après annulation
test(multitenant): ajoute le test d'isolation RLS sur students
docs(volume-3): précise la policy RLS sur payments
```

## Definition of Done (rappel — détail complet `docs/Volume_6_Dev_Guide.md` §11)

Une PR n'est mergeable que si :

- [ ] Le ticket référencé dans le titre de la PR correspond exactement au scope du diff.
- [ ] `dotnet build` passe sans warning nouveau.
- [ ] `dotnet test` passe (unitaires + intégration).
- [ ] Si la PR touche une table `ITenantEntity` : le test `Category=MultiTenant` couvre la nouvelle table.
- [ ] Si la PR touche Notes/Paiements/Frais : le verrouillage optimiste est testé (cas de conflit `409`).
- [ ] Aucune règle non négociable d'`AGENTS.md` n'est contournée.
- [ ] Aucun secret, `.env`, ou clé n'est committé.
- [ ] La documentation impactée (`docs/Volume_X`) est mise à jour si le comportement change.

## Revue de code

- Une PR = un ticket. Pas de PR multi-tickets.
- Le relecteur vérifie en priorité les 10 règles non négociables d'`AGENTS.md` avant le style.
- Toute dérogation à une règle d'architecture doit être justifiée explicitement dans la description de la PR, jamais silencieuse.

## Questions fonctionnelles

En cas de doute sur une règle métier, se référer à `docs/Volume_1_Cahier_des_Charges.md` en premier. Si le cahier des charges ne tranche pas, documenter l'hypothèse prise dans la PR plutôt que de bloquer le développement.
