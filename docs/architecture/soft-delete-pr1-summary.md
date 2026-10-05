# Soft Delete global et index uniques — PR1 : résumé de livraison

**Date de clôture :** 05/10/2026
**Plan :** `docs/superpowers/plans/2026-10-01-soft-delete-index-uniques.md`
**Conception :** `docs/superpowers/specs/2026-10-01-soft-delete-index-uniques-design.md`
**Inventaire technique détaillé (référence vivante) :** `docs/architecture/soft-delete-index-inventory.md`
**Statut :** 6/6 tâches livrées et relues ; revue finale de branche « Prête à fusionner, avec correctifs » ; correctifs appliqués et re-relus, **boucle de revue clôturée, 0 point bloquant restant**. Une décision humaine de périmètre de branche reste ouverte (§6).

---

## 1. Ce qui a été livré, par domaine

### 1.1 Backend — contrat de suppression/restauration (Tâches 2-3)

- Toute suppression d'une entité de référence réutilisable (Bâtiment, Salle, Classe, Catégorie de
  frais, Mention, Année scolaire/Trimestre) passe par `SoftDelete(actorId)`, jamais une suppression
  physique.
- Contrat d'erreur normalisé à deux codes HTTP 409 :
  - `ARCHIVED_ENTITY_EXISTS` — création d'une identité qui n'existe qu'à l'état supprimé.
  - `ACTIVE_ENTITY_CONFLICT` — restauration vers une identité désormais portée par une ligne active.
- Restauration explicite, tenant-scoped, via des commandes typées par domaine (pas de commande
  polymorphe) ; lecture d'un tombstone par `IgnoreQueryFilters()` toujours re-filtrée sur
  `SchoolId`, jamais un contournement de la RLS.
- `teacher_subjects` (qualification enseignant-matière) suit un contrat miroir : révocable sans
  perte d'historique, ré-attribuable sans collision via un index unique partiel.
- **Livré pour :** Building, Classroom, Room, FeeCategory, Mention, SchoolYear/Term, teacher_subjects.
- **Non livré dans ce lot (périmètre réduit documenté, pas un bug) :** `CreateInventoryCategoryCommandHandler`,
  ClassFee, UserSchool, Term autonome, TeacherAssignment, Subject et les entités de normes — leurs
  index sont déjà partiels (Tâche 4) mais sans garde `ARCHIVED_ENTITY_EXISTS` à la création.

### 1.2 RLS & migrations SQL (Tâches 2, 4, 5)

- **Index uniques** : toutes les identités réutilisables (Building/Room/Classroom/FeeCategory/
  Mention/SchoolYear/Term/teacher_subjects) sont passées d'un index unique incluant `IsDeleted` à
  un index **partiel** `WHERE NOT "IsDeleted"` — un tombstone ne bloque plus jamais la recréation
  de la même identité.
- **Immuabilité au niveau base** : une migration dynamique révoque `DELETE` (puis, après la revue
  finale, aussi `TRUNCATE`) sur **toutes** les tables de `public` — y compris les vues — sauf une
  liste blanche strictement technique (`refresh_tokens`, `password_reset_tokens`,
  `matricule_sequences`). `ALTER DEFAULT PRIVILEGES` empêche toute table future d'hériter de ces
  droits. Le rôle applicatif (`sama_ecole_app`) ne peut donc plus, structurellement, supprimer
  physiquement une ligne métier — testé par 24 tests contre un PostgreSQL réel (code SQLSTATE
  `42501`), pas une simulation.
- **4 migrations additives** au total pour ce périmètre : `SoftDeleteUniqueIndexesAreLiveRowsOnly`
  (Tâche 4), `RevokeDeleteOnTeacherSubjects` (Tâche 2), `RevokeDeleteOnBusinessTables` (Tâche 5),
  plus deux migrations de correctif issues de la revue finale
  (`AddRoleGuardToTeacherSubjectsRevoke`, `WidenBusinessRevokeToViewsAndTruncate`). **Aucune
  migration déjà appliquée n'a été modifiée** — chaque correctif post-revue est allé en avant via
  une nouvelle migration, conformément à la règle non négociable du projet.

### 1.3 Finance — contre-écritures (Tâche 5)

