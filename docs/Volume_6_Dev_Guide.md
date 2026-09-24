# SAMA ECOLE

# VOLUME 6 — Development Guide & Coding Standards (DGCS)

**Version :** 2.0
**Statut :** Constitution technique du projet — remplace la version 1.0
**Changements de cette version :** un seul moteur de base de données (PostgreSQL) — suppression du chapitre « migrations multi-moteurs » ; suppression du projet `Sama Ecole.Desktop` de l'organisation des dossiers (aucune application desktop n'est prévue, voir Volume 0 v2.0) ; ajout d'un renvoi explicite vers le Volume 3 §2 pour tout code touchant à l'isolation multi-tenant.

---

## Table des matières

1. Vision du développement et stack officielle
2. Architecture des projets (solution .NET)
3. Conventions de nommage
4. Organisation des dossiers par module
5. CQRS
6. Gestion des erreurs
7. Validation
8. Frontend — composants JavaScript partagés
9. Gestion des migrations de base de données
10. Gestion des branches Git
11. Standards de qualité
12. Définition de terminé (Definition of Done)

---

## 1. Vision du développement et stack officielle

Le développement de Sama Ecole respecte : architecture modulaire, code maintenable, forte séparation des responsabilités, sécurité par défaut, évolutivité, performances élevées. Sama Ecole n'est jamais développé comme une simple application CRUD — chaque module encapsule de vraies règles métier (Volume 2).

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
├── SamaEcole.Domain            → Entités, Value Objects, énumérations, interfaces, événements métier (aucune dépendance externe)
├── SamaEcole.Application       → Use Cases (CQRS/MediatR), DTO, Validators, règles métier applicatives
├── SamaEcole.Infrastructure    → Email, PDF, Excel, notifications, QR Code, chiffrement, intégrations externes
├── SamaEcole.Persistence       → DbContext, configurations EF Core, migrations, seeders, repositories
├── SamaEcole.Web               → Controllers API + Controllers MVC/Razor, middlewares, Swagger, authentification
tests/
├── SamaEcole.UnitTests
├── SamaEcole.IntegrationTests
└── SamaEcole.FunctionalTests
tools/
└── SamaEcole.Tools             → scripts d'import, outils de maintenance
```

> Le projet `Sama Ecole.Desktop` présent dans une version antérieure de ce document est supprimé : il présupposait une application installée localement, incompatible avec le pivot cloud (Volume 0 v2.0). `SamaEcole.Web` héberge à la fois l'API REST et les vues Razor du back-office — un seul déploiement, pas deux binaires à maintenir.

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

## 8. Frontend — composants JavaScript partagés

Une logique d'interface consommée à l'identique par plusieurs écrans (modale, moteur de rendu, gestion d'état complexe) est factorisée en **un seul composant JS partagé** sous `wwwroot/js/`, jamais recopiée dans chaque vue. Le composant expose une fabrique d'état (`window.<nom>.state()`) que chaque composant Alpine étale dans le sien :

```js
Alpine.data('students', () => ({
    ...window.pdfPreview.state(),
    openReceipt(id) { this.openPdfPreview(`/api/v1/.../${id}/pdf`, 'Reçu', 'Recu.pdf'); }
}));
```

La vue inclut le balisage correspondant une seule fois via une partial partagée (ex. `@await Html.PartialAsync("_PdfPreviewModal")`), jamais dupliquée par écran.

### Exemple de référence : `pdf-preview.js`

Moteur unique de toutes les modales de prévisualisation PDF (reçus, bulletins, attestations, billets, sommations…), consommé par une quinzaine d'écrans (Dashboard, Caisse, Inscriptions, Élèves, Enseignants, Examens, Paie, Discipline, Inventaire, etc.). Partial associée : `Views/Shared/_PdfPreviewModal.cshtml`.

- **Rendu par `<canvas>` via PDF.js, jamais par `<iframe src="blob:…">`.** L'approche iframe, utilisée initialement, échoue de façon silencieuse et non reproductible selon le navigateur (extension bloquant le viewer PDF interne, mode « télécharger au lieu d'afficher », absence de plugin PDF sur mobile) : l'écran restait blanc sans que l'erreur réelle (jeton expiré, 404, document vide) ne remonte. Un composant Alpine qui a besoin d'un aperçu PDF doit passer par ce moteur, pas réimplémenter un montage iframe.
- **Chargement à la demande** : PDF.js (`~1,8 Mo` module + worker) n'est importé dynamiquement qu'au premier aperçu déclenché, pour ne pas alourdir chaque page sur une connexion mobile (Volume 5 §1).
- **Auto-hébergé** (`wwwroot/js/vendor/`), jamais un CDN : la CSP (`script-src 'self'`, ticket JGK-F01) l'exige.
- **Repli sans PDF.js** : si le module ne se charge pas, la modale reste utilisable (nouvel onglet, téléchargement) au lieu de se bloquer.
- **Rendu progressif et annulable** : la page 1 s'affiche dès qu'elle est prête, les suivantes en arrière-plan ; rouvrir un autre document ou changer le zoom pendant un rendu annule le rendu précédent au lieu de superposer deux séries de pages.
- **Erreurs distinctes par cause** (réseau, HTTP non 2xx, contenu non-PDF, PDF invalide, document protégé) plutôt qu'un message générique — chaque cas correspond à une panne réellement observée en production.

Tout nouveau composant JS partagé suit le même principe : une fabrique d'état documentée en tête de fichier (pourquoi le composant existe, quel problème il évite de reproduire), consommée par étalement (`...window.<nom>.state()`) plutôt que par copier-coller entre écrans.

---

## 9. Gestion des migrations de base de données

Un seul moteur, un seul environnement de référence : **PostgreSQL**, du poste de développement à la production (Volume 0 §0.8, Volume 3 §8). Toutes les migrations sont gérées via `dotnet ef migrations`. Aucune requête SQL brute spécifique à un moteur n'est nécessaire, ce qui simplifie considérablement ce chapitre par rapport à la version 1.0 de ce document, qui devait gérer trois moteurs différents.

---

## 10. Gestion des branches Git

Stratégie officielle : **Git Flow**.

| Branche | Rôle |
|---|---|
| `main` | Production |
| `develop` | Intégration continue |
| `feature/*` | Nouvelle fonctionnalité |
| `release/*` | Préparation d'une version |
| `hotfix/*` | Correction urgente en production |

---

## 11. Standards de qualité

Aucun code n'est fusionné dans `develop` sans respecter : architecture conforme (Volume 2), tests unitaires associés, validation métier, gestion des erreurs, journalisation, respect des permissions (Volume 7), performances vérifiées.

---

## 12. Définition de terminé (Definition of Done)

Une fonctionnalité est terminée uniquement si :

- le code compile sans erreur ;
- les tests unitaires et d'intégration passent (Volume 8) ;
- les permissions sont respectées (Volume 7) ;
- l'interface est conforme au Volume 5 ;
- l'API est conforme au Volume 4 ;
- le schéma de données est conforme au Volume 3, **y compris le test d'isolation multi-tenant** (Volume 3 §2.4) ;
- la documentation (RTM, Volume 8) est mise à jour.

**Fin du Volume 6.**
