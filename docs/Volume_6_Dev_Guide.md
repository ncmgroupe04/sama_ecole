# JANGALEKAT

# VOLUME 6 — Development Guide & Coding Standards (DGCS)

**Version :** 2.0
**Statut :** Constitution technique du projet — remplace la version 1.0
**Changements de cette version :** un seul moteur de base de données (PostgreSQL) — suppression du chapitre « migrations multi-moteurs » ; suppression du projet `Jangalekat.Desktop` de l'organisation des dossiers (aucune application desktop n'est prévue, voir Volume 0 v2.0) ; ajout d'un renvoi explicite vers le Volume 3 §2 pour tout code touchant à l'isolation multi-tenant.

---

## Table des matières

1. Vision du développement et stack officielle
2. Architecture des projets (solution .NET)
3. Conventions de nommage
4. Organisation des dossiers par module
5. CQRS
6. Gestion des erreurs
7. Validation
8. Gestion des migrations de base de données
9. Gestion des branches Git
10. Standards de qualité
11. Définition de terminé (Definition of Done)

---

## 1. Vision du développement et stack officielle

Le développement de Jangalekat respecte : architecture modulaire, code maintenable, forte séparation des responsabilités, sécurité par défaut, évolutivité, performances élevées. Jangalekat n'est jamais développé comme une simple application CRUD — chaque module encapsule de vraies règles métier (Volume 2).

### Stack technologique officielle (définitive)

