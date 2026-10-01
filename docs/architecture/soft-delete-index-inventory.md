# Inventaire — cycles de vie, suppressions et unicité

**Date de relevé :** 2026-10-01  
**Objet :** PR1 « Soft Delete global et index uniques » (conception du 2026-10-01).  
**Sources examinées :** `AGENTS.md`, volumes DDS/API/Sécurité/Stratégie de tests (3, 4, 7, 8), types `Domain/Entities`, configurations EF, `ApplicationDbContext` et `ApplicationDbContextModelSnapshot`, migrations historiques, handlers/queries applicatifs et tests d’intégration/unitaires. Ce document décrit le modèle et le code présents, puis fixe le traitement attendu; il ne prétend pas connaître les données des bases déployées.

## 1. Règles de lecture

- Le filtre `ApplicationDbContext.SetTenantFilter` s’applique à chaque `AuditableEntity, ITenantEntity` et combine `SchoolId == CurrentSchoolId && !IsDeleted`. Il n’y a pas de mécanisme partagé dans `SaveChanges` qui transforme un `Remove` arbitraire en soft delete; les handlers doivent appeler `SoftDelete(...)` explicitement.
- Les données métier tenant restent sous filtre EF et RLS PostgreSQL. Les exemptions au filtre sont détaillées en §5; elles ne désactivent jamais la RLS côté base.
- « Réutilisable » signifie que l’identité peut être reprise après suppression logique; la cible est un index partiel sur les seules lignes actives. « Permanente » signifie que la clé historique reste réservée, même si un enregistrement est annulé/archivé. Les clés techniques et relations de jonction sont classées selon leur invariant, pas converties mécaniquement.
- Aucun dédoublonnage de production ne peut être déduit du code. Les collisions actives doivent être mesurées avant migration et rapportées sans correction automatique.

## 2. Entités persistées et classification

Les entités persistées ci-dessous proviennent des `DbSet` et des entités découverts par les configurations / snapshot (les formes de résultat sans clé sont exclues). La classification est la cible de cycle de vie; les observations signalent les écarts connus au modèle actuel.

| Entité(s) persistée(s) | Classe | Suppression / restauration et dépendances |
|---|---|---|
| `School`, `SchoolSettings` | Référence modifiable, globale ou configuration tenant | `School` désactivation/archivage seulement si des données historiques la référencent; ne pas cascader sur historique. Réglages : suppression bloquée, modification en place. Pas de corbeille utilisateur pour l’établissement plateforme. |
| `User`, `UserSchool` | Référence modifiable d’identité / association d’accès | Désactiver le compte et révoquer les associations; conserver l’audit et l’historique de statut. Restaurer par identifiant si le rôle/association est réactivable, après contrôle d’unicité et autorisation. `UserSchool` actuellement unique avec `IsDeleted` dans les colonnes, donc plusieurs tombstones ne sont pas possibles. |
| `Student`, `Teacher` | Référence modifiable, supprimable sous condition | Archiver si aucune donnée historique ne l’interdit; préserver inscriptions, notes, paiements/reçus et mouvements. Restaurer par ID; matricules/IEN: voir §3. |
| `Classroom`, `Building`, `Room`, `Subject`, `FeeCategory`, `ClassFee`, `Mention`, `SchoolYear`, `Term`, `GradeAgeNorm`, `WeeklyHourNorm`, `SyllabusUnit`, `SchoolSettings` | Références/configurations modifiables | Corbeille/restauration par ID. Bloquer la suppression si des données immuables/historiques sont liées; sinon désactiver parent et garder les enfants. L’année scolaire porte une règle dédiée en §4. Les clés métier réutilisables doivent être libérées par index actif seulement. |
| `ClassSubject`, `StudentSubjectEnrollment`, `StudentSubjectExemption`, `SubjectCoefficientOverride` | Association/configuration révocable ou modifiable | Révocation logique; restaurer l’association précise si possible. Les contraintes uniques sont des identités actives (à vérifier: plusieurs indices existants sont permanents). |
| `TeacherSubject` | Association révocable (identifiée explicitement par la conception) | Soft delete, conserver l’historique enseignant-matière pour consultation des bulletins. L’actuel `TeacherSubjectConfiguration` est unique sans filtre et l’ancienne migration accorde explicitement `DELETE` : deux écarts majeurs à corriger. Restauration explicite; ne pas rendre l’association introuvable aux lectures historiques. |
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

