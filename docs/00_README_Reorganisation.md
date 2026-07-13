# JANGALEKAT — Documentation v2.0
## Index et note de réorganisation

**Date :** Juillet 2026
**Portée :** Ce jeu de fichiers remplace intégralement les Volumes 0 à 9 précédents.

---

## 1. Ce qui a changé, en une phrase

Jangalekat n'est plus une application Offline-First évoluant progressivement vers le SaaS. C'est désormais **une plateforme SaaS cloud, en ligne dès le premier jour, multi-tenant, sur un socle technique unique**. Le détail complet des arbitrages se trouve dans le **Journal des décisions au Volume 0, §0.13**.

## 2. Les trois problèmes corrigés

1. **Incohérence technique** : l'ancien cahier des charges mentionnait à un endroit SQLAlchemy (Python) et PostgreSQL en mode LAN, alors que le reste de la documentation imposait ASP.NET Core/EF Core et MySQL en LAN. **Tranché : ASP.NET Core 9 + Entity Framework Core + PostgreSQL uniquement, aucune exception.**
2. **Numérotation cassée** : plusieurs volumes étaient empilés dans un même fichier (Sécurité et Tests noyés dans le fichier "Volume 6", "Volume 9" dupliqué dans deux fichiers différents). **Tranché : un fichier = un volume**, ci-dessous.
3. **Paralysie par la planification** : le cahier des charges continuait de s'enrichir de fonctionnalités avant même le début du développement. **Tranché : un MVP gelé (Volume 1.5, Chapitre 7), tout le reste en backlog explicite.**

## 3. Fichiers du jeu de documentation

| Fichier | Contenu |
|---|---|
| `Volume_0_Vision_Architecture.md` | Vision produit, décisions d'architecture définitives, journal des décisions |
| `Volume_1_Cahier_des_Charges.md` | Cahier des charges fonctionnel consolidé (fusion des versions 1.1 à 1.4) |
| `Volume_1.5_PRD.md` | Product Requirements Document — utilisateurs, modules, MVP verrouillé, roadmap |
| `Volume_2_SDS.md` | Software Design Specification — architecture logicielle, contrats de module, workflows |
| `Volume_3_DDS.md` | Database Design Specification — schéma PostgreSQL, stratégie multi-tenant (RLS) |
| `Volume_4_API_Design.md` | Spécification des API REST |
| `Volume_5_UIUX_Design.md` | Spécification UI/UX — design system, écrans, ergonomie |
| `Volume_6_Dev_Guide.md` | Guide de développement — stack, conventions, CQRS, Git, Definition of Done |
| `Volume_7_Security.md` | Architecture de sécurité + Matrice complète des rôles et permissions |
| `Volume_8_Test_Strategy.md` | Stratégie de tests, test obligatoire d'isolation multi-tenant, RTM |
| `Volume_9_Deployment_Operations.md` | Déploiement cloud, CI/CD, sauvegardes, supervision, reprise après sinistre |

**Ordre de lecture recommandé :** dans l'ordre du tableau ci-dessus.

**Non bloquants (backlog documentaire, à écrire en parallèle du code) :** Volume 10 — Plan de continuité d'activité, Volume 11 — Manuels utilisateurs, Volume 12 — Guide technique administrateur, Volume 13 — Roadmap produit détaillée.

## 4. Décisions techniques clés (résumé — détail au Volume 0 §0.8 et §0.13)

| Domaine | Choix retenu |
|---|---|
| Backend | ASP.NET Core 9 (Web API + MVC), C# |
| Architecture | Clean Architecture |
| ORM | Entity Framework Core (Npgsql) — exclusivement |
| Base de données | **PostgreSQL uniquement**, un seul environnement (dev/staging/prod) |
| Multi-tenant | Base partagée + `SchoolId` + Row-Level Security PostgreSQL + Global Query Filter EF Core |
| Frontend | ASP.NET Core MVC + Razor (V1), API REST découplée dès le départ pour préparer une future app mobile |
| Authentification | ASP.NET Core Identity + JWT (access + refresh token) |
| Concurrence | Verrouillage optimiste (`RowVersion`/`xmin`) — remplace la notion de "conflits de synchronisation offline", devenue obsolète |
| Déploiement | Cloud uniquement dès la V1 : Docker, PostgreSQL managé, Nginx, CI/CD — **aucun mode local ni LAN** |
| Sauvegardes | Automatiques côté plateforme (infrastructure), plus de bouton "sauvegarde manuelle" côté école |

## 5. Contrepartie à garder en tête

Le passage 100 % en ligne simplifie radicalement l'architecture (un seul moteur de BDD, un seul environnement à opérer, plus de synchronisation à gérer) mais suppose une **connexion Internet disponible dans chaque établissement**. Le Volume 5 §9 et le Volume 1.5 §10 prévoient une résilience aux coupures courtes (conservation des saisies, nouvel envoi automatique), mais pas de fonctionnement hors ligne complet — c'est un choix produit assumé, pas un oubli technique.
