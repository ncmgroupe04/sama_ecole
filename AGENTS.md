# AGENTS.md — Sama Ecole

Plateforme SaaS de gestion scolaire (Sénégal). Backend ASP.NET Core 9 (C#), EF Core, PostgreSQL uniquement. Frontend ASP.NET Core MVC + Razor, API REST découplée, **Tailwind CSS** pour le style (Décision D-13). Multi-tenant par `SchoolId` + Row-Level Security PostgreSQL. 100 % en ligne — aucun mode local/LAN/offline.

**Documentation complète :** `/docs`. Ce fichier ne remplace pas les volumes, il indique quand aller les lire. Ne pas dupliquer leur contenu dans le code de review ; y renvoyer.

## Commandes

```bash
dotnet restore
dotnet build
dotnet test                                    # tous les tests
dotnet test --filter Category=MultiTenant      # test critique d'isolation (docs/Volume_8_Test_Strategy.md §5)
dotnet ef migrations add <Nom> -p src/SamaEcole.Persistence -s src/SamaEcole.Web
dotnet ef database update -p src/SamaEcole.Persistence -s src/SamaEcole.Web
docker compose up -d                           # Postgres + Redis en local (laisse le port 5000 libre)
docker compose --profile app up -d             # idem + l'api conteneurisée sur le port 5000
dotnet run --project src/SamaEcole.Web         # lance l'app (pas de `dotnet run` nu : aucun projet à la racine)
npm install --prefix src/SamaEcole.Web        # dépendances Tailwind CSS
npm run build:css --prefix src/SamaEcole.Web  # compile wwwroot/css/site.css depuis Tailwind
npm run watch:css --prefix src/SamaEcole.Web  # recompile en continu pendant le développement
```

## Règles non négociables (ne jamais réinventer, ne jamais contourner)

1. **Un seul moteur de BDD : PostgreSQL.** Jamais de SQLite, MySQL, ou code conditionnel multi-SGBD. → `docs/Volume_0_Vision_Architecture.md` §0.8.
2. **Toute table métier porte un `SchoolId`** et est protégée par une policy RLS PostgreSQL **+** un Global Query Filter EF Core — les deux, jamais un seul. → `docs/Volume_3_DDS.md` §2.
   - **Deux rôles PostgreSQL, jamais un seul** : l'application tourne avec `sama_ecole_app` (`NOSUPERUSER`, `NOBYPASSRLS`, propriétaire d'aucune table) ; le rôle propriétaire `sama_ecole` est réservé aux **migrations**. PostgreSQL exempte de RLS un superutilisateur, un rôle `BYPASSRLS` **et le propriétaire d'une table** : brancher l'application sur le propriétaire désactive l'isolation *sans aucun message d'erreur*. `RlsGuard` refuse le démarrage dans ce cas — ne le contournez pas.
   - Toute nouvelle table tenant doit être ajoutée à `TenantTables` dans la migration RLS, sinon elle n'est protégée que par le filtre EF Core.
   - **Un cache applicatif (Redis, prévu dès la V1) n'est pas couvert par la RLS.** Toute clé de cache passe par `ITenantCacheKeyFactory.BuildKey` (jamais une interpolation manuelle), qui embarque systématiquement le `SchoolId` du tenant courant. → `docs/Volume_3_DDS.md` §2.5.
3. **Le matricule (élève/enseignant) est généré uniquement dans la transaction d'enregistrement**, jamais à l'ouverture du formulaire. → `docs/Volume_1_Cahier_des_Charges.md` §2.1.
4. **Le service Finance ne modifie jamais directement un montant issu d'une inscription.** Toute correction passe par Secrétariat/Admin et est historisée. → `docs/Volume_1_Cahier_des_Charges.md` §7.2.
5. **Concurrence : verrouillage optimiste (`RowVersion`/`xmin`)** sur les tables sensibles (Notes, Paiements, Frais). Conflit → `409 Conflict`, jamais un écrasement silencieux.
6. **Aucune suppression physique** de donnée métier. Toujours soft delete (`IsDeleted`, `DeletedAt`, `DeletedBy`).
7. **CQRS via MediatR** : Commands pour l'écriture, Queries pour la lecture. Aucun service ne mélange les deux. → `docs/Volume_6_Dev_Guide.md` §5.
8. **Aucune logique métier dans un contrôleur ni dans une entité EF Core.** Elle vit dans `SamaEcole.Application`.
9. **Toute erreur API suit le format normalisé** de `docs/Volume_4_API_Design.md` §0.4 — jamais une exception brute renvoyée au client.
10. **JWT contient `sub`, `schoolId`, `role`.** Le `schoolId` ne se lit jamais depuis un paramètre de requête modifiable par le client.
11. **Un paiement d'abonnement n'est confirmé que par un webhook signé (HMAC) de l'agrégateur** (PayDunya/CinetPay). Aucune route accessible au client ne positionne `SubscriptionPayments.Status = Confirmed`. Tant que `Subscriptions.Status = AwaitingPayment`, l'accès est restreint au strict paiement — voir `docs/Volume_7_Security.md` §12bis, ticket JGK-I04.
12. **Le reçu d'inscription, le bulletin de notes et le gabarit de dashboard suivent EXACTEMENT `docs/design-references/`** (images + description détaillée dans `docs/design-references/README.md`) — reproduction fidèle, pas d'interprétation créative. Le reçu inclut obligatoirement la mention : *"Il est demandé aux parents de garder minutieusement leur reçu après le paiement."*