La table ci-dessous reprend chaque `IsUnique()` de configuration au relevé et les contraintes uniques/partielles matérialisées par le snapshot. « Test » nomme la couverture repérée; `à compléter` signifie que l’invariant n’a pas été identifié comme directement testé dans la recherche ciblée. Les noms SQL physiques varient selon les migrations; la configuration/snapshot fait foi pour le modèle courant.

| Table / identité (configuration) | Portée et règle | État / cible PR1 | Couverture repérée |
|---|---|---|---|
| `AttendanceSheets` `(SchoolId, ClassroomId, SubjectId, Date, Period)` | Tenant; une feuille d’appel par classe/matière/date/période | Identité de séance, à conserver (historique); suppression logique peut libérer seulement si séance annulée et règle documentée | tests appels/attendance; confirmer cas doublon |
| `ClassSubjects` `(ClassroomId, SubjectId)` | Classe; matière programme | Unique partiel actif en migration récente; conserver filtre `NOT IsDeleted` | tests ClassSubjects/options |
| `ClassFees` `(SchoolId, FeeCategoryId, ClassroomId, IsDeleted)` | Tenant/classe/catégorie; clé active encodée dans les colonnes | Écart: supprimer `IsDeleted` des colonnes, garder clé active via filtre partiel | tests ClassFee/pricing à vérifier |
| `Buildings` `(SchoolId, Name, IsDeleted)` | Tenant; nom bâtiment | Écart de forme: prédicat actif, pas `IsDeleted` dans clé | tests bâtiments à vérifier |
| `Classrooms` `(SchoolId, Name, IsDeleted)` | Tenant; nom classe | Écart de forme: filtre actif; contrainte delete bloque tant qu’élèves actifs rattachés | `DeleteClassroomTests`, tests classe |
| `CashierSessions` `(SchoolId, CashierId)` | Tenant; une caisse ouverte par caissier, filtre `ClosedAt IS NULL` | Unicité d’état ouverte; ne pas convertir mécaniquement en filtre IsDeleted | tests CashierSession |
| `ClassJournalEntryUnits` `(ClassJournalEntryId, SyllabusUnitId)` | Entrée; une unité couverte une fois | Historique d’enseignement; confirmer réemploi après suppression logique | tests SyllabusTracking |
| `ExamResults` `(ExamDossierId)` | Dossier; un résultat final par dossier | Identité du résultat officiel; permanente après émission | tests ExamResult |
| `Enrollments` `(SchoolId, ReceiptNumber)` | Tenant; numéro reçu d’inscription | Identifiant de reçu immuable/permanent | tests Enrollment/receipt |
| `Enrollments` `(SchoolId, StudentId, SchoolYearId)` | Tenant/année/élève; inscription annuelle | Identité réutilisable? Les annulations avec paiement restent historiques: définir explicitement, probablement permanent par année si reçu émis; sinon filtre actif | tests Enrollment |
| `EmployeeContracts` `TeacherId` filtré `IS NOT NULL AND EndDate IS NULL` | Globalement unique sur contrat en cours du professeur | Prédicat d’état métier; conserver sans filtre tombstone automatique | tests contract lifecycle |
| `EmployeeContracts` `UserId` filtré `IS NOT NULL AND EndDate IS NULL` | Globalement unique sur contrat en cours du compte | Prédicat d’état métier; conserver | tests contract lifecycle |
| `FeeInstallments` `(SchoolId, FeeInstallmentPlanId, SequenceNo)` | Tenant/plan; séquence d’échéance | Historique du plan; permanent après génération | tests installments |
| `FeeInstallmentPlans` `(SchoolId, EnrollmentId)` | Tenant/inscription; plan unique | Réutilisable seulement si plan annulé sans paiement; définir + éventuellement actif | tests installment plan |
| `FeeCategories` `(SchoolId, Name, IsDeleted)` | Tenant; nom catégorie de frais | Écart: index partiel actif plutôt que `IsDeleted` en clé | tests FeeCategory |
| `ExamDossiers` `(SchoolId, ExamSessionId, StudentId)` | Tenant/session; dossier candidat | Identité d’inscription à examen, réemploi après annulation à définir; index partiel si réutilisable | tests ExamDossier |
| `ExamDossiers` `(SchoolId, ExamSessionId, CandidateNumber)` | Tenant/session; numéro candidat | Numéro attribué, conserver historique de session; ne pas réattribuer après publication | tests ExamDossier |
| `FichePaies` `(SchoolId, EmployeeContractId, Month, Year)` | Tenant/contrat/période | Écriture de paie permanente; pas suppression | tests payroll |
| `GradeAgeNorms` `(SchoolId, GradeLevel)` | Tenant/niveau scolaire | Référence modifiable; actif seulement si historiques ne dépendent pas, sinon versionner | tests âge à vérifier |
| `Grades` `(SchoolId, StudentId, SubjectId, TermId, EvaluationType, IsDeleted)` | Tenant/élève/évaluation | Écart de forme, mais suppression/restauration de note publiée interdite; définir statut de brouillon/publiée avant filtre | tests Grades |
| `InventoryCategories` `(SchoolId, Name, IsDeleted)` | Tenant; catégorie | Écart de forme: filtre partiel actif | tests inventory |
| `InventoryItems` `(SchoolId, Code)` | Tenant; code patrimoine | Filtrer actif seulement si code réutilisable métier; sinon identifiant historique permanent | tests inventory |
| `MatriculeSequences` `(SchoolId, Kind, Year)` | Tenant; compteur technique | Permanente par cycle; conserver | tests matricule |
| `Mentions` `(SchoolId, Label, IsDeleted)` | Tenant; mention de bulletin | Écart de forme: filtre partiel actif; ancienne version doit rester résoluble pour bulletins émis | tests grading scale |
| `PasswordResetTokens` `(TokenHash)` | Global/token éphémère | Unique permanent tant que ligne conservée; purge technique possible | tests auth |
| `Payments` `(SchoolId, ReceiptNumber)` | Tenant; reçu émis | Unicité permanente, jamais libérée par suppression | tests payments/receipt |
| `Payments` `(SchoolId, IdempotencyKey)` | Tenant; idempotence | Unicité permanente du rejeu consommé; jamais libérée | tests idempotency |
| `PromoCodes` `(Code)` | Plateforme; code de promotion | Réutilisation après désactivation à décider explicitement; filtre actif si réutilisable | tests promo |
| `RefreshTokens` `(TokenHash)` | Global/token technique | Unicité permanente de token hash tant qu’enregistré; purge technique | tests auth |
| `ReportCardRemarks` `(SchoolId, StudentId, TermId, IsDeleted)` | Tenant/élève/période | Écart de forme; note d’appréciation liée au bulletin publié = historiser, sinon clé active filtrée | tests report cards |
| `SchoolRegistrationRequests` `(TrackingReference)` | Plateforme | Référence de suivi permanente | tests onboarding |
| `Schools` `NationalSchoolCode` | Plateforme/global | Réutilisable seulement si établissement fermé selon arbitrage; aucun effacement automatique | tests provisioning |
| `SchoolYears` `(SchoolId, Label, IsDeleted)` | Tenant; libellé année | Écart de forme: index partiel actif; année utilisée doit être désactivée plutôt que supprimée | tests SchoolYears |
| `SchoolYears` `(SchoolId)` filtré année active | Tenant; une seule année active | Prédicat d’état, garder indépendamment d’IsDeleted | tests SchoolYears |
| `Rooms` `(SchoolId, BuildingId, Name, IsDeleted)` | Tenant/bâtiment; nom local | Écart de forme: index partiel actif | tests infrastructure |
| `StudentAttendances` `(AttendanceSheetId, StudentId)` | Feuille/élève; ligne d’appel unique | Enregistrement historique; correction par état/audit, pas doublon | tests attendance |
| `SchoolSettings` `(SchoolId)` | Tenant; réglage singleton | Permanent (une configuration par école) | tests settings |
| `StudentSubjectExemptions` `(SchoolId, StudentId, SubjectId, SchoolYearId)` | Tenant/année/élève/matière | Partiel `NOT IsDeleted`, libérer à la révocation | tests exemptions |
| `TeacherAssignments` `(SchoolId, TeacherId, ClassroomId, SubjectId, SchoolYearId)` | Tenant/année; attribution | Partiel actif déjà présent; lecture des bulletins conserve historique | tests TeacherAssignments |
| `StudentSubjectEnrollments` `(StudentId, ClassSubjectId, SchoolYearId)` | Élève/année/programme | Partiel actif; une option retirée libère l’identité | tests options |
| `TaxeDeclarations` `(SchoolId, Month, Year)` | Tenant/période fiscale | Registre permanent, correction par déclaration rectificative | tests taxe |
| `Subscriptions` `(SchoolId)` | Plateforme; une souscription par école | Singleton, désactivation par statut uniquement | tests subscription |
| `SubjectCoefficientOverrides` `(SchoolYearId, SubjectId, ClassroomId)` | Année/matière/classe | Partiel actif, classe OU série | tests coefficients |
| `SubjectCoefficientOverrides` `(SchoolYearId, SubjectId, Series)` | Année/matière/série | Partiel actif, classe OU série | tests coefficients |
| `SyllabusUnits` `(SchoolId, SubjectId, GradeLevel, Title)` | Tenant/matière/niveau | Référence réutilisable après archivage si non référencée; index actif à envisager | tests syllabus |
| `Users` `Email` (`citext`) | Global; compte | Déjà partiel `IsDeleted=false`; identité supprimée doit provoquer conflit/restauration ou email réutilisable selon politique; ne jamais créer silencieusement | tests user/auth |
| `StudentMutationCertificates` `(SchoolId, CertificateNumber)` | Tenant; certificat émis | Identité permanente/immuable | tests certificate |
| `StudentMutationCertificates` `VerificationCode` | Global/public | Code permanent, non réattribuable | tests verification |
| `LateArrivals` `(StudentId, TargetScheduleSlotId, Date)` filtré `Status IN ('Issued','Accepted') AND NOT IsDeleted` | Billet actif du jour/cours | Partiel métier correct; annulation libère clé | tests entry ticket |
| `LateArrivals` `(SchoolId, TeacherId, Date)` filtré `IsDeleted=false` | Tenant; absence/événement enseignant | Partiel actif déjà appliqué | tests teacher attendance |
| `TeacherSubjects` `(TeacherId, SubjectId)` | Association | Actuellement permanente et migration accorde DELETE; convertir en suppression logique + filtre actif; lectures historiques gardent les lignes supprimées | tests teacher subject |
| `SubscriptionPayments` `ProviderTransactionRef` | Global transaction fournisseur | Permanente, immuable | tests subscription webhook |
| `Terms` `(SchoolId, SchoolYearId, Order, IsDeleted)` | Tenant/année; ordre période | Écart de forme: filtre partiel actif; année supprimée n’efface pas bulletin | tests school-year periods |
| `WeeklyHourNorms` `(SchoolId, Level, ...)` | Tenant/niveau/année selon configuration | Référence de norme; filtre actif si réutilisable | tests weekly norms |

