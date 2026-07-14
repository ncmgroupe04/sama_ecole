# SAMA ECOLE

# VOLUME 8 — Test Strategy & Quality Assurance (TSQA)

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0 (précédemment noyée dans un fichier partagé)
**Changement principal :** ajout d'un test obligatoire et non négociable sur l'isolation multi-tenant (§4bis) ; suppression des « tests de migration SQLite → MySQL → PostgreSQL » (Volume 0 v2.0 — un seul moteur, dès le jour 1).

---

## Table des matières

1. Objectif
2. Stratégie globale (pyramide de tests)
3. Tests unitaires
4. Tests d'intégration
5. Test obligatoire d'isolation multi-tenant
6. Tests End-to-End (E2E)
7. Tests de performance
8. Tests de sécurité
9. Tests de sauvegarde et restauration
10. Recette utilisateur (UAT)
11. Critères de qualité (Definition of Ready to Ship)
12. Requirements Traceability Matrix (RTM)
13. Catalogue des cas de test (extrait de référence)

---

## 1. Objectif

Garantir que chaque version de Sama Ecole est fiable, stable, sécurisée, performante et conforme au cahier des charges (Volume 1) et aux spécifications techniques (Volumes 2 à 7). Aucune fonctionnalité n'est mise en production sans avoir été testée.

## 2. Stratégie globale

Pyramide de tests, du plus nombreux/rapide au plus rare/coûteux :

```
        ▲  Tests manuels utilisateurs (UAT)
        │  Tests End-to-End (E2E)
        │  Tests d'intégration
        ▼  Tests unitaires (base large)
```

## 3. Tests unitaires

Chaque service métier (Élèves, Inscriptions, Finance, Notes, Bulletins, Utilisateurs) dispose de tests unitaires couvrant les cas nominaux, les cas d'erreur, les validations métier et les calculs automatiques (moyennes, totaux, soldes). Outils : **xUnit**, **Moq**, **FluentAssertions**.

## 4. Tests d'intégration

Valident les interactions entre modules : Inscription → Finance, Élèves → Notes, Notes → Bulletins, Finance → Reçus, Utilisateurs → Permissions, Paramètres → Génération des bulletins.

## 5. Test obligatoire d'isolation multi-tenant

> Chapitre ajouté dans cette version — absent de la version 1.0, alors que le multi-tenant y était déjà mentionné comme cible.

Pour **chaque** entité portant `SchoolId`, un test d'intégration automatisé vérifie :

1. Un utilisateur authentifié de l'école A ne peut lire aucune ligne de l'école B, même par ID direct (`GET /students/{id}` d'un élève de B avec un token de A → `404`, jamais `403`, pour ne pas révéler l'existence de la ressource).
2. Une requête SQL brute exécutée avec le rôle applicatif `sama_ecole_app` (sans `BYPASSRLS`) ne retourne que les lignes de l'école courante, **même si le Global Query Filter EF Core est désactivé manuellement dans le test** — ce qui prouve que la Row-Level Security PostgreSQL (Volume 3 §2.2) constitue bien une barrière indépendante et non un doublon cosmétique.

Ce test est exécuté à **chaque** pipeline CI/CD, pas seulement en recette manuelle, et bloque le merge en cas d'échec.

## 6. Tests End-to-End (E2E)

Simulent un parcours utilisateur réel, de bout en bout, sur un environnement proche de la production.

**Exemple — inscription complète d'un élève :**
1. Connexion du Secrétaire.
2. Création du parent/tuteur puis de l'élève.
3. Affectation à une classe (contrôle de places disponibles).
4. Validation de l'inscription → événement `EnrollmentConfirmed`.
5. Connexion du service Finance → montant dû visible automatiquement.
6. Paiement des frais → génération du reçu.
7. Vérification des données enregistrées et du journal d'audit.

Ce scénario est automatisé et exécuté avant chaque version majeure.

## 7. Tests de performance

| Action | Temps maximal cible |
|---|---|
| Connexion | 2 s |
| Recherche d'un élève | 1 s |
| Ouverture d'un module | 1 s |
| Génération d'un bulletin | 5 s |
| Génération d'un reçu | 2 s |
| Export PDF / Excel | 10 s |

Des tests de charge sont réalisés sur des jeux de données allant jusqu'à **100 000 élèves multi-écoles simultanées** pour valider la montée en charge de l'architecture multi-tenant (et non plus une seule école isolée comme dans la version 1.0).

## 8. Tests de sécurité

Couvrent : permissions par rôle (matrice du Volume 7), tentatives d'accès non autorisées, résistance aux injections SQL, validation des fichiers importés, chiffrement des mots de passe, gestion des sessions et de leur expiration, résistance au brute force (rate limiting, Volume 7 §9).

## 9. Tests de sauvegarde et restauration

