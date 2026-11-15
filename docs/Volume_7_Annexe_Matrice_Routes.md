# Volume 7 — Annexe : matrice des droits par route API

> **Fichier généré** par `tests/SamaEcole.FunctionalTests/Security/PermissionMatrixTests.cs` à partir des attributs des
> contrôleurs — ne pas l'éditer à la main. Un changement de droits sans régénération fait échouer les tests.
> Régénérer : `UPDATE_PERMISSION_MATRIX=1 dotnet test tests/SamaEcole.FunctionalTests --filter PermissionMatrix`.

Lecture : **Rôles** = rôles autorisés (une intersection `A ∩ B` signale une garde de classe et une garde de méthode
cumulées : il faut satisfaire les deux). « Tout utilisateur authentifié » = `[Authorize]` sans rôle ; le serveur borne
alors par la RLS et les règles du Handler. **Condition** = module activé, formule d'abonnement ou délégation exigée
en plus du rôle. Les routes anonymes sont listées pour mémoire (connexion, webhooks signés, annuaire public).
Le détail fonctionnel par module reste dans la matrice du §15 de `Volume_7_Security.md`.

## Absence

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/absences/arrival-preview` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/absences/early-departures` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| POST | `/api/v1/absences/early-departures` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/absences/justifications` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| POST | `/api/v1/absences/justifications` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/absences/late-arrivals` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| POST | `/api/v1/absences/late-arrivals` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/absences/today-slots` | SuperAdmin, Directeur, Surveillant | module Pédagogie |

## Account

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/abonnement/paiement` | _anonyme_ |  |
| GET | `/inscription` | _anonyme_ |  |
| GET | `/login` | _anonyme_ |  |
| GET | `/mot-de-passe-oublie` | _anonyme_ |  |
| GET | `/reinitialiser-mot-de-passe` | _anonyme_ |  |
| GET | `/suivi-demande` | _anonyme_ |  |

## AdminOverQuotaSchools

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/admin/over-quota-schools` | SuperAdmin |  |

## AdminPricingGrid

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/admin/pricing-grid` | SuperAdmin |  |
| PUT | `/api/v1/admin/pricing-grid/{tier}` | SuperAdmin |  |

## AdminRegistrationRequests

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/admin/registration-requests` | SuperAdmin |  |
| POST | `/api/v1/admin/registration-requests/{id:guid}/approve` | SuperAdmin |  |
| POST | `/api/v1/admin/registration-requests/{id:guid}/reject` | SuperAdmin |  |

## AdminSubscriptionChangeRequests

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/admin/subscription-change-requests` | SuperAdmin |  |
| POST | `/api/v1/admin/subscription-change-requests/{id:guid}/approve` | SuperAdmin |  |
| POST | `/api/v1/admin/subscription-change-requests/{id:guid}/reject` | SuperAdmin |  |

## Attendance

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/attendance` | Enseignant, Directeur, Secretariat, Surveillant | module Pédagogie |
| GET | `/api/v1/attendance/roster` | Enseignant, Directeur, Secretariat, Surveillant | module Pédagogie |
| GET | `/api/v1/attendance/slots` | Enseignant, Directeur, Secretariat, Surveillant | module Pédagogie |
| GET | `/api/v1/attendance/{id:guid}` | Directeur, Secretariat, SuperAdmin, Surveillant | module Pédagogie |

## AuditLogs

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/audit-logs` | Directeur |  |

## Auth

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/auth/change-email` | Directeur, SuperAdmin |  |
| POST | `/api/v1/auth/change-password` | Tout utilisateur authentifié |  |
| POST | `/api/v1/auth/forgot-password` | _anonyme_ |  |
| POST | `/api/v1/auth/login` | _anonyme_ |  |
| POST | `/api/v1/auth/logout` | Tout utilisateur authentifié |  |
| GET | `/api/v1/auth/my-schools` | Tout utilisateur authentifié |  |
| POST | `/api/v1/auth/refresh` | _anonyme_ |  |
| POST | `/api/v1/auth/reset-password` | _anonyme_ |  |
| POST | `/api/v1/auth/switch-school` | Tout utilisateur authentifié |  |

## Billets

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/billets/early-departure/{earlyDepartureId:guid}` | SuperAdmin, Directeur, Secretariat, Surveillant | module Pédagogie |
| GET | `/api/v1/billets/early-departure/{earlyDepartureId:guid}/pdf` | SuperAdmin, Directeur, Secretariat, Surveillant | module Pédagogie |
| GET | `/api/v1/billets/late-arrival/{lateArrivalId:guid}` | SuperAdmin, Directeur, Secretariat, Surveillant | module Pédagogie |
| GET | `/api/v1/billets/late-arrival/{lateArrivalId:guid}/pdf` | SuperAdmin, Directeur, Secretariat, Surveillant | module Pédagogie |