Entrées complémentaires relevées lors de la comparaison exacte des 60 appels `.IsUnique()` de configuration (la table ci-dessus en résumait plusieurs mais pas toutes les colonnes) :

| Table / identité (configuration) | Portée et règle | État / cible PR1 | Couverture repérée |
|---|---|---|---|
| `ClassJournalEntryUnits` `(ClassJournalEntryId, SyllabusUnitId)` | Lien entrée/unité | Partiel `NOT IsDeleted` déjà configuré; plusieurs tombstones autorisés; préserver les leçons consultées | tests `SyllabusTrackingTests` |
| `Subjects` `(SchoolId, Level, ParentSubjectId, Name)` `NULLS NOT DISTINCT` | Tenant/niveau/parent; nom de matière, parent nul signifie matière racine | Partiel `NOT IsDeleted` déjà configuré; `NULLS NOT DISTINCT` conserve l’unicité des racines; création avec tombstone doit proposer restauration | tests Subjects |
| `Students` `(SchoolId, Matricule)` | Tenant; matricule d’élève | Permanent/non réattribuable après génération; séquence n’est pas décrémentée par suppression | tests génération matricule |
| `Students` `(SchoolId, IenNumber)` avec `IenNumber IS NOT NULL` | Tenant; IEN | Unicité par école (pas nationale); conserve sa valeur même si élève archivé | tests IEN |
| `Teachers` `(SchoolId, Matricule)` | Tenant; matricule enseignant | Permanent/non réattribuable | tests enseignants |
| `Teachers` `UserId` avec `UserId IS NOT NULL` | Global; un compte de connexion lié au plus à une fiche enseignant | Filtre de NULL seulement, pas suppression; une fiche archivée ne devrait pas autoriser le rattachement du même compte à une seconde identité sans arbitrage | tests teacher account |
| `UserSchools` `(UserId, SchoolId, IsDeleted)` | Association utilisateur/école | Écart de forme: supprimer `IsDeleted` de la clé, index partiel actif; aujourd’hui une seule ligne supprimée historique par association | tests memberships |
| `WeeklyHourNorms` `(SchoolId, GradeLevel, Series, SubjectId)` `NULLS NOT DISTINCT`, filtre `NOT IsDeleted` | Norme de volume horaire tenant, niveau/série/matière | Index partiel actif existant; NULL représente la portée toutes séries et reste unique | tests weekly norms |
| `Schools` `NationalSchoolCode` avec `NationalSchoolCode IS NOT NULL` | Global plateforme, code national | Identité officielle; conserver l’unicité; le code d’une école désactivée ne doit pas être attribué à une autre sans décision administrative | tests provisioning |
| `CashierSessions` `(SchoolId, CashierId)` avec `Status = 'Open'` | Tenant/caissier; une caisse ouverte | Contrainte d’état, pas réutilisation de tombstone; fermeture libère la clé | tests caisse |
| `ExamDossiers` `(SchoolId, ExamSessionId, CandidateNumber)` avec numéro non nul | Tenant/session; numéro de table/candidat | Identité de session, le filtre de NULL permet plusieurs candidats non encore numérotés; garder numéros déjà attribués si dossier archivé | tests examens |
| `LateArrivals` `(StudentId, TargetScheduleSlotId, Date)` avec statut `Issued|Accepted` et `NOT IsDeleted` | Billet actif par élève, cours et date | Partiel métier déjà présent; `Cancelled` libère la clé | tests `EntryTicketDtoTests` et billet |
| `TeacherAttendances` `(SchoolId, TeacherId, Date)` avec `IsDeleted=false` | Tenant; une fiche de présence active/jour | Partiel actif déjà présent; conserver correction/historique | tests présence enseignants |
| `SchoolYears` `(SchoolId)` avec `IsActive=true AND NOT IsDeleted` | Tenant; une année active | Contrainte d’état; conserver, l’archivage doit d’abord désactiver | `SchoolYearPeriodsTests`, `DeleteSchoolYearTests` |
| `TeacherAssignments` `(TeacherId, ClassroomId, SubjectId, SchoolYearId)` avec `NOT IsDeleted` | Association d’affectation active | Index partiel existant; offre actuellement une recréation sans recherche de tombstone | tests `TeacherAssignment` |
| `StudentSubjectEnrollments` `(StudentId, ClassSubjectId, SchoolYearId)` avec `NOT IsDeleted` | Choix d’option | Index partiel existant; révoquer puis recréer libère le choix | tests options |
| `StudentSubjectExemptions` `(SchoolId, StudentId, SubjectId, SchoolYearId)` avec `NOT IsDeleted` | Dispense active | Index partiel existant; restauration explicite du tombstone requise | tests exemptions |
| `SubjectCoefficientOverrides` `(SchoolYearId, SubjectId, ClassroomId)` avec portée de classe non nulle et `NOT IsDeleted` | Surcharge par classe | Index partiel existant; clé active, portée exclusive via CHECK | tests coefficient overrides |
| `SubjectCoefficientOverrides` `(SchoolYearId, SubjectId, Series)` avec série non nulle et `NOT IsDeleted` | Surcharge par série | Index partiel existant; clé active, portée exclusive via CHECK | tests coefficient overrides |