Scénarios couverts : sauvegarde complète automatique, restauration complète sur environnement de secours, restauration après interruption. Les données restaurées doivent être strictement identiques aux données sauvegardées (vérification par somme de contrôle). Détail opérationnel : Volume 9.

## 10. Recette utilisateur (UAT)

Avant chaque mise en production majeure, une recette est réalisée avec un panel représentatif : Directeur, Secrétaire, Responsable Finance, Enseignant, Super Administrateur. Chaque profil valide les fonctionnalités le concernant, selon les critères d'acceptation du Volume 1.5 §9.

## 11. Critères de qualité (Definition of Ready to Ship)

Une version est déclarée conforme uniquement si :
- 100 % des tests critiques (dont le test d'isolation multi-tenant, §5) sont réussis ;
- aucune régression n'est détectée ;
- toutes les exigences de la RTM (§12) sont couvertes ;
- les performances respectent les objectifs (§7) ;
- les tests de sécurité sont validés (§8).

## 12. Requirements Traceability Matrix (RTM)

Chaque exigence fonctionnelle reçoit un identifiant unique `REQ-{MODULE}-{NNN}` et est reliée au SDS, au DDS, à l'API, à l'écran UI et au(x) test(s) qui la couvrent.

| Préfixe | Module |
|---|---|
| REQ-SCH | École |
| REQ-STU | Élèves |
| REQ-TEA | Enseignants |
| REQ-REG | Inscriptions |
| REQ-FIN | Finance |
| REQ-GRA | Notes |
| REQ-RPT | Bulletins |
| REQ-SET | Paramètres |
| REQ-AUD | Audit |
| REQ-SUBS | Abonnements |

**Exemple — Module Élèves**

| ID | Exigence | API | Table | Écran | Test | Statut |
|---|---|---|---|---|---|---|
| REQ-STU-001 | Ajouter un élève | `POST /students` | `Students` | Élèves | `UNIT-STU-001` | Terminé |
| REQ-STU-002 | Modifier un élève | `PUT /students/{id}` | `Students` | Élèves | `UNIT-STU-002` | Terminé |
| REQ-STU-003 | Archiver (suppression logique) | `DELETE /students/{id}` | `Students` | Élèves | `UNIT-STU-003` | Terminé |
| REQ-STU-004 | Import Excel | `POST /students/import` | `Students` | Import | `INT-STU-001` | Post-MVP |

**Exemple — Module Finance**

| ID | Exigence | API | Table | Test |
|---|---|---|---|---|
| REQ-FIN-001 | Paiement | `POST /payments` | `Payments` | `UNIT-FIN-101` |
| REQ-FIN-002 | Dépense | `POST /expenses` | `Expenses` | `UNIT-FIN-102` |
| REQ-FIN-003 | Reçu PDF | `POST /receipts/generate/{paymentId}` | `Receipts` | `E2E-FIN-005` |

Avant chaque version, une vérification automatique confirme : aucune exigence orpheline, aucune API sans exigence associée, aucun écran non documenté, aucun test manquant.

## 13. Catalogue des cas de test (extrait de référence)

Convention : `TC-{MODULE}-{NNNN}`. Chaque cas précise préconditions, étapes, résultat attendu, priorité (Critique/Haute/Moyenne/Faible) et type (Fonctionnel, Sécurité, Performance, Intégration, E2E, Régression).

**Exemple — TC-STU-0001 : Création d'un élève**

| Champ | Détail |
|---|---|
| Préconditions | Utilisateur connecté, rôle Secrétariat, école active |
| Étapes | 1. Ouvrir Élèves → 2. Ajouter → 3. Saisir les informations obligatoires → 4. Enregistrer |
| Résultat attendu | Élève créé, matricule généré selon Volume 1 §2.1, aucune ligne orpheline si annulation avant l'étape 4 |
| Priorité | Critique |
| Type | Fonctionnel |

**Exemple — TC-TEN-0001 : Isolation multi-tenant (test critique transverse)**

| Champ | Détail |
|---|---|
| Préconditions | Deux écoles A et B existantes, avec au moins un élève chacune |
| Étapes | 1. S'authentifier comme utilisateur de A → 2. Appeler `GET /students/{id}` avec l'ID d'un élève de B |
| Résultat attendu | Réponse `404 Not Found`, aucune donnée de B exposée, tentative journalisée |
| Priorité | Critique — bloque toute mise en production |
| Type | Sécurité |

L'équipe QA maintient le catalogue complet (plusieurs centaines de cas à terme) dans l'outil de gestion de tests choisi par l'équipe (ex. dans le dépôt de code, sous forme de tests automatisés eux-mêmes, plutôt que dans un document séparé qui se désynchronise vite du code réel).

**Fin du Volume 8.**