## Boarding

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/boarding/assign-bed` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/attendance` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/attendance` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/attendance/groups` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/attendance/monthly-summary` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/attendance/monthly-summary/pdf` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/attendance/session-templates` | Directeur | module Internat |
| GET | `/api/v1/boarding/attendance/sessions` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/attendance/sessions` | Directeur | module Internat |
| PUT | `/api/v1/boarding/attendance/sessions/order` | Directeur | module Internat |
| DELETE | `/api/v1/boarding/attendance/sessions/{id:guid}` | Directeur | module Internat |
| PUT | `/api/v1/boarding/attendance/sessions/{id:guid}` | Directeur | module Internat |
| GET | `/api/v1/boarding/attendance/sheet` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/attendance/sheet` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/available-rooms` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/beds` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/beds/deleted` | Directeur, Secretariat | module Internat |
| DELETE | `/api/v1/boarding/beds/{id:guid}` | Directeur, Secretariat | module Internat |
| POST | `/api/v1/boarding/beds/{id:guid}/restore` | Directeur, Secretariat | module Internat |
| PUT | `/api/v1/boarding/beds/{id:guid}/status` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/boardable-students` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/boarders` | Directeur, Secretariat, Surveillant | module Internat |
| DELETE | `/api/v1/boarding/boarders/{id:guid}` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/boarders/{id:guid}` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/boarders/{id:guid}/pdf` | Directeur, Secretariat, Surveillant | module Internat |
| PUT | `/api/v1/boarding/boarders/{id:guid}/profile` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/deletion-impact` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/deletions` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/boarding/dormitories` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/dormitories` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/dormitories/deleted` | Directeur, Secretariat | module Internat |
| DELETE | `/api/v1/boarding/dormitories/{id:guid}` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/dormitories/{id:guid}` | Directeur, Secretariat, Surveillant | module Internat |
| PUT | `/api/v1/boarding/dormitories/{id:guid}` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/dormitories/{id:guid}/register-pdf` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/dormitories/{id:guid}/restore` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/leaves` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/leaves` | Directeur, Secretariat, Surveillant | module Internat |
| DELETE | `/api/v1/boarding/leaves/{id:guid}` | Directeur, Secretariat, Surveillant | module Internat |
| PUT | `/api/v1/boarding/leaves/{id:guid}/return` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/boarding/rooms` | Directeur, Secretariat | module Internat |
| GET | `/api/v1/boarding/rooms/deleted` | Directeur, Secretariat | module Internat |
| DELETE | `/api/v1/boarding/rooms/{id:guid}` | Directeur, Secretariat | module Internat |
| PUT | `/api/v1/boarding/rooms/{id:guid}` | Directeur, Secretariat | module Internat |
| POST | `/api/v1/boarding/rooms/{id:guid}/restore` | Directeur, Secretariat | module Internat |
| DELETE | `/api/v1/boarding/unassign-bed/{boarderId:guid}` | Directeur, Secretariat, Surveillant | module Internat |

## Buildings

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/buildings` | Tout utilisateur authentifié |  |
| POST | `/api/v1/buildings` | Directeur, Secretariat |  |
| GET | `/api/v1/buildings/deleted` | Directeur, Secretariat |  |
| DELETE | `/api/v1/buildings/{id:guid}` | Directeur, Secretariat |  |
| PUT | `/api/v1/buildings/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/buildings/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| POST | `/api/v1/buildings/{id:guid}/restore` | Directeur, Secretariat |  |
| POST | `/api/v1/buildings/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |

## ClassJournal

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/class-journal` | Directeur, Secretariat, Surveillant, Enseignant | module Pédagogie |
| POST | `/api/v1/class-journal` | Enseignant | module Pédagogie |
| DELETE | `/api/v1/class-journal/{id:guid}` | Enseignant, Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/class-journal/{id:guid}` | Enseignant, Directeur, Secretariat | module Pédagogie |

## ClassSubjects

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/class-subjects` | Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/class-subjects` | Directeur | module Pédagogie |
| POST | `/api/v1/class-subjects/assign-default-options` | Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/class-subjects/options` | Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/class-subjects/reset` | Directeur | module Pédagogie |
| GET | `/api/v1/class-subjects/students/{studentId:guid}/exemptions` | Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/class-subjects/students/{studentId:guid}/exemptions` | Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/class-subjects/students/{studentId:guid}/options` | Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/class-subjects/{id:guid}` | Directeur | module Pédagogie |

## Classrooms

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/classrooms` | Tout utilisateur authentifié |  |
| POST | `/api/v1/classrooms` | Directeur, Secretariat |  |
| GET | `/api/v1/classrooms/deleted` | Directeur, Secretariat |  |
| DELETE | `/api/v1/classrooms/{id:guid}` | Directeur, Secretariat |  |
| PUT | `/api/v1/classrooms/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/classrooms/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| POST | `/api/v1/classrooms/{id:guid}/restore` | Directeur, Secretariat |  |
| GET | `/api/v1/classrooms/{id:guid}/school-cards` | Directeur, Secretariat |  |
| POST | `/api/v1/classrooms/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |

## Coefficients

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/coefficients` | Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/coefficients` | Directeur | module Pédagogie |
| POST | `/api/v1/coefficients/apply-template` | Directeur | module Pédagogie |
| POST | `/api/v1/coefficients/carry-over` | Directeur | module Pédagogie |
| GET | `/api/v1/coefficients/catalog` | Directeur, Secretariat | module Pédagogie |
| DELETE | `/api/v1/coefficients/{id:guid}` | Directeur | module Pédagogie |