Les index `unique: true` portés par migrations anciennes mais non présents dans les configurations/snapshot courants sont historiques et ne doivent pas être traités comme une nouvelle règle. Chaque migration corrective doit comparer sa cible au snapshot actuel; une migration déjà appliquée n’est jamais réécrite.

## 4. Suppressions, cascades et commandes

| Parcours/commande ou chemin | Effet observé | Règle PR1 / restauration |
|---|---|---|
| `DeleteClassroomCommandHandler` (`DELETE /classrooms/{id}`) | Vérifie qu’aucun élève actif n’est rattaché, puis `SoftDelete(actor)` sur classe | Bloquer si élève actif; préserver historique d’élèves supprimés et autres références. Restaurer la classe ciblée avec conflit si nom repris. |
| `UpdateTeacherCommandHandler` — retrait de `TeacherSubject` | `TeacherSubjects.Remove(toRemove)` (suppression physique EF); configuration unique sans filtre | Remplacer par révocation logique et historique; restaurer une relation supprimée par ses clés/ID; consulter l’historique pour bulletins. |
| `DeleteSchoolYearCommandHandler` | Mode réel : vérifie toute donnée, incluant tombstones, bloque si inscription/note/appréciation/appel/examen/certificat; année vide archivée, termes/affectations logiquement supprimés. Mode test/pré-live : service appelle fonction SQL de purge limitée | Mode réel conforme à « désactiver le parent »; la fonction SQL est exception destructive explicitement limitée au nettoyage d’école non-live/test, pas un endpoint de suppression métier ordinaire. Année active de remplacement choisie. |
| `SubmitRegistrationRequestHandler` | `Remove` d’une requête de provisioning dans une branche de rollback/échec transactionnel (à ne pas confondre avec commande utilisateur de suppression métier) | Vérifier le callsite/transaction lors de changement; garder rollback atomique, pas d’effacement d’une demande déjà acceptée. |
| Création/affectation de professeur | `AssignTeacherCommandHandler` recherche l’affectation active sans `IgnoreQueryFilters`; crée si absente; index partiel `TeacherAssignment` | Tombstone n’empêche pas réaffectation; la nouvelle ligne ne restaure pas l’ancienne silencieusement. Il faut conflit `ARCHIVED_ENTITY_EXISTS` et restauration explicite si l’identité métier doit être conservée. |
| Authentification | `AuthStore` / migrations et procédures révoquent/retirent refresh/password reset tokens | Technique uniquement; purge/révocation autorisée, sans restauration utilisateur. |
| `reset_school_data` via `ResetSchoolDataService` | Fonction PostgreSQL de maintenance supprime les données saisies d’une école, y compris tombstones, après contrôle du `SchoolId` de session; opération transactionnelle, journalisée; interdit en mode live par fonctions guardées. | Exception maintenance explicite, pas de corbeille. Conserver audit, configuration choisie et RLS. Réviser les listes de tables dans les migrations `AddSchoolDataReset` et suites avant extension du modèle. |
| `delete_school_year` / migrations historiques | SQL direct supprime paiements, reçus/détails, lignes financières, évaluations, certificats, etc. pour années en mode pré-live; migrations ultérieures ajoutent tables oubliées. | Exception de nettoyage hors production seulement. En mode réel bloquer dès qu’une écriture historique existe. Les migrations déjà livrées ne sont pas modifiées. |
| Cascades EF/FK `ON DELETE CASCADE` | Plusieurs relations historiques utilisent Cascade dans configurations/designer; les parents principaux sont souvent `RESTRICT` | Une future commande métier ne doit jamais appeler `Remove` sur parent et déclencher cascade. Remplacer les cascades sur tout parent soft-delete par `RESTRICT/NO ACTION` ou gérer l’état explicite; auditer les FK exactes en migration de mise en œuvre. |
| `RemoveRange`, `ExecuteDelete`, SQL `DELETE` dans Application | Recherche ciblée ne révèle aucun `ExecuteDelete` applicatif. Seuls `Remove` ci-dessus observés. `ResetSchoolDataService` exécute une fonction SQL, pas un DELETE client EF. | Garder toute suppression métier hors `DELETE` physique. Ajouter instrumentation/test de garde en PR1 si prévue par plan. |

