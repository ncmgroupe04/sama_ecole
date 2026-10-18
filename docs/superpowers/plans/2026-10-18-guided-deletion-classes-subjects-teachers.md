# Suppression guidée des classes, matières et enseignants (PR2) — plan

> Exécution en ligne dans la session (pas de sous-agents). Même méthode que la PR1 : test d'abord, un commit par tâche.

**Goal :** appliquer la suppression guidée (blocage financier, liens énumérés, rupture explicite) aux **classes, matières et enseignants**, en réutilisant le socle de la PR1.

**Spec :** `docs/superpowers/specs/2026-10-17-deletion-dependency-guard-design.md` (§6 catalogue). **Plan PR1 :** `2026-10-18-guided-student-deletion.md`.

## Décisions

1. **Socle générique** : `DeletionDependency` (ex-`StudentDependency`), constructeurs `DeletionDependencies.Owned<T>` / `ArchiveAsync`, une `EntityDeletionPolicy` (existence, règle financière, liste de dépendances, phrase d'en-tête) et un `EntityDeletionAnalyzer` (analyse + rupture) partagés. Les élèves passent par ce socle sans changer d'API.
2. **Une seule paire de requêtes MediatR** pour les nouvelles entités : `GetDeletionImpactQuery(resource, id)` et `UnlinkDependenciesCommand(resource, id)`, `resource` ∈ {Classroom, Subject, Teacher}. Les contrôleurs exposent `GET {id}/deletion-impact` et `POST {id}/unlink-dependencies` (mêmes rôles que leur `DELETE`). Les `DELETE` existants sont durcis : ils ne rompent jamais rien (409 `FINANCIAL_RECORDS_EXIST` / `DEPENDENCIES_EXIST`).
3. **Règles financières** (spec §2.3 propagée) :
   - Classe : un paiement non annulé sur une inscription de la classe → bloqué.
   - Enseignant : une fiche de paie sur l'un de ses contrats → bloqué.
   - Matière : aucune.
4. **Liens non rompables ici** (à traiter avant) : élèves rattachés à une classe (`Student.ClassroomId` est obligatoire : on les transfère ou on les supprime), activités d'un domaine (suppression ligne par ligne, comme avant), matériel prêté non restitué (enseignant).
5. **Garde-fou** : un test de couverture du modèle EF par entité (toute FK vers `Classroom` / `Subject` / `Teacher` doit être classée).
6. **Interface** : `guided-delete.js` reçoit un texte d'aide financier configurable ; `classrooms.js`, `subjects.js`, `teachers.js` remplacent leurs modales de suppression par la modale partagée.

## Tâches

1. **Socle générique** — renommer/extraire, refactorer les élèves ; les tests de la PR1 doivent rester verts.
2. **Classes** — politique + tests (analyse, rupture, couverture FK, financier) + `DELETE` durci.
3. **Matières** — idem (notes archivées avec confirmation, activités non rompables).
4. **Enseignants** — idem (fiche de paie bloquante, contrat archivé, prêts de matériel).
5. **Requêtes génériques + routes** des trois contrôleurs + tests fonctionnels.
6. **Interface** des trois écrans + tests JS.
7. **Recette navigateur, documentation (`openapi.yaml`, spec), PR.**
