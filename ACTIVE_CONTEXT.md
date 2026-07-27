# ACTIVE_CONTEXT — Périmètre V1 de Sama Ecole

État de référence du périmètre livré, tenu à jour à chaque clôture de sprint. Ce fichier ne porte
**aucune règle** : les règles non négociables vivent dans `AGENTS.md`, la spécification fonctionnelle
dans `docs/Volume_1_Cahier_des_Charges.md`. Il répond à une seule question — *qu'est-ce qui est dans
la V1, et qu'est-ce qui n'y est pas ?*

**Dernière mise à jour : 27/07/2026** (sprint de finalisation, audit de conformité P0/P1).

---

## 1. Hors périmètre V1

### Portail Parents & Élèves + Messagerie (`docs/Volume_1_Cahier_des_Charges.md` §13) — **reporté en V3**

Sont concernés, et **uniquement** eux :

| §  | Sujet | Statut |
|---|---|---|
| 13.1 | Rattachement Parent ↔ Élève | Reporté V3 |
| 13.2 | Compte Élève — restriction de cycle (Collège/Lycée) | Reporté V3 |
| 13.3 | Consultation Parent (lecture seule) | Reporté V3 |
| 13.4 | Consultation Élève (lecture seule) | Reporté V3 |
| 13.5 | Messagerie et annonces | Reporté V3 |
| 13.6 | Isolation et sécurité des portails | Reporté V3 |

Conséquences concrètes pour toute personne — ou tout agent — qui travaille sur ce dépôt :

- Les rôles `Parent` et `Eleve` **ne doivent pas** être ajoutés à l'énumération `Role`, ni apparaître
  dans un `[Authorize]`, tant que la V3 n'est pas ouverte.
- Aucun endpoint public de consultation par un tiers non-personnel de l'établissement.
- La communication avec les parents en V1 passe **exclusivement** par les canaux sortants déjà
  livrés : notification SMS (`SmsDispatcher`, formule Premium) et e-mail transactionnel.
- Les convocations de parent/tuteur (`/convocations`) ne sont **pas** un portail : c'est un registre
  interne du module Vie Scolaire, imprimé et remis en main propre.

Ce report ne remet en cause ni le modèle de données ni la RLS : le jour où le portail sera ouvert,
il devra respecter à la lettre la règle #2 d'`AGENTS.md` (Global Query Filter **et** policy RLS).

---

## 2. Modules livrés et intégrés

Livrés, câblés à l'IHM, et couverts par la suite de tests :

| Module | Écran | Backend |
|---|---|---|
| **Paie** | `/paie` | Fiches de paie, contrats employés, heures enseignants |
| **Trésorerie** | `/tresorerie` | `GetTreasuryDashboardQuery` — encaissements + décaissements consolidés |
| **Caisse** | `/caisse` | Encaissements de guichet, reçus |
| **TVA / Fiscalité** | `/fiscalite` | VRS / IPRES, `GenerateTaxDeclarationCommand` |
| **Infrastructures** | `/infrastructures` | Bâtiments & salles |
| **Documents** | Module Documents | Génération et archivage documentaire |
| **Rapports financiers** | `/rapports/financiers` | `GetRevenueConsolidationQuery` + export `.xlsx` |

Le socle V1 (Élèves, Inscriptions, Classes, Matières, Enseignants, Notes & Bulletins, Frais,
Présences, Surveillance générale, Abonnements & Facturation, Console Super Admin) est livré depuis
les sprints précédents.

---

## 3. Points de conformité traités (sprint du 27/07/2026)

1. **`Subscription` implémente `ITenantEntity`.** La table `subscriptions` figurait dans les
   `TenantTables` de la migration `EnableRowLevelSecurity` — la policy RLS était donc en place, mais
   le Global Query Filter EF Core manquait. Les **deux** protections exigées par la règle #2 sont
   désormais actives. Aucune migration : le changement est purement applicatif.
2. **Export financier `.xlsx` câblé à l'IHM.** L'endpoint `GET /finance/reports/revenue/excel`
   existait sans consommateur ; il est servi par le nouvel écran `/rapports/financiers`.
3. **Créneaux d'emploi du temps — création, modification ET suppression.** Un Enseignant ne peut plus
   agir sur le créneau d'un collègue. Le contrôle est porté par `ScheduleOwnershipAuthorizer` (même
   idiome qu'`AttendanceScopeAuthorizer`), partagé par les trois handlers : le `TeacherId` reçu est
   rapproché de sa propre fiche via `Teacher.UserId` (règle #10).
   La modification contrôle **deux** choses — le propriétaire actuel du créneau *et* le propriétaire
   demandé — sinon le verrou de la création se contournait en deux appels (créer pour soi, puis
   réattribuer à un collègue). La suppression est couverte pour la même raison : interdire la
   modification en laissant supprimer ne protégeait rien.
4. **Vue orpheline supprimée.** `Views/Absences/BilletPrint.cshtml` (« Page en construction ») et sa
   route `/billet-print` : les billets d'entrée et de sortie sont des PDF A5 générés côté serveur.