### SQL direct, migrations et permissions `DELETE`

Les suppressions SQL des migrations sont des opérations de maintenance/migration ou fonctions contrôlées, pas des commandes utilisateur. Il existe plusieurs générations de migration de `reset_school_data` (`AddSchoolDataReset`, correctifs de tables manquantes, mode live, classements, Coran, exemptions, etc.) qui portent des tableaux `DELETE FROM %I WHERE SchoolId = $1`; elles ont aussi des chemins distincts supprimant `user_schools`, tokens et comptes de personnel. `AddSchoolYearDeletion` et migrations d’extension contiennent une purge ciblée par année. `AddStudentSubjectEnrollments` contient une suppression de données de liaison au cours de la migration de schéma. Tests exécutent du SQL destructif uniquement pour valider le refus/grants ou nettoyer le jeu de test.

| Table / cible de grant DELETE relevée | Source / portée | Décision |
|---|---|---|
| Toutes tables existantes du schéma `public` | `EnableRowLevelSecurity`: grant initial `SELECT, INSERT, UPDATE, DELETE ON ALL TABLES`; c’est une autorisation large historique | Revoquer les grants applicatifs `DELETE` explicitement dans une nouvelle migration, puis n’accorder que les besoins d’exception documentés. RLS seule ne rend pas ce privilège sûr. |
| `classrooms` et tables de la migration `AddClassrooms` | grant `SELECT, INSERT, UPDATE, DELETE` historique sur la liste de tables de cette migration | Retirer `DELETE`; suppression métier = update soft delete. |
| `refresh_tokens` | `AddAuthentication`: grant CRUD complet; techniquement purgable/révocable | Autoriser une exception technique minimale si nécessaire, mais vérifier qu’aucun rôle applicatif large ne reçoit suppression sur le reste. |
| `password_reset_tokens` | `AddPasswordResetTokens`: grant CRUD complet; technique éphémère | Exception technique limitée à cette table; purge non destinée aux utilisateurs. |
| tables de `AddScheduleAndDisbursements` | boucle de grant incluant DELETE | Examiner la liste en migration pour chaque table; retirer DELETE des données métier, garder exception uniquement si table technique autorisée. |
| tables de `AddPayrollAndTax` | boucle de grant incluant DELETE | Paie/taxes immuables: révoquer DELETE. |
| `teacher_subjects` | `GrantDeleteOnTeacherSubjects` accorde explicitement `DELETE`; rollback révoque | Supprimer ce grant et remplacer l’opération physique par suppression logique; la décision précédente est incompatible avec la conception actuelle. |
| `stock_movements` | pas de grant DELETE; la configuration/migration documente `SELECT, INSERT` | Conserver append-only, mouvement inverse comme correction. |
| `payments`, `subscription_payments`, `enrollments`, `grades`, `teachers` et tables métier ajoutées ultérieurement | commentaires/migrations indiquent grants sans DELETE ou `SELECT,INSERT,UPDATE` | Confirmer dans les grants réels et modèle final que l’ancien grant global n’est pas resté actif; les commentaires seuls ne révoquent pas le droit initial. |