## Ne jamais faire

- Ajouter un provider EF Core autre que Npgsql.
- Créer un mode "hors ligne", une queue de synchronisation, ou une résolution de conflits multi-postes (obsolète, voir Journal des décisions `docs/Volume_0_Vision_Architecture.md` §0.13).
- Modifier une migration déjà appliquée en production — toujours en créer une nouvelle.
- Committer `.env`, des clés, ou des secrets. Utiliser `.env.example` comme modèle.
- Générer du code sans test associé pour les modules Finance, Notes, et tout ce qui touche à l'isolation multi-tenant.

## Conventions

- Commits : Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `test:`).
- Branches : Git Flow — `main`, `develop`, `feature/*`, `release/*`, `hotfix/*`. → `docs/Volume_6_Dev_Guide.md` §9.
- Nommage : voir `docs/Volume_3_DDS.md` §1.4 (BDD) et `docs/Volume_6_Dev_Guide.md` §3 (code).
- Une fonctionnalité n'est terminée que si elle respecte la Definition of Done : `docs/Volume_6_Dev_Guide.md` §11 et `CONTRIBUTING.md`.
- Processus de contribution complet (branches, commits, checklist de PR) : `CONTRIBUTING.md`.

## Squelette de code déjà en place

La solution `.NET` (`SamaEcole.sln`, 5 projets `src/`, 3 projets `tests/`, 1 projet `tools/`) est déjà initialisée et compile une fois `dotnet restore` exécuté avec accès à NuGet. Le module `Students` (`CreateStudentCommand` + Handler + Validator + `StudentsController`) sert de **pattern de référence** à suivre pour tous les tickets de `docs/BACKLOG_TICKETS.md` : même structure Command/Handler/Validator, même gestion du tenant via `ITenantProvider`, même génération de matricule dans le Handler.

## Périmètre V1 (état au 27/07/2026)

État détaillé : **`ACTIVE_CONTEXT.md`** à la racine. En résumé :

- **Hors périmètre V1 — reporté en V3 :** le module « Portails Parents et Élèves & Messagerie »
  (`docs/Volume_1_Cahier_des_Charges.md` §13, sous-sections 13.1 à 13.6 — encadré de report en tête
  du chapitre, justification au `docs/Volume_1.5_PRD.md` §8.1). Ne pas ajouter les rôles
  `Parent`/`Eleve`, ni d'endpoint de consultation ouvert à un tiers non-personnel de l'établissement.
  La communication vers les parents en V1 est **sortante uniquement** : SMS et WhatsApp (formule
  Premium, `Feature.SmsNotifications`), e-mail, et documents remis en main propre. Une convocation de
  parent (§18) n'est pas un portail.
- **Hors périmètre V1 — acté pour la version suivante :** l'export **global** « Exporter mes données »
  du Directeur (`GET /api/v1/exports/school-data`, jamais implémenté). Ne pas le construire sans
  arbitrage. Les exports **par domaine** existent et couvrent les besoins réels : rapport financier
  `.xlsx`, export des présences, import/export Excel des notes, PDF officiels. → `docs/Volume_4_API_Design.md` §11.
- **Livrés et intégrés :** Paie, Trésorerie, Caisse, TVA/Fiscalité, Discipline & Convocations,
  Infrastructures (Bâtiments & Salles), Documents administratifs (8 PDF), Emploi du temps &
  Pointage enseignants, et Rapports financiers (écran `/rapports/financiers` + export `.xlsx`).
  Spécification fonctionnelle : `docs/Volume_1_Cahier_des_Charges.md` §14 à §21. Routes :
  `docs/Volume_4_API_Design.md` §12 à §19.

## Où trouver quoi (ne pas tout lire à chaque tâche — ouvrir le volume pertinent)

| Besoin | Volume |
|---|---|
| Règle métier / exigence fonctionnelle | `Volume_1_Cahier_des_Charges.md`, `Volume_1.5_PRD.md` |
| Architecture logicielle, contrat de module, workflow | `Volume_2_SDS.md` |
| Schéma de données, table précise, RLS | `Volume_3_DDS.md` |
| Route API, format de requête/réponse | `Volume_4_API_Design.md` (voir aussi `openapi.yaml` à la racine) |
| Écran, composant UI, design system | `Volume_5_UIUX_Design.md` |
| Convention de code, structure de projet | `Volume_6_Dev_Guide.md` |
| Permission, rôle, sécurité | `Volume_7_Security.md` |
| Cas de test, critère de qualité | `Volume_8_Test_Strategy.md` |
| Déploiement, CI/CD, supervision | `Volume_9_Deployment_Operations.md` |
| Ticket de développement prêt à l'emploi | `docs/BACKLOG_TICKETS.md` |
| Ce qui est livré / ce qui est hors périmètre V1 | `ACTIVE_CONTEXT.md` (racine) |
| Schéma entité-relation visuel | `docs/ERD.md` |
| Données de test | `docs/seed-data.json` |
| **Reçu, bulletin, dashboard — design à reproduire à l'identique** | `docs/design-references/` — voir règle ci-dessous |

## Note pour les outils multi-agents

Ce fichier est la source unique. `CLAUDE.md` l'importe (`@AGENTS.md`). `GEMINI.md` (Antigravity) y renvoie. Cursor le reflète dans `.cursor/rules/`. Si une règle change, modifier uniquement ce fichier.