## CurrentSchool

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/schools/current` | Tout utilisateur authentifié |  |
| PUT | `/api/v1/schools/current` | Directeur |  |
| POST | `/api/v1/schools/current/go-live` | Directeur |  |
| POST | `/api/v1/schools/current/lock-production` | Directeur |  |
| POST | `/api/v1/schools/current/logo` | Directeur |  |
| GET | `/api/v1/schools/current/mode` | Tout utilisateur authentifié |  |
| POST | `/api/v1/schools/current/reset-data` | Directeur |  |
| POST | `/api/v1/schools/current/revert-to-test` | Directeur |  |

## DevPaymentSimulation

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/dev/paydunya-checkout/{reference}` | _anonyme_ |  |

## Disbursements

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/finance/disbursements` | Directeur, Finance, SuperAdmin | module Finance |
| POST | `/api/v1/finance/disbursements` | Directeur, Finance, SuperAdmin | module Finance |
| DELETE | `/api/v1/finance/disbursements/{id:guid}` | Directeur, Finance, SuperAdmin | module Finance |

## Discipline

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/discipline` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| POST | `/api/v1/discipline` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/discipline/{id:guid}/pv` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/discipline/{id:guid}/pv/pdf` | SuperAdmin, Directeur, Surveillant | module Pédagogie |

## Enrollments

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/enrollments` | Directeur, Secretariat |  |
| DELETE | `/api/v1/enrollments/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/enrollments/{id:guid}/certificate` | Tout utilisateur authentifié |  |
| GET | `/api/v1/enrollments/{id:guid}/certificate/pdf` | Tout utilisateur authentifié |  |
| GET | `/api/v1/enrollments/{id:guid}/exeat` | Tout utilisateur authentifié |  |
| GET | `/api/v1/enrollments/{id:guid}/exeat/pdf` | Tout utilisateur authentifié |  |
| GET | `/api/v1/enrollments/{id:guid}/receipt` | Tout utilisateur authentifié |  |
| GET | `/api/v1/enrollments/{id:guid}/receipt/pdf` | Tout utilisateur authentifié |  |
| POST | `/api/v1/enrollments/{id:guid}/status` | Directeur, Secretariat |  |

## EntryTicketActions

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/billets/{id:guid}/accept` | Enseignant, Directeur | module Pédagogie |
| POST | `/api/v1/billets/{id:guid}/cancel` | Surveillant, Directeur | module Pédagogie |

## Exams

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/exams/cfee-candidates` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/dossiers` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat, Enseignant | module Pédagogie |
| POST | `/api/v1/exams/dossiers` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/dossiers/audit` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/exams/dossiers/candidate-forms/pdf` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/dossiers/{id:guid}` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat, Enseignant | module Pédagogie |
| PUT | `/api/v1/exams/dossiers/{id:guid}` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/exams/dossiers/{id:guid}/assign-center` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/dossiers/{id:guid}/candidate-form/pdf` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/dossiers/{id:guid}/convocation/pdf` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/exams/dossiers/{id:guid}/result` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/dossiers/{id:guid}/synthesis/pdf` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/exams/dossiers/{id:guid}/transmit` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/export/ministerial` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/sessions` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/exams/sessions` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/exams/sessions/{id:guid}` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/exams/sessions/{id:guid}/dispatch-convocations` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | formule Premium (SMS) ; module Pédagogie |
| GET | `/api/v1/exams/sessions/{id:guid}/pv/pdf` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/exams/statistics` | Directeur, Secretariat, Enseignant ∩ Directeur, Secretariat | module Pédagogie |

## Exports

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/exports/school-data` | Directeur |  |