**Limite du relevé de grants :** le grant initial sur toutes tables rend les GRANT ponctuels ultérieurs insuffisants pour prouver l’absence de DELETE. Il faut comparer privilèges effectifs PostgreSQL après migrations, pas seulement rechercher les chaînes `GRANT` dans les migrations. Ce contrôle dépend d’une base migrée et n’a pas été exécuté ici.

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

**Écart actuel :** le code examiné ne fournit pas de commande générale de corbeille/restauration; les suppressions sont hétérogènes. `AssignTeacher` laisse actuellement une même identité réutilisable après révocation, mais ne détecte pas explicitement le tombstone pour offrir sa restauration. Les anciennes clés avec `IsDeleted` dans les colonnes limitent à une seule ligne supprimée historique.

## 8. Doublons actifs et précontrôle de migration

Les migrations précédentes de conversion d’index (`MakeSubjectsUniqueIndexPartial`, cas d’email utilisateur, index option/exemption/override/attribution/entrée) montrent des contrôles/corrections propres à ces changements, mais l’inventaire du code ne peut pas mesurer l’état d’une base réelle. Aucune liste fiable de doublons actifs n’est disponible sans interroger chaque environnement cible.

Avant migration PR1, exécuter un précontrôle transactionnel pour chaque nouvel index partiel et ses colonnes normalisées (ex. `citext`, `COALESCE`, prédicats statut/NULL). Sortie : table, colonnes/clés en conflit, nombres/IDs utiles minimaux. Si collision : arrêt explicite avant `DROP/CREATE`; aucune fusion, suppression, sélection arbitraire ou réécriture par la migration. Arbitrage manuel approuvé avant reprise.