- Un décaissement n'est plus jamais supprimé ni édité : son annulation crée une **contre-écriture**
  (montant et TVA opposés, `ReversalOfId` vers l'original).
- Invariants posés en base (pas seulement en code) : une seule contre-écriture par original (index
  unique partiel), toujours négative (contrainte `CHECK`), jamais d'annulation d'une annulation.
  Le rôle applicatif a aussi perdu `UPDATE` sur `Disbursements` (ajouté lors du correctif final —
  aucun chemin de code n'en avait besoin).
- Les agrégats (tableau de bord Finance, Trésorerie, déclaration de TVA) sont de simples sommes :
  la contre-écriture s'annule d'elle-même, sans recalcul spécifique.
- L'UI de la liste des décaissements distingue visuellement original / contre-écriture / annulé.

### 1.4 UI — corbeille et restauration (Tâche 3)

- Écrans Razor/Alpine.js de corbeille et restauration pour Bâtiments, Salles, Classes, Catégories
  de frais, Mentions, Années scolaires — panneau de corbeille partagé (`_TrashPanel.cshtml`),
  gestion du conflit 409 à la création avec lien vers l'élément archivé.
- Autorisation vérifiée par rôle (Directeur/Secrétariat selon le domaine ; Année scolaire réservée
  au Directeur) — couverte par 12 tests fonctionnels prouvant qu'un rôle non autorisé reçoit 403,
  ajoutés lors de la revue finale.

### 1.5 Tests & isolation (Tâche 6 + revue finale)

- Cycle complet Create→Delete→Trash→Restore→Visible testé via les vrais Handlers pour chaque
  agrégat restaurable.
- Concurrence **réelle** (deux `DbContext` en course depuis un état pré-commit partagé, pas deux
  appels séquentiels) prouvée pour Classroom/Room/SchoolYear : une seule ligne survit, l'autre
  reçoit un 409 normalisé.
- Partage d'identité active entre deux tenants prouvé pour Classroom/Room/SchoolYear/Building/
  FeeCategory/Mention ; isolation d'un tombstone entre tenants prouvée (ni liste ni restauration
  possible depuis l'autre école).
- Garde « parent bloqué si enfants rattachés » prouvée (Bâtiment avec Salle active).
- Suite ciblée de ce périmètre : **797+/798 à 24/24 selon la suite**, 0 échec attribuable à ce
  travail sur l'ensemble des relectures. Les quelques échecs observés sur la catégorie
  `MultiTenant` complète (régulièrement relancée tout au long de ce travail) partagent tous la même
  signature `TimeoutException` de création de conteneur Docker/Testcontainers, dans des modules
  étrangers à ce lot (Finance hors décaissements, Examens, Internat, Inventaire, Enseignants...) —
  confirmé comme une saturation d'environnement local, pas une régression, par ré-exécution isolée
  systématique de chaque classe en échec (toujours verte seule).

---

## 2. Propreté de l'historique Git et du worktree

- `git status --short` : rien à committer, aucun fichier non suivi, aucun artefact de scratch
  (le fichier de diagnostic temporaire `TmpGrantDump.cs` repéré en cours de route n'a jamais été
  committé et a disparu avant toute action de ma part).
- `git diff --check` sur l'ensemble du périmètre soft-delete : aucune erreur d'espace blanc.
- Aucune migration déjà appliquée n'a été éditée (vérifié migration par migration via `git log`
  sur chaque fichier).
- Aucun secret ni fichier `.env` ajouté.
- Rien poussé vers un dépôt distant à aucun moment de ce travail.

---

## 3. Prêt pour le push / l'ouverture de la PR1 ?

**Le travail Soft Delete lui-même : oui.** Zéro point Critique ou Important ouvert sur le code, les
migrations, les tests ou la documentation technique (inventaire rafraîchi et exact).

**La branche en tant que PR1 : non, pas sans décision.** `claude/senegal-series-subjects-coefficients-ndqwkq`
porte aussi, entremêlés avec les commits soft-delete, le chantier **frais optionnels** (migration
`AddFeeCategoryIsOptional`, plomberie `IsOptional`) et le chantier **noms bilingues Franco-Arabe**
(`FullNameAr`/`GuardianNameAr`) — deux périmètres que le plan exclut explicitement de PR1
(« Hors plan : toute implémentation du chantier des frais optionnels appartient à la PR2 »).
Ouvrir cette branche contre `main` en l'état produirait une PR combinée, pas une PR1 isolée.

**Deux options, à ta décision :**
1. **Extraire une branche PR1 propre** — créer une nouvelle branche depuis `main`, et y
   cherry-picker (ou rebaser) uniquement les commits soft-delete (plage `ae8ab3c3` → `82c606a6`,
   liste exacte à établir via `git log --oneline ae8ab3c3^..82c606a6` en excluant les commits
   Franco-Arabe/frais optionnels interleaved), en laissant le reste sur la branche de travail actuelle.
2. **Accepter une PR combinée** — ouvrir la PR en l'état, avec un titre/description qui reflète
   explicitement les trois chantiers livrés ensemble.

Je n'ai pris aucune mesure sur cette question (ni rebase, ni split, ni push) — c'est une décision
qui t'appartient, pas un défaut de code à corriger.

---

## 4. Suivi — points mineurs non bloquants restants

Documentés et laissés en l'état (triés par la revue finale comme ne nécessitant pas de correction
avant push) :

- Message d'erreur générique sur une double contre-écriture concurrente (reste un 409, juste moins
  précis que possible).
- `matricule_sequences` garde `DELETE` sans qu'aucun code ne l'utilise (resserrable, non urgent).
- Asymétrie de restauration FeeCategory/ClassFee (les frais de classe en cascade ne sont pas
  restaurés automatiquement avec la catégorie).
- `TeacherSubjectHistoryTests` ne couvre qu'un seul tenant.
- FK `ReversalOfId` en colonne simple plutôt que composite `(SchoolId, Id)` — défense en profondeur
  uniquement, non atteignable par l'application.
- Petite incohérence cosmétique dans l'inventaire (« trois » vs « quatre » migrations à un endroit) —
  sans impact factuel.