## Features

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/features` | Tout utilisateur authentifié |  |

## Finance

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/finance/classrooms/{classroomId:guid}/installment-plan` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/commitments` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/commitments/{id:guid}` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/commitments/{id:guid}/pdf` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/daily-cash-register/pdf` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/dashboard` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/dues-reminder-batches` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/dues-reminder-batches/recompute` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/dues-reminder-batches/{id:guid}/dismiss` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/dues-reminder-batches/{id:guid}/send` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/employee-contracts` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts/{contractId:guid}/hour-records` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/employee-contracts/{contractId:guid}/hour-records` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts/{contractId:guid}/hour-records/sheet` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts/{contractId:guid}/hour-records/sheet/pdf` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts/{contractId:guid}/suggested-hours` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts/{contractId:guid}/work-certificate` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/employee-contracts/{contractId:guid}/work-certificate/pdf` | Directeur, Finance | module Finance |
| PATCH | `/api/v1/finance/employee-contracts/{id:guid}` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/employee-contracts/{id:guid}/close` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/enrollments/{enrollmentId:guid}/dues-notice` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/enrollments/{enrollmentId:guid}/dues-notice/pdf` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/enrollments/{enrollmentId:guid}/installment-plan` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/fee-categories` | Tout utilisateur authentifié | module Finance |
| POST | `/api/v1/finance/fee-categories` | Directeur ; Finance si délégué | délégation « la Finance peut modifier les montants de frais » ; module Finance |
| GET | `/api/v1/finance/fee-categories/deleted` | Directeur ; Finance si délégué | délégation « la Finance peut supprimer des frais » ; module Finance |
| DELETE | `/api/v1/finance/fee-categories/{id:guid}` | Directeur ; Finance si délégué | délégation « la Finance peut supprimer des frais » ; module Finance |
| PUT | `/api/v1/finance/fee-categories/{id:guid}` | Directeur ; Finance si délégué | délégation « la Finance peut modifier les montants de frais » ; module Finance |
| GET | `/api/v1/finance/fee-categories/{id:guid}/deletion-impact` | Directeur ; Finance si délégué | délégation « la Finance peut supprimer des frais » ; module Finance |
| POST | `/api/v1/finance/fee-categories/{id:guid}/restore` | Directeur ; Finance si délégué | délégation « la Finance peut supprimer des frais » ; module Finance |
| POST | `/api/v1/finance/fee-categories/{id:guid}/unlink-dependencies` | Directeur ; Finance si délégué | délégation « la Finance peut supprimer des frais » ; module Finance |
| GET | `/api/v1/finance/fees` | Tout utilisateur authentifié | module Finance |
| POST | `/api/v1/finance/fees/apply-standard` | Directeur ; Finance si délégué | délégation « la Finance peut modifier les montants de frais » ; module Finance |
| DELETE | `/api/v1/finance/fees/{id:guid}` | Directeur ; Finance si délégué | délégation « la Finance peut supprimer des frais » ; module Finance |
| PUT | `/api/v1/finance/fees/{id:guid}` | Directeur ; Finance si délégué | délégation « la Finance peut modifier les montants de frais » ; module Finance |
| GET | `/api/v1/finance/fees/{id:guid}/history` | Tout utilisateur authentifié | module Finance |
| GET | `/api/v1/finance/monthly-status` | Directeur ; Finance si accès ouvert | accès « situation des mensualités » (Finance, si le Directeur l'ouvre) ; module Finance |
| GET | `/api/v1/finance/monthly-status/excel` | Directeur ; Finance si accès ouvert | accès « situation des mensualités » (Finance, si le Directeur l'ouvre) ; module Finance |
| GET | `/api/v1/finance/monthly-status/pdf` | Directeur ; Finance si accès ouvert | accès « situation des mensualités » (Finance, si le Directeur l'ouvre) ; module Finance |
| GET | `/api/v1/finance/payments` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/payments` | Directeur, Finance, Secretariat | module Finance |
| POST | `/api/v1/finance/payments/{id:guid}/cancel` | Tout utilisateur authentifié | CanCancelPayments ; module Finance |
| GET | `/api/v1/finance/payments/{id:guid}/receipt` | Tout utilisateur authentifié | module Finance |
| GET | `/api/v1/finance/payments/{id:guid}/receipt/pdf` | Tout utilisateur authentifié | module Finance |
| GET | `/api/v1/finance/payroll` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/payroll` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/payroll/{id:guid}/cancel` | Directeur ; Finance si droit accordé | droit d'annulation des fiches de paie (Finance, si le Directeur l'accorde) ; module Finance |
| POST | `/api/v1/finance/payroll/{id:guid}/pay` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/payroll/{id:guid}/pdf` | Directeur, Finance | module Finance |
| POST | `/api/v1/finance/payroll/{id:guid}/regenerate` | Directeur ; Finance si droit accordé | droit d'annulation des fiches de paie (Finance, si le Directeur l'accorde) ; module Finance |
| GET | `/api/v1/finance/sessions/current` | Directeur, Finance, Secretariat | module Finance |
| POST | `/api/v1/finance/sessions/open` | Directeur, Finance, Secretariat | module Finance |
| POST | `/api/v1/finance/sessions/{id}/close` | Directeur, Finance, Secretariat | module Finance |
| GET | `/api/v1/finance/sessions/{id}/closing-report` | Directeur, Finance, Secretariat | module Finance |
| GET | `/api/v1/finance/students/search` | Tout utilisateur authentifié | module Finance |
| GET | `/api/v1/finance/students/{studentId:guid}/balance` | Tout utilisateur authentifié | module Finance |
| POST | `/api/v1/finance/tax-declaration` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/tax-declarations` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/tax-declarations/{id:guid}/pdf` | Directeur, Finance | module Finance |
| GET | `/api/v1/finance/treasury` | Directeur, Finance | module Finance |

## FinancialReports

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/finance/reports/debtor-aging` | Directeur, Finance | formule Standard ou supérieure (rapports avancés) ; module Finance |
| GET | `/api/v1/finance/reports/debtor-aging/excel` | Directeur, Finance | formule Standard ou supérieure (rapports avancés) ; module Finance |
| GET | `/api/v1/finance/reports/revenue` | Directeur, Finance | formule Standard ou supérieure (rapports avancés) ; module Finance |
| GET | `/api/v1/finance/reports/revenue/excel` | Directeur, Finance | formule Standard ou supérieure (rapports avancés) ; module Finance |

## Grades

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/grades` | Directeur, Secretariat, Enseignant | module Pédagogie |
| POST | `/api/v1/grades` | Directeur, Secretariat, Enseignant | module Pédagogie |
| GET | `/api/v1/grades/calculate` | Directeur, Enseignant | module Pédagogie |
| GET | `/api/v1/grades/mentions` | Directeur, Secretariat, Enseignant | module Pédagogie |
| POST | `/api/v1/grades/mentions` | Directeur ; Secretariat si délégué | délégation « le Secrétariat gère la configuration des notes » ; module Pédagogie |
| GET | `/api/v1/grades/mentions/deleted` | Directeur ; Secretariat si délégué | délégation « le Secrétariat gère la configuration des notes » ; module Pédagogie |
| DELETE | `/api/v1/grades/mentions/{id:guid}` | Directeur ; Secretariat si délégué | délégation « le Secrétariat gère la configuration des notes » ; module Pédagogie |
| PATCH | `/api/v1/grades/mentions/{id:guid}` | Directeur ; Secretariat si délégué | délégation « le Secrétariat gère la configuration des notes » ; module Pédagogie |
| POST | `/api/v1/grades/mentions/{id:guid}/restore` | Directeur ; Secretariat si délégué | délégation « le Secrétariat gère la configuration des notes » ; module Pédagogie |
| GET | `/api/v1/grades/sheet/export` | Directeur, Secretariat, Enseignant | module Pédagogie |
| POST | `/api/v1/grades/sheet/import` | Directeur, Secretariat, Enseignant | module Pédagogie |
| GET | `/api/v1/grades/sheet/print` | Directeur, Secretariat, Enseignant | module Pédagogie |
| DELETE | `/api/v1/grades/{id:guid}` | Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/grades/{id:guid}` | Directeur, Secretariat, Enseignant | module Pédagogie |

## HourVolumes

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/hour-volumes/compliance` | Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/hour-volumes/norms` | Directeur, Secretariat | module Pédagogie |
| PUT | `/api/v1/hour-volumes/norms` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| DELETE | `/api/v1/hour-volumes/norms/{id:guid}` | Directeur, Secretariat ∩ Directeur | module Pédagogie |

## Institutional

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/institutional/age-check` | Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/institutional/age-norms` | Directeur, Secretariat | module Pédagogie |
| DELETE | `/api/v1/institutional/age-norms/{gradeLevel}` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| PUT | `/api/v1/institutional/age-norms/{gradeLevel}` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| GET | `/api/v1/institutional/ief-report` | Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/institutional/ief-report/excel` | Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/institutional/ief-report/pdf` | Directeur, Secretariat | module Pédagogie |

## Internat

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/internat/assignable-students` | Directeur | module Internat |
| POST | `/api/v1/internat/attendance` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| GET | `/api/v1/internat/attendance/monthly-summary` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| GET | `/api/v1/internat/attendance/sessions` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| GET | `/api/v1/internat/attendance/sheet` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| GET | `/api/v1/internat/instructors` | Directeur, Secretariat, Surveillant | module Internat |
| POST | `/api/v1/internat/instructors` | Directeur | module Internat |
| PUT | `/api/v1/internat/instructors/{id:guid}` | Directeur | module Internat |
| GET | `/api/v1/internat/instructors/{instructorId:guid}/students` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| GET | `/api/v1/internat/my-halqa` | Enseignant | module Internat |
| GET | `/api/v1/internat/progress-dashboard` | Directeur, Secretariat, Surveillant | module Internat |
| GET | `/api/v1/internat/progress-history` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| POST | `/api/v1/internat/students/assign-instructor` | Directeur | module Internat |
| GET | `/api/v1/internat/students/{studentId:guid}/hizb-history` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| GET | `/api/v1/internat/students/{studentId:guid}/hizb-progress` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |
| PUT | `/api/v1/internat/students/{studentId:guid}/hizb-progress` | Directeur, Enseignant | module Internat |
| GET | `/api/v1/internat/students/{studentId:guid}/hizb-report/pdf` | Directeur, Secretariat, Surveillant, Enseignant | module Internat |

## Inventory

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/inventory/assignments` | Tout utilisateur authentifié |  |
| POST | `/api/v1/inventory/assignments` | Directeur, Secretariat, Surveillant |  |
| DELETE | `/api/v1/inventory/assignments/{id:guid}` | Directeur, Secretariat, Surveillant |  |
| GET | `/api/v1/inventory/assignments/{id:guid}/pdf` | Tout utilisateur authentifié |  |
| POST | `/api/v1/inventory/assignments/{id:guid}/return` | Directeur, Secretariat, Surveillant |  |
| GET | `/api/v1/inventory/categories` | Tout utilisateur authentifié |  |
| POST | `/api/v1/inventory/categories` | Directeur, Secretariat |  |
| DELETE | `/api/v1/inventory/categories/{id:guid}` | Directeur, Secretariat |  |
| PUT | `/api/v1/inventory/categories/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/inventory/categories/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| POST | `/api/v1/inventory/categories/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |
| GET | `/api/v1/inventory/items` | Tout utilisateur authentifié |  |
| POST | `/api/v1/inventory/items` | Directeur, Secretariat |  |
| DELETE | `/api/v1/inventory/items/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/inventory/items/{id:guid}` | Tout utilisateur authentifié |  |
| PUT | `/api/v1/inventory/items/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/inventory/items/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| POST | `/api/v1/inventory/items/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |
| GET | `/api/v1/inventory/movements` | Tout utilisateur authentifié |  |
| POST | `/api/v1/inventory/movements` | Directeur, Secretariat, Surveillant |  |
| GET | `/api/v1/inventory/reports/global/pdf` | Directeur, Secretariat |  |

## Onboarding

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/onboarding/select-profile` | Directeur |  |
| GET | `/api/v1/onboarding/subscription` | Tout utilisateur authentifié |  |

## ParentSummons

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/parent-summons` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| POST | `/api/v1/parent-summons` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/parent-summons/{id:guid}/notice` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| GET | `/api/v1/parent-summons/{id:guid}/notice/pdf` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| PATCH | `/api/v1/parent-summons/{id:guid}/outcome` | SuperAdmin, Directeur, Surveillant | module Pédagogie |

## PaymentWebhooks

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/webhooks/payments/{provider}` | _anonyme_ |  |

## Platform

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/admin/platform/activity` | SuperAdmin |  |
| GET | `/api/v1/admin/platform/dashboard` | SuperAdmin |  |
| GET | `/api/v1/admin/platform/revenue-projection` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/schools/{schoolId:guid}/complimentary-access` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/schools/{schoolId:guid}/impersonate` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/schools/{schoolId:guid}/sms-credits` | SuperAdmin |  |
| PUT | `/api/v1/admin/platform/schools/{schoolId:guid}/tenant-subscription` | SuperAdmin |  |
| GET | `/api/v1/admin/platform/subscriptions` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/subscriptions/{schoolId:guid}/remind` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/users/{userId:guid}/schools` | SuperAdmin |  |
| GET | `/api/v1/admin/platform/wave-payments` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/wave-payments/{paymentId:guid}/approve` | SuperAdmin |  |
| POST | `/api/v1/admin/platform/wave-payments/{paymentId:guid}/reject` | SuperAdmin |  |

## PromoCodes

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/admin/promo-codes` | SuperAdmin |  |
| POST | `/api/v1/admin/promo-codes` | SuperAdmin |  |
| POST | `/api/v1/admin/promo-codes/{id:guid}/deactivate` | SuperAdmin |  |

## PublicDirectory

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/public/schools` | _anonyme_ |  |
| GET | `/api/v1/public/schools/{schoolId:guid}` | _anonyme_ |  |

## PublicPricingGrid

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/public/pricing-grid` | _anonyme_ |  |

## Quran

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/quran/evaluations` | Directeur, Enseignant, Secretariat | module Coran |
| POST | `/api/v1/quran/evaluations` | Directeur, Enseignant | module Coran |
| GET | `/api/v1/quran/evaluations/classroom/{classroomId:guid}` | Directeur, Enseignant, Secretariat | module Coran |
| PUT | `/api/v1/quran/evaluations/{id:guid}` | Directeur, Enseignant | module Coran |
| GET | `/api/v1/quran/progress` | Directeur, Enseignant, Secretariat | module Coran |
| POST | `/api/v1/quran/progress` | Directeur, Enseignant | module Coran |
| GET | `/api/v1/quran/progress/classroom/{classroomId:guid}` | Directeur, Enseignant, Secretariat | module Coran |
| PUT | `/api/v1/quran/progress/{id:guid}` | Directeur, Enseignant | module Coran |

## RegistrationRequests

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/registration-requests` | _anonyme_ |  |
| GET | `/api/v1/registration-requests/{trackingReference}/status` | _anonyme_ |  |

## ReportCards

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/report-cards/class-bulletins/merged-pdf` | Directeur, Enseignant, Secretariat | module Pédagogie |
| GET | `/api/v1/report-cards/class-bulletins/zip` | Directeur, Enseignant, Secretariat | module Pédagogie |
| GET | `/api/v1/report-cards/class-deliberation/annual/pdf` | Directeur, Enseignant, Secretariat | module Pédagogie |
| GET | `/api/v1/report-cards/class-deliberation/pdf` | Directeur, Enseignant, Secretariat | module Pédagogie |
| POST | `/api/v1/report-cards/council-decisions/apply-proposals` | Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/report-cards/council-rules` | Directeur, Enseignant, Secretariat | module Pédagogie |
| PUT | `/api/v1/report-cards/council-rules` | Directeur | module Pédagogie |
| POST | `/api/v1/report-cards/generate` | Directeur, Enseignant, Secretariat | module Pédagogie |
| GET | `/api/v1/report-cards/remark` | Directeur, Enseignant, Secretariat | module Pédagogie |
| PUT | `/api/v1/report-cards/remark` | Directeur, Enseignant, Secretariat | module Pédagogie |
| POST | `/api/v1/report-cards/send` | Directeur, Enseignant, Secretariat | module Pédagogie |

## Reports

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/reports/attendance` | Directeur, Secretariat, SuperAdmin | module Pédagogie |
| GET | `/api/v1/reports/attendance/by-subject` | Directeur, Secretariat, SuperAdmin | module Pédagogie |
| GET | `/api/v1/reports/attendance/export` | Directeur, Secretariat, SuperAdmin | module Pédagogie |
| GET | `/api/v1/reports/dashboard` | Directeur, SuperAdmin |  |

## RollBook

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/roll-book` | Directeur, Secretariat, Surveillant, Enseignant, SuperAdmin | module Pédagogie |
| PUT | `/api/v1/roll-book/entries` | Directeur, Secretariat, Surveillant, Enseignant, SuperAdmin | module Pédagogie |
| DELETE | `/api/v1/roll-book/entries/{id:guid}` | Directeur, Secretariat, Surveillant, Enseignant, SuperAdmin | module Pédagogie |
| GET | `/api/v1/roll-book/pdf` | Directeur, Secretariat, Surveillant, Enseignant, SuperAdmin ∩ Directeur, Secretariat, Surveillant, SuperAdmin | module Pédagogie |
| GET | `/api/v1/roll-book/stats` | Directeur, Secretariat, Surveillant, Enseignant, SuperAdmin ∩ Directeur, Secretariat, SuperAdmin | module Pédagogie |

## Rooms

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/rooms` | Directeur, Secretariat |  |
| GET | `/api/v1/rooms/deleted` | Directeur, Secretariat |  |
| DELETE | `/api/v1/rooms/{id:guid}` | Directeur, Secretariat |  |
| PUT | `/api/v1/rooms/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/rooms/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| POST | `/api/v1/rooms/{id:guid}/restore` | Directeur, Secretariat |  |
| POST | `/api/v1/rooms/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |

## Schedules

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/schedules` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |
| GET | `/api/v1/schedules/classroom/{classroomId:guid}` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |
| GET | `/api/v1/schedules/classroom/{classroomId:guid}/sections` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |
| GET | `/api/v1/schedules/teacher/{teacherId:guid}` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |
| GET | `/api/v1/schedules/teacher/{teacherId:guid}/sections` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |
| DELETE | `/api/v1/schedules/{id:guid}` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |
| PUT | `/api/v1/schedules/{id:guid}` | Directeur, Secretariat, Enseignant, SuperAdmin | module Pédagogie |

## SchoolCalendar

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/school-calendar/closures` | Directeur, Secretariat, Enseignant, Surveillant, SuperAdmin | module Pédagogie |
| POST | `/api/v1/school-calendar/closures` | Directeur, Secretariat, Enseignant, Surveillant, SuperAdmin ∩ Directeur | module Pédagogie |
| DELETE | `/api/v1/school-calendar/closures/{id:guid}` | Directeur, Secretariat, Enseignant, Surveillant, SuperAdmin ∩ Directeur | module Pédagogie |
| PUT | `/api/v1/school-calendar/closures/{id:guid}` | Directeur, Secretariat, Enseignant, Surveillant, SuperAdmin ∩ Directeur | module Pédagogie |

## SchoolSettings

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/schools/current/settings` | Tout utilisateur authentifié |  |
| PUT | `/api/v1/schools/current/settings` | Directeur |  |
| POST | `/api/v1/schools/current/settings/cashier-signature` | Directeur |  |
| POST | `/api/v1/schools/current/settings/director-signature` | Directeur |  |
| POST | `/api/v1/schools/current/settings/establishment-profile` | Directeur |  |
| PUT | `/api/v1/schools/current/settings/grading-scale` | Directeur ; Secretariat si délégué | délégation « le Secrétariat gère la configuration des notes » |
| PUT | `/api/v1/schools/current/settings/managed-cycles` | Directeur |  |
| GET | `/api/v1/schools/current/settings/matricule-sequences` | Directeur |  |
| PUT | `/api/v1/schools/current/settings/matricule-sequences` | Directeur |  |
| POST | `/api/v1/schools/current/settings/official-stamp` | Directeur |  |
| POST | `/api/v1/schools/current/settings/secretary-signature` | Directeur |  |
| POST | `/api/v1/schools/current/settings/surveillant-signature` | Directeur |  |

## SchoolYears

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/school-years` | Tout utilisateur authentifié |  |
| POST | `/api/v1/school-years` | Directeur |  |
| GET | `/api/v1/school-years/deleted` | Directeur |  |
| DELETE | `/api/v1/school-years/{id:guid}` | Directeur |  |
| PUT | `/api/v1/school-years/{id:guid}` | Directeur |  |
| POST | `/api/v1/school-years/{id:guid}/activate` | Directeur |  |
| POST | `/api/v1/school-years/{id:guid}/apply-evaluation-periods` | Directeur |  |
| GET | `/api/v1/school-years/{id:guid}/deletion-impact` | Directeur |  |
| GET | `/api/v1/school-years/{id:guid}/export` | Directeur |  |
| POST | `/api/v1/school-years/{id:guid}/restore` | Directeur |  |
| GET | `/api/v1/school-years/{id:guid}/terms` | Tout utilisateur authentifié |  |
| POST | `/api/v1/school-years/{id:guid}/unlink-dependencies` | Directeur |  |

## Schools

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/schools` | SuperAdmin |  |
| POST | `/api/v1/schools` | SuperAdmin |  |
| PATCH | `/api/v1/schools/{schoolId:guid}/status` | SuperAdmin |  |

## Sms

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/sms/dues-reminders` | Directeur, Finance | formule Premium (SMS) |
| GET | `/api/v1/sms/history` | Directeur, Finance | formule Premium (SMS) |

## SmsWebhooks

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/webhooks/sms/{provider}` | _anonyme_ |  |

## StateIntegration

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/state-integration/certificates` | Directeur, Secretariat ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/state-integration/certificates/verify/{token}` | _anonyme_ |  |
| POST | `/api/v1/state-integration/certificates/{id:guid}/revoke` | Directeur, Secretariat ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/state-integration/planete/export` | Directeur, Secretariat ∩ Directeur |  |
| GET | `/api/v1/state-integration/simen/status` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| GET | `/api/v1/state-integration/stateduc` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| GET | `/api/v1/state-integration/stateduc/excel` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| GET | `/api/v1/state-integration/stateduc/pdf` | Directeur, Secretariat ∩ Directeur | module Pédagogie |
| PUT | `/api/v1/state-integration/students/{studentId:guid}/ien` | Directeur, Secretariat ∩ Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/state-integration/students/{studentId:guid}/mutation-certificate` | Directeur, Secretariat ∩ Directeur, Secretariat | module Pédagogie |
| GET | `/api/v1/state-integration/students/{studentId:guid}/skills-booklet` | Directeur, Secretariat ∩ Directeur, Secretariat | module Pédagogie |

## Students

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/students` | Tout utilisateur authentifié |  |
| POST | `/api/v1/students` | Directeur, Secretariat |  |
| GET | `/api/v1/students/export/pdf` | Directeur, Secretariat, Finance |  |
| POST | `/api/v1/students/import` | Directeur, Secretariat |  |
| GET | `/api/v1/students/import/template` | Directeur, Secretariat |  |
| DELETE | `/api/v1/students/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/students/{id:guid}` | Tout utilisateur authentifié |  |
| PUT | `/api/v1/students/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/students/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| PUT | `/api/v1/students/{id:guid}/matricule` | Directeur |  |
| PUT | `/api/v1/students/{id:guid}/photo` | Directeur, Secretariat |  |
| POST | `/api/v1/students/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |

## Subjects

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/subjects` | Tout utilisateur authentifié | module Pédagogie |
| POST | `/api/v1/subjects` | Directeur, Secretariat, Enseignant | module Pédagogie |
| DELETE | `/api/v1/subjects/{id:guid}` | Directeur, Secretariat, Enseignant | module Pédagogie |
| PUT | `/api/v1/subjects/{id:guid}` | Directeur, Secretariat, Enseignant | module Pédagogie |
| GET | `/api/v1/subjects/{id:guid}/deletion-impact` | Directeur, Secretariat, Enseignant | module Pédagogie |
| POST | `/api/v1/subjects/{id:guid}/unlink-dependencies` | Directeur, Secretariat, Enseignant | module Pédagogie |

## Subscriptions

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| POST | `/api/v1/subscriptions/validate-promo` | Directeur |  |
| GET | `/api/v1/subscriptions/{schoolId:guid}/payments` | Directeur |  |
| POST | `/api/v1/subscriptions/{schoolId:guid}/payments` | Directeur |  |

## Syllabus

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/syllabus/coverage` | Directeur, Secretariat | module Pédagogie |
| POST | `/api/v1/syllabus/import-template` | Directeur | module Pédagogie |
| GET | `/api/v1/syllabus/units` | Directeur, Secretariat, Enseignant, Surveillant | module Pédagogie |
| POST | `/api/v1/syllabus/units` | Directeur | module Pédagogie |
| DELETE | `/api/v1/syllabus/units/{id:guid}` | Directeur | module Pédagogie |
| PUT | `/api/v1/syllabus/units/{id:guid}` | Directeur | module Pédagogie |

## TeacherAttendance

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/teacher-attendance` | SuperAdmin, Directeur, Surveillant | module Pédagogie |
| POST | `/api/v1/teacher-attendance` | SuperAdmin, Directeur, Surveillant | module Pédagogie |

## Teachers

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/teachers` | SuperAdmin, Directeur, Secretariat |  |
| POST | `/api/v1/teachers` | Directeur, Secretariat |  |
| GET | `/api/v1/teachers/export/pdf` | SuperAdmin, Directeur, Secretariat |  |
| POST | `/api/v1/teachers/import` | Directeur, Secretariat |  |
| GET | `/api/v1/teachers/import/template` | Directeur, Secretariat |  |
| DELETE | `/api/v1/teachers/{id:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/teachers/{id:guid}` | SuperAdmin, Directeur, Secretariat |  |
| PUT | `/api/v1/teachers/{id:guid}` | Directeur, Secretariat |  |
| POST | `/api/v1/teachers/{id:guid}/assignments` | Directeur, Secretariat |  |
| DELETE | `/api/v1/teachers/{id:guid}/assignments/{assignmentId:guid}` | Directeur, Secretariat |  |
| GET | `/api/v1/teachers/{id:guid}/deletion-impact` | Directeur, Secretariat |  |
| PUT | `/api/v1/teachers/{id:guid}/matricule` | Directeur |  |
| PUT | `/api/v1/teachers/{id:guid}/photo` | Directeur, Secretariat |  |
| POST | `/api/v1/teachers/{id:guid}/unlink-dependencies` | Directeur, Secretariat |  |
| PUT | `/api/v1/teachers/{id:guid}/user-account` | Directeur |  |

## TenantSubscription

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/tenant-subscription/change-requests` | Directeur |  |
| POST | `/api/v1/tenant-subscription/change-requests` | Directeur |  |
| POST | `/api/v1/tenant-subscription/change-requests/preview` | Directeur |  |
| POST | `/api/v1/tenant-subscription/change-requests/{id:guid}/cancel` | Directeur |  |
| GET | `/api/v1/tenant-subscription/quota` | Directeur, Secretariat |  |

## Users

| Méthode | Route | Rôles | Condition |
|---|---|---|---|
| GET | `/api/v1/users` | Directeur |  |
| POST | `/api/v1/users` | Directeur |  |
| PATCH | `/api/v1/users/{userId:guid}/password` | Directeur |  |
| PATCH | `/api/v1/users/{userId:guid}/profile` | Directeur |  |
| PATCH | `/api/v1/users/{userId:guid}/status` | Directeur |  |
| GET | `/api/v1/users/{userId:guid}/status-history` | Directeur |  |