## 9. Vérification et limites de ce relevé

- Inventaire croisé avec les types `Domain/Entities`, `DbSet`/configurations EF, snapshot et migrations; les projections sans clé sont séparées.
- Les index uniques sont repris des `IsUnique()` et des filtres explicites présents dans les configurations; revue ponctuelle des migrations historiques confirme qu’elles comportent des index/grants de purge et des exceptions anciennes.
- Les opérations applicatives `Remove`, `RemoveRange`, `ExecuteDelete`, les SQL directs et `IgnoreQueryFilters` ont été recherchés. Les migrations générées répétées pour `reset_school_data` sont groupées par fonction/table cible pour éviter de présenter chaque version identique comme une nouvelle commande métier.
- Ce document n’a exécuté ni projet ni test d’intégration et ne prétend pas que les privilèges réels ou les doublons de production sont vérifiés. Docker étant indisponible, le relevé se limite aux sources; les contrôles dépendant d’une base restent exigés avant migration.
- La stratégie API et tests de la conception PR1 s’applique : conflit explicite `ARCHIVED_ENTITY_EXISTS`, restauration ciblée, conflit `ACTIVE_ENTITY_CONFLICT`, isolation multi-tenant sur recherche tombstone et restauration, index partiels uniquement pour identités métier réutilisables.