| Composant | Choix |
|---|---|
| Backend | ASP.NET Core 9 (Web API + MVC) |
| Langage | C# 13 |
| ORM | Entity Framework Core (Npgsql) |
| Base de données | **PostgreSQL — moteur unique** (Volume 0 §0.8) |
| Authentification | ASP.NET Core Identity + JWT |
| Documentation API | Swagger / OpenAPI |
| Journalisation | Serilog |
| Mapping | AutoMapper |
| Validation | FluentValidation |
| Médiateur applicatif | MediatR (support du pattern CQRS) |
| Tests | xUnit, FluentAssertions, Moq |
| Génération PDF | QuestPDF |
| Excel | ClosedXML |
| QR Code (reçus, fiches) | QRCoder |
| Cache | Redis (dès la V1 — plus de distinction « MemoryCache local / Redis SaaS » puisqu'il n'y a plus de version locale) |
| Tâches asynchrones | Hangfire (imports de masse, envois d'email, génération de rapports volumineux) |

---

## 2. Architecture des projets (solution .NET)

```
src/
├── Jangalekat.Domain            → Entités, Value Objects, énumérations, interfaces, événements métier (aucune dépendance externe)
├── Jangalekat.Application       → Use Cases (CQRS/MediatR), DTO, Validators, règles métier applicatives
├── Jangalekat.Infrastructure    → Email, PDF, Excel, notifications, QR Code, chiffrement, intégrations externes
├── Jangalekat.Persistence       → DbContext, configurations EF Core, migrations, seeders, repositories
├── Jangalekat.Web               → Controllers API + Controllers MVC/Razor, middlewares, Swagger, authentification
tests/
├── Jangalekat.UnitTests
├── Jangalekat.IntegrationTests
└── Jangalekat.FunctionalTests
tools/
└── Jangalekat.Tools             → scripts d'import, outils de maintenance
```

> Le projet `Jangalekat.Desktop` présent dans une version antérieure de ce document est supprimé : il présupposait une application installée localement, incompatible avec le pivot cloud (Volume 0 v2.0). `Jangalekat.Web` héberge à la fois l'API REST et les vues Razor du back-office — un seul déploiement, pas deux binaires à maintenir.

### Rôle de chaque projet

- **Domain** : entités, value objects, énumérations, interfaces, événements métier — aucune dépendance externe.
- **Application** : cas d'utilisation (CQRS via MediatR), DTO, validators, règles métier.
- **Infrastructure** : services techniques (email, PDF, Excel, notifications, QR code, chiffrement).
- **Persistence** : `DbContext`, configurations EF Core (`IEntityTypeConfiguration`), migrations, seeders, repositories. C'est ici, et uniquement ici, que vivent le Global Query Filter multi-tenant et la configuration RLS (Volume 3 §2).
- **Web** : contrôleurs, middlewares, Swagger, authentification, vues Razor.

---

## 3. Conventions de nommage

| Élément | Convention | Exemple |
|---|---|---|
| Entités (C#) | Singulier | `Student`, `Teacher`, `Payment`, `ReportCard` |
| Tables SQL | Pluriel | `Students`, `Teachers`, `Payments` |
| Clé primaire | Toujours `Id` (jamais `StudentId` dans la table `Students` elle-même) | — |
| Clé étrangère | `{Entité}Id` | `StudentId`, `SchoolId` |
| Services | Suffixe `Service` | `StudentService`, `FinanceService` |
| Repositories | Suffixe `Repository`, interface préfixée `I` | `IStudentRepository` |

---

## 4. Organisation des dossiers par module

Chaque module métier suit strictement la même structure, par exemple pour `Students` :

```
Students/
├── Commands/
├── Queries/
├── DTOs/
├── Validators/
├── Mappings/
├── Events/
├── Rules/
├── Controllers/
└── Views/
```

---

## 5. CQRS

Toutes les opérations métier passent par CQRS via MediatR : les lectures utilisent des **Queries**, les écritures des **Commands**. Aucun service ne mélange lecture et écriture.

Exemples : `CreateStudentCommand`, `UpdateStudentCommand`, `RegisterPaymentCommand`, `GenerateReportCardCommand`.

---

## 6. Gestion des erreurs

Aucune exception brute n'est renvoyée au client. Un middleware global intercepte les exceptions et les transforme en réponses normalisées (Volume 4 §0.4). Les journaux détaillés sont enregistrés via Serilog (Volume 7 §8).

---

## 7. Validation

Toutes les validations passent par **FluentValidation**. Les contrôleurs ne contiennent aucune logique de validation. Les règles métier complexes (ex. contrôle de places disponibles, cohérence des montants Inscription/Finance) sont isolées dans des classes de règles dédiées (`Rules/`), testables indépendamment.

---

## 8. Gestion des migrations de base de données

Un seul moteur, un seul environnement de référence : **PostgreSQL**, du poste de développement à la production (Volume 0 §0.8, Volume 3 §8). Toutes les migrations sont gérées via `dotnet ef migrations`. Aucune requête SQL brute spécifique à un moteur n'est nécessaire, ce qui simplifie considérablement ce chapitre par rapport à la version 1.0 de ce document, qui devait gérer trois moteurs différents.

---

## 9. Gestion des branches Git

Stratégie officielle : **Git Flow**.

| Branche | Rôle |
|---|---|
| `main` | Production |
| `develop` | Intégration continue |
| `feature/*` | Nouvelle fonctionnalité |
| `release/*` | Préparation d'une version |
| `hotfix/*` | Correction urgente en production |

---

## 10. Standards de qualité

Aucun code n'est fusionné dans `develop` sans respecter : architecture conforme (Volume 2), tests unitaires associés, validation métier, gestion des erreurs, journalisation, respect des permissions (Volume 7), performances vérifiées.

---

## 11. Définition de terminé (Definition of Done)

Une fonctionnalité est terminée uniquement si :

- le code compile sans erreur ;
- les tests unitaires et d'intégration passent (Volume 8) ;
- les permissions sont respectées (Volume 7) ;
- l'interface est conforme au Volume 5 ;
- l'API est conforme au Volume 4 ;
- le schéma de données est conforme au Volume 3, **y compris le test d'isolation multi-tenant** (Volume 3 §2.4) ;
- la documentation (RTM, Volume 8) est mise à jour.

**Fin du Volume 6.**
