# Inventaire — cycles de vie, suppressions et unicité

**Date de relevé :** 2026-10-01
**Objet :** PR1 « Soft Delete global et index uniques » (conception du 2026-10-01).
**Statut :** relevé initial (2026-10-01) **mis à jour le 2026-10-04** après l'implémentation backend de la PR1 — voir « Statut d'implémentation » ci-dessous. Les sections 2 à 8 conservent l'analyse ; chaque écart y est annoté « ✅ corrigé » ou « ⏳ restant ».
**Sources examinées :** `AGENTS.md`, volumes DDS/API/Sécurité/Stratégie de tests (3, 4, 7, 8), types `Domain/Entities`, configurations EF, `ApplicationDbContext` et `ApplicationDbContextModelSnapshot`, migrations historiques, handlers/queries applicatifs et tests d’intégration/unitaires. Ce document décrit le modèle et le code présents, puis fixe le traitement attendu; il ne prétend pas connaître les données des bases déployées.

## 0. Statut d'implémentation (backend PR1, 2026-10-04)

Vérifié par tests PostgreSQL réels (Testcontainers, rôle applicatif `sama_ecole_app` NOBYPASSRLS), pas seulement par lecture du code.

### 0.1 Restauration et conflits (API)

| Entité | Création bloquée (`409 ARCHIVED_ENTITY_EXISTS`) | Corbeille `GET .../deleted` | Restauration `POST .../{id}/restore` | Règles propres |
|---|---|---|---|---|
| `Building` | ✅ | ✅ | ✅ | Directeur/Secrétariat |
| `Classroom` | ✅ | ✅ | ✅ | Le programme (`class_subjects`) n'est pas réactivé implicitement |
| `Room` | ✅ | ✅ | ✅ | Refus `PARENT_ENTITY_ARCHIVED` si le bâtiment est supprimé |
| `FeeCategory` | ✅ | ✅ | ✅ | Les lignes de barème supprimées avec elle ne sont pas réactivées (aucune fusion) |
| `Mention` | ✅ | ✅ | ✅ | Même autorisation que l'écriture des mentions |
| `SchoolYear` (+ `Term`, `TeacherAssignment`) | ✅ | ✅ | ✅ | Cascade symétrique (enfants partis AVEC l'année) ; `ACTIVE_ENTITY_CONFLICT` si libellé repris, période chevauchante, rang de période repris ou autre année active ; la restauration n'active jamais l'année ; affectations dont enseignant/classe/matière sont supprimés : laissées supprimées et comptées |
| `TeacherSubject` | n/a (une requalification crée une nouvelle ligne) | — | — | Retrait = soft delete ; index actif ; historique lisible |

Communs : `404` pour un ID hors tenant (aucune fuite d'existence), `SchoolId` réimposé après `IgnoreQueryFilters()`, restauration atomique (un seul `SaveChanges`), aucune fusion. Code partagé : `Application/Common/SoftDelete/SoftDeleteLifecycle.cs` ; les commandes restent typées par entité.

**⏳ Restant (hors lot actuel) :** `Subject`, `InventoryCategory`/`InventoryItem`, `SyllabusUnit`, `ClassFee`, `UserSchool`, `Student`, `Teacher`, normes (`GradeAgeNorm`, `WeeklyHourNorm`), associations d'options/dispenses/surcharges. Leur création ne déclenche pas encore `ARCHIVED_ENTITY_EXISTS` et elles n'ont pas de corbeille.

### 0.2 Index uniques partiels

Convertis en index partiels `WHERE "IsDeleted" = false` (migration `20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly`, déjà sur la branche) : `buildings`, `classrooms`, `class_fees`, `fee_categories`, `inventory_categories`, `mentions`, `rooms`, `school_years`, `terms`, `user_schools`. `teacher_subjects` : migration `20261001000000_RevokeDeleteOnTeacherSubjects` (précontrôle des doublons actifs, `Down` refusé si plusieurs tombstones).

**Décision métier (close) :** `grades` et `report_card_remarks` sont **exclus** du soft delete et de la restauration. Leur index conserve `IsDeleted` dans la clé ; aucune restauration n'est offerte. Les identifiants permanents (numéros de reçu, clés d'idempotence, matricules, IEN, codes de vérification, références de transaction) restent non partiels (§3).

Test PostgreSQL : `SoftDeleteUniqueIndexTests` (plusieurs tombstones + une seule ligne active, deux établissements, doublon actif refusé).

### 0.3 Immuabilité et privilèges `DELETE`

Audit **à l'exécution** (base migrée, rôle applicatif) : `DELETE` accordé sur 13 tables — `Disbursements`, `ScheduleSlots`, `classrooms`, `employee_contracts`, `fiche_paies`, `matricule_sequences`, `password_reset_tokens`, `refresh_tokens`, `schools`, `students`, `subscriptions`, `taxe_declarations`, `users` — et sur `__EFMigrationsHistory`.

✅ Migration `20261004034236_RevokeDeleteOnBusinessTables` : `REVOKE DELETE` dynamique sur **toutes** les tables de `public` sauf la liste blanche technique (`refresh_tokens`, `password_reset_tokens`, `matricule_sequences`), plus `ALTER DEFAULT PRIVILEGES ... REVOKE DELETE`. Couvre aussi les tables dont seul un privilège par défaut d'environnement (docker init) aurait donné `DELETE`. Les purges légitimes (`reset_school_data`, `delete_school_year`) sont des fonctions `SECURITY DEFINER` sous le rôle propriétaire : non affectées. `Down` volontairement vide (roll-forward).

Tests (`ImmutableRegistersTests`) : liste blanche **exacte** des tables supprimables (une nouvelle table supprimable fait échouer le test) ; `DELETE` réellement tenté et refusé (`42501`) sur 12 registres (paiements, ventilations, paiements d'abonnement, paie, déclarations, décaissements, audit, mouvements de stock, historique de barème, historique de contrats, historique de statut, certificats) ; journaux append-only (`audit_logs`, `stock_movements`, `fee_change_history`, `employee_contract_histories`, `user_status_history`) sans `UPDATE`.

**Décaissements :** `DELETE /finance/disbursements/{id}` crée désormais une **contre-écriture** (montants et TVA opposés, `ReversalOfId`, datée du jour d'annulation) au lieu d'un soft delete ; migration additive `20261004034202_AddDisbursementReversal` (index unique partiel : une seule contre-écriture par original ; `CHECK` : montant négatif ; FK `RESTRICT`). Une annulation ne peut être annulée (`409 DISBURSEMENT_IS_REVERSAL`), ni une double annulation (`409 DISBURSEMENT_ALREADY_REVERSED`). Les agrégats (solde réel, trésorerie, TVA déductible) s'annulent d'eux-mêmes ; `DisbursementDto` expose `ReversalOfId` et `IsReversed`.

### 0.4 Écarts assumés et points ouverts

1. **Changement de comportement :** recréer un nom supprimé renvoie `409 ARCHIVED_ENTITY_EXISTS` (spec §3.2, décision n°2) au lieu de réussir (commit antérieur `547b9535`). Le test fonctionnel Mentions a été aligné ; les écrans existants ne gèrent pas encore ce 409 (UI à venir).
2. **Purge en mode test :** `delete_school_year` / `reset_school_data` restent des suppressions **physiques** (fonctions `SECURITY DEFINER`, école non encore en production, déclenchées par le Directeur et journalisées). Exception connue à la règle générale, bornée à ce chemin.
3. **`Disbursements` garde `UPDATE`** (aucune commande ne l'utilise) : non strictement append-only, contrairement aux journaux de §0.3.
4. **`FeeCategory.IsOptional`** est déjà présent sur la branche (migration `20260930105018_AddFeeCategoryIsOptional`, venue d'`origin`) alors que la spec le classe en PR2.
5. **`ON DELETE CASCADE` historiques** : non audités FK par FK ; sans effet applicatif tant qu'aucun `Remove` n'est émis et que le rôle n'a plus `DELETE`.
6. **Tests dépendants de la date :** 5 tests fonctionnels Attendance échouent les dimanches (jour de repos), indépendamment de la PR1.

## 1. Règles de lecture

- Le filtre `ApplicationDbContext.SetTenantFilter` s’applique à chaque `AuditableEntity, ITenantEntity` et combine `SchoolId == CurrentSchoolId && !IsDeleted`. Il n’y a pas de mécanisme partagé dans `SaveChanges` qui transforme un `Remove` arbitraire en soft delete; les handlers doivent appeler `SoftDelete(...)` explicitement.
- Les données métier tenant restent sous filtre EF et RLS PostgreSQL. Les exemptions au filtre sont détaillées en §5; elles ne désactivent jamais la RLS côté base.
- « Réutilisable » signifie que l’identité peut être reprise après suppression logique; la cible est un index partiel sur les seules lignes actives. « Permanente » signifie que la clé historique reste réservée, même si un enregistrement est annulé/archivé. Les clés techniques et relations de jonction sont classées selon leur invariant, pas converties mécaniquement.
- Aucun dédoublonnage de production ne peut être déduit du code. Les collisions actives doivent être mesurées avant migration et rapportées sans correction automatique.

## 2. Entités persistées et classification

Les entités persistées ci-dessous proviennent des `DbSet` et des entités découverts par les configurations / snapshot (les formes de résultat sans clé sont exclues). La classification est la cible de cycle de vie; les observations signalent les écarts connus au modèle actuel.

| Entité(s) persistée(s) | Classe | Suppression / restauration et dépendances |
|---|---|---|
| `School`, `SchoolSettings` | Référence modifiable, globale ou configuration tenant | Désactivation/archivage seulement si des données historiques la référencent; ne pas cascader sur historique. Réglages : suppression bloquée, modification en place. Pas de corbeille utilisateur pour l’établissement plateforme. |
| `User`, `UserSchool` | Référence modifiable d’identité / association d’accès | Désactiver le compte et révoquer les associations; conserver l’audit et l’historique de statut. Restaurer par identifiant si le rôle/association est réactivable, après contrôle d’unicité et autorisation. Cette association est actuellement unique avec `IsDeleted` dans les colonnes, donc plusieurs tombstones ne sont pas possibles. |
| `Student`, `Teacher` | Référence modifiable, supprimable sous condition | Archiver si aucune donnée historique ne l’interdit; préserver inscriptions, notes, paiements/reçus et mouvements. Restaurer par ID; matricules/IEN: voir §3. |
| `Classroom`, `Building`, `Room`, `Subject`, `FeeCategory`, `ClassFee`, `Mention`, `SchoolYear`, `Term`, `GradeAgeNorm`, `WeeklyHourNorm`, `SyllabusUnit` | Références/configurations modifiables | Corbeille/restauration par ID. Bloquer la suppression si des données immuables/historiques sont liées; sinon désactiver parent et garder les enfants. L’année scolaire porte une règle dédiée en §4. Les clés métier réutilisables doivent être libérées par index actif seulement. |
| `ClassSubject`, `StudentSubjectEnrollment`, `StudentSubjectExemption`, `SubjectCoefficientOverride` | Association/configuration révocable ou modifiable | Révocation logique; restaurer l’association précise si possible. La portée active ou permanente est classée individuellement par index en §3. |
| `TeacherSubject` | Association révocable (identifiée explicitement par la conception) | ✅ Corrigé : soft delete (acteur + date), index unique partiel actif, `DELETE` révoqué, lectures historiques des bulletins vérifiées. Une requalification après révocation crée une nouvelle ligne (jamais de réactivation). |
| `TeacherAssignment` | Opérationnelle/configuration d’année, révocable | Soft delete lorsqu’une affectation est retirée; ne pas supprimer les bulletins ou historiques. Lecture d’historique doit utiliser l’association à la période. Index à filtre actif existant; commande actuelle ignore les tombstones pour vérifier un doublon. |
| `Enrollment`, `EnrollmentFeeLine`, `FeeInstallmentPlan`, `FeeInstallment`, `FinancialCommitment` | Opérationnelle, suppression conditionnelle | Inscription annulable seulement avant toute écriture financière opposable; sinon conserver/désactiver et contrepasser. Lignes figées de frais validés restent immuables. Plans/échéances liés aux reçus et paiements: bloquer ou désactiver le parent, jamais effacer une preuve financière. Restauration par ID si l’état métier le permet. |
| `Payment`, `PaymentBreakdown`, `SubscriptionPayment`, `FichePaie`, `TaxeDeclaration`, `Disbursement`, `FeeChangeHistory`, `EmployeeContractHistory`, `TeacherHourRecord` | Registre immuable / financier | Pas de suppression/restauration utilisateur; correction par écriture compensatrice ou nouvelle version historisée. Conservation permanente des références et identifiants de transaction/reçu. |
| `EmployeeContract` | Opérationnelle supprimable par état | Fin de contrat via `EndDate` (pas suppression); unicité de contrat courant définie par `TeacherId/UserId` non nul et `EndDate IS NULL`. Ne pas ajouter `IsDeleted` au prédicat sans décision domaine. |
| `CashierSession` | Opérationnelle bornée par état | Fermer la session; ne pas effacer les paiements/encaissements. Contrainte « une session ouverte par caissier » demeure liée à l’état ouvert, pas à la suppression. |
| `ScheduleSlot` | Configuration opérationnelle temporelle | Désactiver/archiver un créneau futur seulement si aucun appel/billet courant ne le référence; conserver ses liens et dates pour les lectures historiques. |
| `AttendanceSheet`, `StudentAttendance`, `TeacherAttendance`, `LateArrival`, `AbsenceJustification`, `EarlyDeparture`, `DisciplineRecord`, `ParentSummons` | Registre opérationnel/historique, suppression sous condition | Une correction ne doit pas effacer preuve d’appel, billet ni justification. Annulation par état et conservation des précédents; bloquer si événement déjà utilisé dans bulletin/audit ou désactiver le parent. Les billets annulés libèrent leur identité active par statut. |
| `Grade`, `ReportCardRemark`, `ExamSession`, `ExamDossier`, `ExamResult`, `StudentMutationCertificate` | Registre académique/historique opposable | Publication/certification rend la donnée immuable. Avant publication, correction versionnée selon le domaine; après publication, ne pas supprimer mais rectifier/historiser. Certificat délivré et son code de vérification ne sont jamais réattribués. |
| `ClassJournalEntry`, `ClassJournalEntryUnit`, `QuranProgress`, `QuranEvaluation` | Registre pédagogique consultable dans le temps | Correction sans effacement d’un historique publié; supprimer logiquement une saisie erronée seulement si non utilisée et avec audit/acteur. Dépendances d’unité/entrée à traiter ensemble; ne pas cascader vers l’historique académique. |
| `InventoryCategory`, `InventoryItem` | Référence de patrimoine modifiable, suppression conditionnelle | Archiver uniquement sans mouvements/prêts qui en dépendent; sinon désactiver le bien/catégorie. Restaurer explicitement. Code article réutilisable seulement si le métier l’autorise et index actif filtré. |
| `StockMovement` | Registre immuable append-only | Jamais de suppression ou restauration; corriger par mouvement inverse. Rôle applicatif documenté `SELECT, INSERT` uniquement. |
| `ItemAssignment` | Opérationnelle avec historique de prêt | Retour/annulation par statut; conserver les fiches et reçus de décharge. Ne pas supprimer si mouvements ou preuve de remise liés; restaurer par identifiant si erreur d’annulation. |
| `AuditLog`, `GlobalAuditLogEntry` (projection/fonction) | Registre immuable | Aucune mutation/suppression utilisateur. `GlobalAuditLogEntry` est une forme sans clé, résultat de `get_global_audit_logs`, pas une table EF. |
| `UserStatusHistory` | Historique immuable de sécurité | Ajouter un événement pour chaque suspension/réactivation; pas de suppression utilisateur. |
| `RefreshToken`, `PasswordResetToken` | Technique éphémère de sécurité | Révocation/expiration puis purge technique autorisée, sans corbeille. Hors commandes métier; le token n’est pas restauré. |
| `MatriculeSequence` | Technique mutable de séquencement | Compteur de génération, écrit uniquement par `MatriculeGenerator`; jamais restauré dans une corbeille. Maintenir atomique `INSERT ... ON CONFLICT`. |
| `SmsMessage`, `DebtorReminderBatch`, `DebtorReminderBatchItem` | Technique opérationnelle / journal de livraison | États retry/échec/annulé; conserver la preuve d’envoi selon rétention et audit. Purge technique bornée, pas d’archivage utilisateur. |
| `PromoCode` | Référence commerciale plateforme modifiable | Désactiver ou archiver; le code peut être réutilisé uniquement selon règle explicite globale. Aucun filtre actif actuellement. |
| `SchoolRegistrationRequest` | Workflow plateforme | Clôturer/refuser/expirer, conserver `TrackingReference` à vie pour preuve et suivi. Pas de restauration comme donnée métier scolaire. |
| `Subscription` | État d’abonnement plateforme, non supprimable logiquement par tenant | Suspendre/réactiver; paiements d’abonnement et webhooks append-only. Navigation administrative globale autorisée explicitement. |
| `MutationCertificateVerification` | Read model sans clé | Résultat uniquement de la fonction publique minimale `verify_mutation_certificate`; non persisté et sans suppression. |
| `PlatformDashboardStats`, `PlatformSubscriptionRow`, `PublicSchoolListing` | Read model / vue | Pas d’écriture ni de corbeille; la vue publique contient uniquement les champs publiés. |

### Entités sans clé et tables non matérialisées

`PlatformDashboardStats` (`v_platform_dashboard_stats`), `PublicSchoolListing` (`public_school_directory`), `GlobalAuditLogEntry` (`get_global_audit_logs`) et `MutationCertificateVerification` (`verify_mutation_certificate`) ne sont pas des lignes persistées manipulables comme des entités métier. Les types historiques mentionnés dans le DDS mais absents du modèle actuel (ex. `Guardian`, `StudentGuardian`, `Notification`, `BackupHistory`, `ApplicationLogs`) ne figurent ni dans les DbSet/configurations/snapshot actuels; ils ne sont donc pas inclus dans le décompte des entités persistées de cette version.

## 3. Index et contraintes uniques présents dans le modèle

La table contient exactement une entrée pour chacun des 60 appels `.IsUnique()` dans les configurations courantes et leur index unique correspondant dans `ApplicationDbContextModelSnapshot.cs`. Les index distincts d’une même table restent des lignes distinctes; un index n’est jamais répété sous un libellé plus descriptif. La colonne « état courant » donne l’expression de clé et le filtre actuels. « Test » cite un fichier et une méthode lorsque le test d’unicité de la contrainte a été directement repéré; sinon elle indique explicitement `aucun test identifié` (un test de domaine ou d’isolation seul n’est pas présenté comme test d’unicité).

| Configuration — table et identité unique | Portée métier / réutilisation après suppression | État courant dans le modèle EF | Test direct repéré |
|---|---|---|---|
| `BuildingConfiguration` — `buildings(SchoolId, Name)` | Tenant, nom de bâtiment; réutilisable après archivage | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié |
| `AttendanceSheetConfiguration` — `attendance_sheets(SchoolId, ClassroomId, SubjectId, Date, Period)` | Tenant et séance; identité historique d’appel, ne pas libérer après utilisation | Index unique sans filtre | `tests/SamaEcole.IntegrationTests/Attendance/AttendanceIsolationTests.cs` — `Same_Class_Subject_Date_And_Period_Cannot_Be_Recorded_Twice` |
| `ClassroomConfiguration` — `classrooms(SchoolId, Name)` | Tenant, nom de classe; réutilisable après archivage si aucun élève actif | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié pour cet index |
| `ClassSubjectConfiguration` — `class_subjects(ClassroomId, SubjectId)` | Programme de classe; réutilisable après révocation | `WHERE NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/ClassSubjects/ClassSubjectsIsolationTests.cs` — `A_Subject_Appears_Once_In_The_Programme_Of_A_Class` |
| `ClassJournalEntryUnitConfiguration` — `class_journal_entry_units(ClassJournalEntryId, SyllabusUnitId)` | Lien d’une unité à une entrée; retrait réversible | `WHERE NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/Syllabus/SyllabusTrackingTests.cs` — `Correcting_An_Entry_Replaces_Its_Units_And_Archives_The_Removed_Ones` |
| `ClassFeeConfiguration` — `class_fees(SchoolId, FeeCategoryId, ClassroomId)` | Barème de catégorie/classe; identité réutilisable après archivage | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié pour cet index |
| `CashierSessionConfiguration` — `cashier_sessions(SchoolId, CashierId)` | Une session de caisse ouverte par caissier; fermée = identité réutilisable | `WHERE Status = 'Open'` (filtre métier) | aucun test identifié pour cette contrainte unique |
| `EmployeeContractConfiguration` — `employee_contracts(TeacherId)` | Un contrat courant par enseignant; réutilisable après clôture | `WHERE TeacherId IS NOT NULL AND EndDate IS NULL` | `tests/SamaEcole.IntegrationTests/Finance/EmployeeContractLifecycleTests.cs` — `Closing_A_Contract_Should_Allow_A_New_Contract_For_The_Same_Person` |
| `EmployeeContractConfiguration` — `employee_contracts(UserId)` | Un contrat courant par compte; réutilisable après clôture | `WHERE UserId IS NOT NULL AND EndDate IS NULL` | aucun test identifié spécifiquement pour la clé `UserId` |
| `EnrollmentConfiguration` — `enrollments(SchoolId, ReceiptNumber)` | Numéro de reçu tenant; permanent, y compris après annulation | Index unique sans filtre | aucun test identifié |
| `EnrollmentConfiguration` — `enrollments(SchoolId, StudentId, SchoolYearId)` | Une inscription non annulée par élève/année; réutilisable après annulation | `WHERE NOT IsDeleted AND Status <> 'Cancelled'` | aucun test identifié pour ce filtre d’index |
| `ExamResultConfiguration` — `exam_results(ExamDossierId)` | Un résultat par dossier; historique officiel permanent | Index unique sans filtre | aucun test identifié |
| `ExamDossierConfiguration` — `exam_dossiers(SchoolId, ExamSessionId, StudentId)` | Un dossier candidat par session; réutilisable seulement avant qu’un dossier historique ne soit opposable | Index unique sans filtre | aucun test identifié |
| `ExamDossierConfiguration` — `exam_dossiers(SchoolId, ExamSessionId, CandidateNumber)` | Numéro candidat non nul dans une session; garder le numéro une fois attribué | `WHERE CandidateNumber IS NOT NULL` | aucun test identifié pour cette contrainte (les tests du générateur candidat ne vérifient pas l’index) |
| `FeeCategoryConfiguration` — `fee_categories(SchoolId, Name)` | Tenant, nom de catégorie de frais; réutilisable après archivage | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié |
| `FeeInstallmentConfiguration` — `fee_installments(SchoolId, FeeInstallmentPlanId, SequenceNo)` | Séquence d’échéance dans un plan; historique permanent après génération | Index unique sans filtre | aucun test identifié |
| `FeeInstallmentPlanConfiguration` — `fee_installment_plans(SchoolId, EnrollmentId)` | Un plan actif par inscription; ancien plan clos peut coexister | `WHERE Status = 'Active'` | aucun test identifié pour l’index actif |
| `GradeAgeNormConfiguration` — `grade_age_norms(SchoolId, GradeLevel)` | Référence tenant/niveau; réutilisable après archivage | `WHERE NOT IsDeleted` | aucun test identifié pour l’index |
| `GradeConfiguration` — `grades(SchoolId, StudentId, SubjectId, TermId, EvaluationType, IsDeleted)` | Une saisie par identité d’évaluation; une note publiée n’est pas réutilisable | Clé inclut `IsDeleted` — **exclu du soft delete (décision métier)**, inchangé | `tests/SamaEcole.IntegrationTests/Grades/GradeConcurrencyTests.cs` — `A_Concurrent_First_Entry_On_The_Same_Key_Should_Be_Refused_With_A_Conflict` |
| `InventoryCategoryConfiguration` — `inventory_categories(SchoolId, Name)` | Catégorie tenant; réutilisable après archivage | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié |
| `InventoryItemConfiguration` — `inventory_items(SchoolId, Code)` | Code article facultatif; réutilisable après archivage | `WHERE Code IS NOT NULL AND IsDeleted = false` | aucun test identifié pour cette unicité |
| `MatriculeSequenceConfiguration` — `matricule_sequences(SchoolId, Kind, Year)` | Compteur technique par tenant/type/année; clé permanente du séquenceur | Index unique sans filtre | `tests/SamaEcole.IntegrationTests/Matricules/MatriculeGeneratorTests.cs` — `Concurrent_Generations_Should_Never_Produce_A_Duplicate` (séquence générée, pas collision directe de l’index) |
| `MentionConfiguration` — `mentions(SchoolId, Label)` | Mention tenant; réutilisable si aucune référence historique ne l’exige | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié pour l’index |
| `PasswordResetTokenConfiguration` — `password_reset_tokens(TokenHash)` | Hash global de jeton technique; unicité tant que conservé, puis purge | Index unique sans filtre | aucun test identifié pour l’index |
| `PaymentConfiguration` — `payments(SchoolId, ReceiptNumber)` | Numéro de reçu tenant; permanent et immuable | Index unique sans filtre | aucun test identifié pour collision de numéro |
| `PaymentConfiguration` — `payments(SchoolId, IdempotencyKey)` | Clé de rejeu tenant; permanente pour une écriture consommée | Index unique sans filtre | `tests/SamaEcole.IntegrationTests/Finance/PaymentIdempotencyTests.cs` — `Retrying_With_The_Same_Idempotency_Key_Does_Not_Create_A_Duplicate` |
| `PromoCodeConfiguration` — `promo_codes(Code)` | Code global de promotion; réutilisation après désactivation à arbitrer | Index unique sans filtre | aucun test identifié pour la contrainte unique |
| `FichePaieConfiguration` — `fiche_paies(SchoolId, EmployeeContractId, Month, Year)` | Fiche de paie par contrat/période; registre permanent | Index unique sans filtre | aucun test identifié |
| `RefreshTokenConfiguration` — `refresh_tokens(TokenHash)` | Hash global de jeton technique; unicité tant que conservé, puis purge | Index unique sans filtre | aucun test identifié pour l’index |
| `SchoolConfiguration` — `schools(NationalSchoolCode)` | Code national global non nul; ne pas réattribuer après fermeture sans décision administrative | `WHERE NationalSchoolCode IS NOT NULL` | aucun test identifié pour cet index |
| `RoomConfiguration` — `rooms(SchoolId, BuildingId, Name)` | Nom de salle dans un bâtiment tenant; réutilisable après archivage | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié |
| `ReportCardRemarkConfiguration` — `report_card_remarks(SchoolId, StudentId, TermId, IsDeleted)` | Une appréciation par élève/période; conserver si bulletin publié | Clé inclut `IsDeleted` — **exclu du soft delete (décision métier)**, inchangé | aucun test identifié |
| `SchoolRegistrationRequestConfiguration` — `school_registration_requests(TrackingReference)` | Référence globale de suivi; permanente pour audit et recherche | Index unique sans filtre | aucun test identifié pour l’index |
| `SchoolSettingsConfiguration` — `school_settings(SchoolId)` | Singleton de configuration par tenant; permanent | Index unique sans filtre | aucun test identifié pour l’index |
| `SchoolYearConfiguration` — `school_years(SchoolId, Label)` | Tenant/libellé d’année; réutilisable si l’année archivée est vide | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié pour cet index |
| `SchoolYearConfiguration` — `school_years(SchoolId)` | Une année active par tenant | `WHERE IsActive AND NOT IsDeleted` | aucun test identifié |
| `StudentMutationCertificateConfiguration` — `student_mutation_certificates(SchoolId, CertificateNumber)` | Numéro de certificat tenant; permanent après émission | Index unique sans filtre | aucun test identifié pour collision d’index |
| `StudentMutationCertificateConfiguration` — `student_mutation_certificates(VerificationCode)` | Code public global de vérification; permanent/non réattribuable | Index unique sans filtre | aucun test identifié pour l’index |
| `StudentConfiguration` — `students(SchoolId, Matricule)` | Matricule tenant; permanent, ne pas réattribuer | Index unique sans filtre | `tests/SamaEcole.IntegrationTests/Matricules/MatriculeGeneratorTests.cs` — `Concurrent_Generations_Should_Never_Produce_A_Duplicate` (générateur) |
| `StudentConfiguration` — `students(SchoolId, IenNumber)` | IEN non nul, unique par école (pas nationalement); conserver après archivage | `WHERE IenNumber IS NOT NULL` | aucun test identifié pour l’index |
| `StudentAttendanceConfiguration` — `student_attendances(AttendanceSheetId, StudentId)` | Une ligne par élève/feuille d’appel; historique de séance | Index unique sans filtre | aucun test identifié pour cette contrainte |
| `StudentSubjectEnrollmentConfiguration` — `student_subject_enrollments(StudentId, ClassSubjectId, SchoolYearId)` | Choix actif d’option par élève/année; réutilisable après révocation | `WHERE NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/ClassSubjects/ClassSubjectsTests.cs` — `Changing_A_Students_Option_Soft_Deletes_The_Previous_Choice` (cycle de vie; pas test direct de collision d’index) |
| `StudentSubjectExemptionConfiguration` — `student_subject_exemptions(SchoolId, StudentId, SubjectId, SchoolYearId)` | Dispense active; réutilisable après révocation | `WHERE NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/Exemptions/StudentExemptionsApiTests.cs` — `A_Soft_Deleted_Row_Can_Be_Recreated` |
| `SubjectCoefficientOverrideConfiguration` — `subject_coefficient_overrides(SchoolYearId, SubjectId, ClassroomId)` | Une surcharge de portée classe; réutilisable après révocation | `WHERE ClassroomId IS NOT NULL AND NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/Coefficients/CoefficientOverrideIsolationTests.cs` — `A_Classroom_Override_Is_Unique_Per_Year_And_Subject` |
| `SubjectCoefficientOverrideConfiguration` — `subject_coefficient_overrides(SchoolYearId, SubjectId, Series)` | Une surcharge de portée série; réutilisable après révocation | `WHERE Series IS NOT NULL AND NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/Coefficients/CoefficientOverrideIsolationTests.cs` — `A_Series_Override_Is_Unique_Per_Year_And_Subject_Until_It_Is_Soft_Deleted` |
| `SubscriptionConfiguration` — `subscriptions(SchoolId)` | Un abonnement plateforme par établissement; gérer par statut, pas suppression | Index unique sans filtre | aucun test identifié pour l’index |
| `SubjectConfiguration` — `subjects(SchoolId, Level, ParentSubjectId, Name)` | Nom de matière dans portée tenant/niveau/parent; réutilisable après archivage | `WHERE NOT IsDeleted`, `NULLS NOT DISTINCT` | aucun test identifié pour la collision sur même portée |
| `SubscriptionPaymentConfiguration` — `subscription_payments(ProviderTransactionRef)` | Référence fournisseur non nulle, globale et permanente | `WHERE ProviderTransactionRef IS NOT NULL` | aucun test identifié pour la collision d’index |
| `LateArrivalConfiguration` — `late_arrivals(StudentId, TargetScheduleSlotId, Date)` | Billet actif par élève/cours/date; `Cancelled` libère la clé | `WHERE Status IN ('Issued','Accepted') AND NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/VieScolaire/EntryTicketSchemaTests.cs` — `Only_One_Active_Ticket_Exists_Per_Student_Slot_And_Day`; `A_Cancelled_Ticket_Frees_The_Key_And_Tickets_Without_A_Slot_Never_Conflict` |
| `TeacherAttendanceConfiguration` — `teacher_attendances(SchoolId, TeacherId, Date)` | Une présence active par enseignant/jour tenant; réutilisable après archivage | `WHERE IsDeleted = false` | aucun test identifié pour l’index |
| `SyllabusUnitConfiguration` — `syllabus_units(SchoolId, SubjectId, GradeLevel, Title)` | Intitulé d’unité par matière/niveau; réutilisable après archivage | `WHERE NOT IsDeleted` | aucun test identifié pour collision d’index |
| `TaxeDeclarationConfiguration` — `taxe_declarations(SchoolId, Month, Year)` | Déclaration tenant/période; rectification par nouvelle procédure, pas effacement | Index unique sans filtre | `tests/SamaEcole.IntegrationTests/Finance/GenerateTaxDeclarationCommandTests.cs` — `A_Duplicate_Declaration_For_The_Same_Period_Is_Rejected` |
| `TeacherConfiguration` — `teachers(SchoolId, Matricule)` | Matricule enseignant tenant; permanent, ne pas réattribuer | Index unique sans filtre | aucun test identifié pour collision de matricule |
| `TeacherConfiguration` — `teachers(UserId)` | Compte lié au plus à une fiche enseignant; non nul | `WHERE UserId IS NOT NULL` | aucun test identifié |
| `TeacherSubjectConfiguration` — `teacher_subjects(TeacherId, SubjectId)` | Qualification matière/enseignant; révoquer logiquement, conserver pour bulletins | ✅ `WHERE NOT "IsDeleted"` (migration `RevokeDeleteOnTeacherSubjects`) | `TeacherSubjectUnassignmentTests` (`Partial_Index_Allows_Several_Tombstones_But_Only_One_Active_Link`, révocation avec tombstone), `TeacherSubjectHistoryTests` |
| `TermConfiguration` — `terms(SchoolId, SchoolYearId, Order)` | Rang de période dans une année; réutilisable après archivage | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié pour cet index |
| `TeacherAssignmentConfiguration` — `teacher_assignments(TeacherId, ClassroomId, SubjectId, SchoolYearId)` | Affectation active tenant/année; réutilisable après retrait | `WHERE NOT IsDeleted` | `tests/SamaEcole.IntegrationTests/Teachers/TeacherAssignmentLifecycleTests.cs` — `Two_Simultaneous_Assignments_Should_Not_Create_A_Duplicate`; `Reassigning_After_A_Removal_Should_Be_Allowed` |
| `UserConfiguration` — `users(Email)` | Identité globale insensible à la casse; courriel libéré par soft delete suivant le contrat d’authentification, conflit/restauration explicite requis | `WHERE IsDeleted = false` | `tests/SamaEcole.UnitTests/Auth/ChangeUserEmailCommandHandlerTests.cs` — `A_Duplicate_Email_Caught_By_The_Precheck_Should_Also_Return_A_409_Conflict`; `A_Duplicate_Detected_Only_By_The_Database_Should_Propagate_As_A_409_Conflict` |
| `WeeklyHourNormConfiguration` — `weekly_hour_norms(SchoolId, GradeLevel, Series, SubjectId)` | Réglage tenant/niveau/série/matière; réutilisable après archivage; NULLS NOT DISTINCT pour la portée sans série | `WHERE NOT IsDeleted`, `NULLS NOT DISTINCT` | `tests/SamaEcole.IntegrationTests/HourVolumes/HourVolumesTests.cs` — `The_Grade_Wide_Setting_Is_Unique_Even_Without_A_Series` |
| `UserSchoolConfiguration` — `user_schools(UserId, SchoolId)` | Association compte/école; réutilisable après révocation | ✅ `WHERE "IsDeleted" = false` (migration `SoftDeleteUniqueIndexesAreLiveRowsOnly`) | aucun test identifié |

Les autres contraintes uniques peuvent être portées par des clés alternatives/FK du modèle plutôt que par un index `IsUnique()` de configuration; les 60 lignes ci-dessus sont le périmètre exact des déclarations uniques de configuration/snapshot. Les migrations historiques qui créent ou remplacent ces index ne sont pas des index additionnels au modèle courant. Elles sont à contrôler individuellement dans les migrations de déploiement, sans modifier les migrations déjà appliquées.
### Contraintes uniques via clés alternatives (distinctes des 60 index)

| Configuration | Contrainte | Portée / règle | Test direct |
|---|---|---|---|
| `AttendanceSheetConfiguration` | Clé alternative `(SchoolId, Id)` | Cible de FK composite tenant vers la feuille; identité technique permanente | aucun test identifié |
| `FeeCategoryConfiguration` | Clé alternative `(SchoolId, Id)` | Cible de FK composite tenant vers catégorie de frais; référence à conserver tant que des lignes d’inscription y renvoient | aucun test identifié |
| `RoomConfiguration` | Clé alternative `(SchoolId, Id)` | Cible de FK composite tenant vers salle; référence à conserver tant que l’inventaire y renvoie | aucun test identifié |
## 4. Suppressions, cascades et commandes

### Call sites d’écriture dans `SamaEcole.Application`

Relevé exhaustif au niveau des fichiers/call sites repérés par `SoftDelete(`, `.Remove(`, `RemoveRange(` et `ExecuteDelete` dans `src/SamaEcole.Application`. Au relevé initial il existait un seul `.Remove` physique (`TeacherSubjects.Remove`, ✅ remplacé par `SoftDelete`) ; il n'en reste **aucun** dans `src`, ni `RemoveRange`/`ExecuteDelete`. Les appels `SoftDelete` sont des mises à jour EF (l’état utilisateur exact varie entre commande d’archivage, retrait relationnel et mise à jour d’une liste).

| Fichier / commande | Entité et opération observée | Classification / règle historique |
|---|---|---|
| `Coefficients/Commands/DeleteCoefficientOverrideCommand.cs` | `SubjectCoefficientOverride.SoftDelete` | Référence de configuration révocable; restauration ID explicite. |
| `ClassSubjects/StudentOptionWriter.cs` | Soft delete des choix d’option retirés | Historique de choix conservé; la nouvelle option ne restaure pas implicitement l’ancienne. |
| `Buildings/Commands/DeleteBuilding/DeleteBuildingCommandHandler.cs` | `Building.SoftDelete` | Référence modifiable; bloquer/désactiver si un parent historique dépend du bâtiment. |
| `ClassJournal/Commands/DeleteClassJournalEntry/DeleteClassJournalEntryCommandHandler.cs` | `ClassJournalEntry.SoftDelete` | Registre pédagogique; conserver si consulté historiquement. |
| `ClassSubjects/Commands/SetStudentExemptionsCommand.cs` | Soft delete des dispenses absentes de la liste remplacée | Révocation logique; index actif libère clé, restauration ciblée nécessaire. |
| `ClassSubjects/ClassSubjectTemplateInjector.cs` | Soft delete de surcharge de classe remplacée lors de l’injection du programme | Référence de configuration; conserver tombstone et contrôler les dépendances. |
| `Classrooms/Commands/DeleteClassroom/DeleteClassroomCommandHandler.cs` | `Classroom.SoftDelete` après refus si élèves actifs | Bloquer tant qu’élève actif attaché; historique conservé. |
| `Features/Schedules/DeleteScheduleSlotCommand.cs` | `ScheduleSlot.SoftDelete` | Configuration temporelle; ne pas casser appels/billets historiques. |
| `Features/Disbursements/DeleteDisbursementCommand.cs` | ✅ Contre-écriture (`ReversalOfId`, montants opposés) — plus de `SoftDelete` | Écriture financière : jamais supprimée ni masquée ; annulation compensatrice, une seule par original. |
| `Teachers/Commands/UpdateTeacher/UpdateTeacherCommandHandler.cs` | ✅ `TeacherSubject.SoftDelete` (ancien `.Remove` physique supprimé) | Relation révocable conservée pour les bulletins ; aucune réactivation d'un ancien tombstone. |
| `Grades/Commands/DeleteMention/DeleteMentionCommand.cs` | `Mention.SoftDelete` | Référence de barème; préserver le libellé/historique de bulletins publiés. |
| `Teachers/Commands/UnassignTeacher/UnassignTeacherCommandHandler.cs` | `TeacherAssignment.SoftDelete` | Révocation logique; préserver affectation historique et restaurer ID ou proposer conflit de tombstone. |
| `Grades/Commands/DeleteGrade/DeleteGradeCommandHandler.cs` | `Grade.SoftDelete` | Note supprimée logiquement; une note publiée/émise doit être corrigée par flux audité, pas effacée. |
| `Teachers/Commands/DeleteTeacher/DeleteTeacherCommandHandler.cs` | `Teacher.SoftDelete` | Parent désactivé; préserver paie, heures, bulletins et affectations. |
| `Inventory/Commands/DeleteInventoryItem/DeleteInventoryItemCommandHandler.cs` | `InventoryItem.SoftDelete` | Bloquer l’archivage s’il y a mouvements/prêts; sinon archiver le parent. |
| `Inventory/Commands/DeleteInventoryCategory/DeleteInventoryCategoryCommandHandler.cs` | `InventoryCategory.SoftDelete` | Bloquer si articles historiques liés; sinon archiver sans cascade. |
| `Syllabus/SyllabusCommands.cs` | `SyllabusUnit.SoftDelete` | Référence d’enseignement; conserver lien aux journaux historiques, restaurer identité si réutilisée. |
| `Syllabus/JournalUnitLinker.cs` | Soft delete des liens unité/entrée retirés | Historique de suivi conservé; rétablissement via association précise. |
| `Finance/Commands/DeleteFeeCategory/DeleteFeeCategoryCommandHandler.cs` | Soft delete des ClassFees puis FeeCategory | Bloquer/archiver parent si frais d’inscriptions/paiements existent; ne pas effacer d’écritures financières. |
| `Finance/Commands/DeleteClassFee/DeleteClassFeeCommandHandler.cs` | `ClassFee.SoftDelete` | Barème modifiable seulement avant consommation; lignes d’inscription figées et paiements conservés. |
| `Inventory/Commands/CancelItemAssignment/CancelItemAssignmentCommandHandler.cs` | `ItemAssignment.SoftDelete` | Annuler par état logique; conserver décharge et mouvements associés. |
| `Rooms/Commands/DeleteRoom/DeleteRoomCommandHandler.cs` | `Room.SoftDelete` | Bloquer/désactiver si patrimoine ou historique d’usage dépend de la salle. |
| `Subjects/Commands/DeleteSubject/DeleteSubjectCommandHandler.cs` | `Subject.SoftDelete` | Bloquer si sous-matière ou historique scolaire lié; conserver pour notes/bulletins. |
| `Institutional/Commands/AgeNormCommands.cs` | Soft delete d’une norme d’âge remplacée | Configuration versionnée/réactivable, sans affecter les élèves existants. |
| `HourVolumes/HourVolumeCommands.cs` | Soft delete d’une norme hebdomadaire remplacée | Configuration par état; aucun effacement des horaires historiques. |
| `Students/Commands/DeleteStudent/DeleteStudentCommandHandler.cs` | `Student.SoftDelete` | Bloquer ou désactiver selon dépendances; paiements, bulletins, certificats et prêts restent. |
| `SchoolYears/Commands/ApplyEvaluationPeriods/ApplyEvaluationPeriodsCommandHandler.cs` | Soft delete des termes remplacés | Refuser si évaluations/bulletins historiques dépendants; les termes historiques restent résolubles. |
| `SchoolYears/Commands/DeleteSchoolYear/DeleteSchoolYearCommandHandler.cs` | Soft delete année vide, puis termes et affectations associés | Année avec données (même tombstones) bloquée; année vide désactivée/archivée. La branche pré-live/test appelle purge maintenance décrite ci-dessous. |

Les chemins ci-dessus ne comprennent pas la révocation de jetons de sécurité ni un handler `SubmitRegistrationRequest` : le balayage actuel ne trouve aucun `.Remove` dans ce handler. Les commandes d’annulation d’inscription/paiement et de clôture de caisse utilisent des statuts/mouvements métier, pas une suppression physique. `StockMovement`, paiement, reçu et audit n’ont aucun endpoint de suppression repéré; le stock se corrige par mouvement inverse.

### SQL direct, cascades EF/FK et grants

Les suppressions SQL des migrations sont des opérations de maintenance/migration ou fonctions contrôlées, pas des commandes utilisateur. Les fonctions `reset_school_data` (migration initiale et migrations correctives) exécutent une liste ordonnée de `DELETE FROM table WHERE SchoolId = $1`, puis des suppressions de comptes/tokens de personnel lors d’un reset d’école de test. Les migrations `AddSchoolYearDeletion` et `AddSubjectCoefficientOverridesToPurges`/`AddStudentSubjectExemptionsToPurges` ont des fonctions de purge par année en mode pré-live; `AddClassSubjectsAndOptions` retire des choix invalides pendant la migration. Ces SQL ne constituent pas des suppressions restaurables. Les tests exécutent également SQL destructif pour préparer/nettoyer leurs fixtures et vérifier ACL/RLS; aucun n’est un call site Application.

> ✅ **Tous les privilèges `DELETE` ci-dessous sont révoqués** par `20261004034236_RevokeDeleteOnBusinessTables` (hors liste blanche technique : `refresh_tokens`, `password_reset_tokens`, `matricule_sequences`), et vérifiés à l'exécution par `ImmutableRegistersTests`. La colonne « Traitement » est l'analyse initiale.

| Cible ayant un `DELETE` accordé explicitement dans les migrations | Migration et objet | Traitement |
|---|---|---|
| Chaque table de `public` existante au passage de migration | `20260714023704_EnableRowLevelSecurity` — `GRANT ... DELETE ON ALL TABLES IN SCHEMA public` | Portée dynamique réelle à la date de migration; doit être révoquée par une migration corrective, puis privilèges réaccordés au moindre droit. La migration ne cible pas les tables créées ensuite. |
| `classrooms` | `20260714125827_AddClassrooms` — `GRANT ... DELETE ON classrooms` | Retirer le privilège applicatif; utiliser l’archivage par UPDATE. |
| `refresh_tokens` | `20260714043511_AddAuthentication` | Table technique: si purge applicative requise, exception strictement limitée à cette table. |
| `password_reset_tokens` | `20260721111925_AddPasswordResetTokens` | Table technique éphémère; exception bornée, sans endpoint de restauration. |
| `Disbursements`, `ScheduleSlots` | `20260724014300_AddScheduleAndDisbursements` — boucle des deux tables | Révoquer sur le registre financier; slot métier utilise soft delete. |
| `employee_contracts`, `fiche_paies`, `taxe_declarations` | `20260724182209_AddPayrollAndTax` — boucle des trois tables | Révoquer; paie/déclaration sont des registres conservés. |
| `teacher_subjects` | `20260830045159_GrantDeleteOnTeacherSubjects` — GRANT explicite, rollback REVOKE | Supprimer le grant et implémenter le retrait logique prévu par la conception. |

Aucune autre chaîne de migration ne contient un GRANT explicite avec `DELETE`; la liste ci-dessus provient du balayage de toutes les migrations (`GRANT ... DELETE`, `GRANT DELETE ON`, `REVOKE DELETE ON`). Cela ne suffisait pas à prouver les privilèges effectifs (propriétaire, rôle hérité, privilèges par défaut) : l'audit **à l'exécution** de §0.3 a confirmé cette liste et trouvé en plus `students`, `users`, `schools`, `subscriptions`, `matricule_sequences` et `__EFMigrationsHistory`. Vérifier les ACL réellement installées sur une base cible; le grant `ALL TABLES` initial rend les seuls grants ultérieurs sans DELETE insuffisants comme preuve de révocation.

Les configurations/designer EF contiennent plusieurs `ON DELETE CASCADE` historiques, principalement pour lignes dépendantes et tables ajoutées en migrations. Toute suppression métier du parent pourrait cascader physiquement; ne jamais appeler `Remove` sur ces parents. Lors de l’implémentation, auditer les FKs exactes de chaque agrégat et passer à `RESTRICT/NO ACTION` si l’historique doit survivre. Le modèle contient aussi des `RESTRICT`, notamment pour notes, examens, inventaire; ceux-ci bloquent un delete physique mais ne remplacent pas les contrôles de domaine.
## 5. `IgnoreQueryFilters()` et isolation tenant

Le filtre global combine tenant et soft delete; `IgnoreQueryFilters()` retire les deux. Chaque cas doit donc affirmer `SchoolId` explicitement lorsqu’il lit des entités tenant. RLS demeure active à moins d’utiliser le rôle propriétaire/superutilisateur, ce qui est interdit à l’application.

| Emplacement/parcours | Motif et portée | Verdict |
|---|---|---|
| `ApplicationDbContext.GetGlobalAuditLogsAsync` | Forme sans clé, résultat de fonction SECURITY DEFINER inter-tenant; `schoolId` est un filtre administratif explicite, aucun filtre EF sur cette forme | Hors tenant par design; API Super Admin et fonction bornée au besoin. |
| `ApplicationDbContext.VerifyMutationCertificateAsync` | Fonction SECURITY DEFINER publique, projection minimale par code; pas de ligne élève ni détails privés | Sans tenant utilisateur; exposer seulement résultat minimal. |
| `DeleteSchoolYearCommandHandler.EnsureYearCarriesNoDataAsync` | Ignore les tombstones pour compter tout historique avant suppression; chaque requête réimpose `SchoolId == year.SchoolId` puis année/IDs; RLS active | Correctement borné et nécessaire pour éviter de purger une année contenant déjà des tombstones. |
| `AssignTeacherCommandHandler` | Aucun `IgnoreQueryFilters` sur vérification d’affectation; utilise seulement identités actives | Bon pour unicité active, mais manque de recherche du tombstone pour conflit/restauration demandé par conception. |
| `MatriculeGenerator`, `NationalIenGenerator` | Contournent filtre pour voir entité/valeur tenant lors de génération transactionnelle; condition explicite sur le SchoolId courant | Vérifier dans chaque expression le `SchoolId` et identité historique; RLS reste seconde barrière. Les valeurs matricule/IEN ne doivent pas être réattribuées si attestées. |
| `DbSeeder` | Ignore le filtre pour retrouver des données de seed existantes dans le tenant; filtre explicite school dans chaque requête | Correct si utilisé seulement lors du seed avec RLS/tenant courant; pas parcours utilisateur ni corbeille. |
| `GetPromoCodesQuery`, `TopUpSmsCreditsCommand`, `SmsDispatcher` | Chemins Super Admin/plateforme sans tenant; contournent filtre pour `Subscription`/`SchoolSettings`/SMS crédits | Hors tenant voulu, accès doit rester réservé aux handlers plateforme; les requêtes sous SchoolId explicite si données tenant croisées. |
| Tests `DeleteSchoolYearTests`, `ResetSchoolDataTests`, `SyllabusTrackingTests`, `SubscriptionAdminStoreTests`, et tests ciblés token/attendance | Lecture propriétaire/test ou validation SQL; souvent filtre retiré volontairement | Test-only. Toute lecture multi-tenant propriétaire vérifie SchoolId/écoles attendues. Certains appels interrogent expressément absence physique après purge: ne pas confondre avec une query de restauration. |

Un balayage des `IgnoreQueryFilters` dans le snapshot/code repère aussi des occurrences de test et commentaires. Toute nouvelle occurrence d’application exige justification, filtre `SchoolId` explicite si tenant, et revue de rôle/RLS. Aucun parcours de corbeille/restauration applicatif complet n’est présent dans le modèle relevé.

## 6. Dépendances historiques: bloquer ou désactiver le parent

| Parent / donnée | Effet immuable ou historique | Règle de suppression |
|---|---|---|
| Élève | inscriptions, reçus/paiements, notes, bulletins, appels, certificats de mutation, mouvements/prêts | Bloquer si cela détruirait/rendrait inintelligible une écriture; sinon désactiver élève et conserver liens. Ne jamais cascade-delete. Restaurer l’élève par ID. |
| Enseignant | affectations de classe/matière, pointages/heures, fiches de paie, bulletins passés | Désactiver le profil; conserver affectations historiques et paie. Relation matière révocable, historisée. |
| Classe/salle/bâtiment | élèves, horaires, appels, patrimoine/prêts | Classe: bloquer tant qu’élèves actifs attachés (commande existante); après départ, soft delete. Salle/bâtiment: désactiver seulement si aucun usage historique courant; pas de cascade. |
| Matière/terme/année | notes, bulletins publiés, feuilles d’appel, dossiers d’examen, certificats, frais et paiements | Année réelle avec données: bloquer (commande actuelle). Données historiques gardées; désactiver année vide. Matière/terme référencés: garder pour résolution historique, ne pas effacer. |
| Catégorie/frais/inscription/échéance | paiements, lignes de frais gelées, reçus, historique de modification | Bloquer l’effacement une fois payé/validé; désactiver ou versionner la référence. Annulation se fait par opération compensatrice. |
| Article/prêt | mouvements append-only et fiches de décharge | Désactiver article si un mouvement/prêt existe; clôturer prêt par état; jamais supprimer mouvement. |
| Utilisateur | événements audit, historique de statut, paiements et validation d’opérations | Désactiver compte/révocation de sessions; conserver auteur historique. Pas de suppression de l’audit. |

Les lectures historiques des bulletins doivent charger les affectations et relations enseignant-matière de la période, y compris celles révoquées. Un filtre d’actifs seul ne suffit pas; requête historique dédiée doit borner `SchoolId` et critères temporels tout en conservant RLS.

## 7. Restauration et portée utilisateur

Les identités restaurables sont les références réutilisables et associations dont une nouvelle création sous la même identité doit déclencher `ARCHIVED_ENTITY_EXISTS` : classes, bâtiments/salles, matières, catégories/frais, mentions, unités/normes et associations (dont enseignant-matière). Chaque identité de ce groupe doit présenter une action « Restaurer » dans la corbeille, ou un lien direct vers l’élément supprimé. L’API de restauration doit accepter l’ID tombstone, vérifier tenant/role, dépendances, puis vérifier l’unicité active atomiquement; conflit `ACTIVE_ENTITY_CONFLICT` si la clé est reprise. Une création ne doit jamais réactiver silencieusement ni fusionner.

Pour `Student`, `Teacher`, `SchoolYear`, `Enrollment` et les autres données opérationnelles, restauration seulement si la règle d’agrégat l’autorise et si aucune donnée ne devient incohérente. Un reçu, paiement, audit, mouvement de stock, certificat émis, déclaration/paie ou token consommé n’a pas de commande de restauration. Une demande de rétablissement du parent ne modifie aucune écriture immuable.

**Écart initial (partiellement ✅ corrigé, voir §0.1) :** le code examiné ne fournissait pas de commande générale de corbeille/restauration. Elle existe désormais pour `Building`, `Classroom`, `Room`, `FeeCategory`, `Mention` et `SchoolYear` (avec `Term`/`TeacherAssignment`) ; les autres identités restaurables listées ci-dessus restent ⏳ à couvrir. Aucun écran de corbeille n'existe encore (API seule). `AssignTeacher` laisse actuellement une même identité réutilisable après révocation, mais ne détecte pas explicitement le tombstone pour offrir sa restauration. Les anciennes clés avec `IsDeleted` dans les colonnes limitent à une seule ligne supprimée historique.

## 8. Doublons actifs et précontrôle de migration

Les migrations précédentes de conversion d’index (`MakeSubjectsUniqueIndexPartial`, cas d’email utilisateur, index option/exemption/override/attribution/entrée) montrent des contrôles/corrections propres à ces changements, mais l’inventaire du code ne peut pas mesurer l’état d’une base réelle. Aucune liste fiable de doublons actifs n’est disponible sans interroger chaque environnement cible.

Avant migration PR1, exécuter un précontrôle transactionnel pour chaque nouvel index partiel et ses colonnes normalisées (ex. `citext`, `COALESCE`, prédicats statut/NULL). Sortie : table, colonnes/clés en conflit, nombres/IDs utiles minimaux. Si collision : arrêt explicite avant `DROP/CREATE`; aucune fusion, suppression, sélection arbitraire ou réécriture par la migration. Arbitrage manuel approuvé avant reprise.

## 9. Vérification et limites de ce relevé

- Inventaire croisé avec les types `Domain/Entities`, `DbSet`/configurations EF, snapshot et migrations; les projections sans clé sont séparées.
- Les index uniques sont repris des `IsUnique()` et des filtres explicites présents dans les configurations; revue ponctuelle des migrations historiques confirme qu’elles comportent des index/grants de purge et des exceptions anciennes.
- Les opérations applicatives `Remove`, `RemoveRange`, `ExecuteDelete`, les SQL directs et `IgnoreQueryFilters` ont été recherchés. Les migrations générées répétées pour `reset_school_data` sont groupées par fonction/table cible pour éviter de présenter chaque version identique comme une nouvelle commande métier.
- Le relevé initial (sections 1 à 8) provient des sources. Le §0 est, lui, **vérifié par exécution** : privilèges réels sur une base migrée, restauration, conflits, isolation tenant et immuabilité sous le rôle applicatif (tests `SoftDelete/*` d'intégration PostgreSQL). Cela ne prouve pas l'état d'une base de production : les doublons actifs et les ACL effectives de chaque environnement cible restent à contrôler avant migration (les migrations contiennent un précontrôle qui s'arrête sans rien modifier).
- La stratégie API et tests de la conception PR1 s’applique : conflit explicite `ARCHIVED_ENTITY_EXISTS`, restauration ciblée, conflit `ACTIVE_ENTITY_CONFLICT`, isolation multi-tenant sur recherche tombstone et restauration, index partiels uniquement pour identités métier réutilisables.
