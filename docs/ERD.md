# Jangalekat — Diagramme Entité-Association (ERD)

Référence normative du schéma : `Volume_3_DDS.md`. Ce diagramme est une vue visuelle synthétique des entités cœur du MVP ; toute divergence avec le DDS doit être corrigée dans le DDS d'abord, puis répercutée ici.

Toutes les tables listées ci-dessous portent en plus (non représenté pour lisibilité) : `id UUID v7`, `created_at`, `updated_at`, `created_by`, `updated_by`, `is_deleted`, et — sauf `schools` et `subscriptions` — `school_id` (isolation multi-tenant, RLS).

```mermaid
erDiagram
    SCHOOLS ||--o{ USERS : emploie
    SCHOOLS ||--|| SUBSCRIPTIONS : possede
    SCHOOLS ||--o{ SCHOOL_YEARS : organise
    SCHOOLS ||--o{ CLASSROOMS : contient
    SCHOOLS ||--o{ SUBJECTS : definit

    SCHOOL_REGISTRATION_REQUESTS |o--o| SCHOOLS : "devient (si approuvee)"
    SUBSCRIPTIONS ||--o{ SUBSCRIPTION_PAYMENTS : encaisse

    SCHOOL_YEARS ||--o{ ENROLLMENTS : couvre
    CLASSROOMS ||--o{ ENROLLMENTS : accueille
    CLASSROOMS ||--o{ SUBJECTS : enseigne

    STUDENTS ||--o{ ENROLLMENTS : concerne
    STUDENTS ||--o{ GRADES : recoit
    STUDENTS ||--o{ PAYMENTS : effectue
    STUDENTS ||--o{ REPORT_CARDS : possede

    TEACHERS ||--o{ SUBJECTS : enseigne
    TEACHERS ||--o{ CLASSROOMS : encadre

    SUBJECTS ||--o{ GRADES : notee_dans

    ENROLLMENTS ||--o{ PAYMENTS : genere

    USERS ||--o{ AUDIT_LOGS : declenche

    SCHOOLS {
        uuid id PK
        string name
        string address
        string phone
        string logo_url
        string status
    }

    SUBSCRIPTIONS {
        uuid id PK
        uuid school_id FK
        string plan
        date expires_at
        string status
    }

    SCHOOL_REGISTRATION_REQUESTS {
        uuid id PK
        string tracking_reference
        string director_full_name
        string director_email
        string school_name
        string requested_plan
        string status
        uuid created_school_id FK
    }

    SUBSCRIPTION_PAYMENTS {
        uuid id PK
        uuid subscription_id FK
        uuid school_id FK
        decimal amount
        string method
        string provider
        string provider_transaction_ref
        string status
    }

    USERS {
        uuid id PK
        uuid school_id FK
        string full_name
        string email
        string role
        string status
    }

    SCHOOL_YEARS {
        uuid id PK
        uuid school_id FK
        string label
        date start_date
        date end_date
        bool is_active
    }

    CLASSROOMS {
        uuid id PK
        uuid school_id FK
        string name
        string level
        int capacity
    }

    SUBJECTS {
        uuid id PK
        uuid school_id FK
        string name
        decimal coefficient
        string level
    }

    STUDENTS {
        uuid id PK
        uuid school_id FK
        string matricule
        string full_name
        date birth_date
        string gender
        uuid classroom_id FK
    }

    TEACHERS {
        uuid id PK
        uuid school_id FK
        string matricule
        string full_name
        string email
    }

    ENROLLMENTS {
        uuid id PK
        uuid school_id FK
        uuid student_id FK
        uuid classroom_id FK
        uuid school_year_id FK
        string type
        decimal total_due
        string status
    }

    GRADES {
        uuid id PK
        uuid school_id FK
        uuid student_id FK
        uuid subject_id FK
        uuid term_id FK
        decimal value
        bytea row_version
    }

    REPORT_CARDS {
        uuid id PK
        uuid school_id FK
        uuid student_id FK
        uuid term_id FK
        string pdf_url
        decimal general_average
    }

    PAYMENTS {
        uuid id PK
        uuid school_id FK
        uuid student_id FK
        uuid enrollment_id FK
        decimal amount
        string method
        string category
        bytea row_version
    }

    AUDIT_LOGS {
        uuid id PK
        uuid school_id FK
        uuid user_id FK
        string action
        jsonb payload
        timestamp occurred_at
    }
```

## Notes de lecture

- `SUBSCRIPTIONS` n'a pas de `school_id` en tant que colonne de filtrage tenant — c'est l'entité qui *définit* le tenant, gérée exclusivement par le Super Admin (RLS différente, voir `Volume_3_DDS.md` §Multi-tenant, cas particulier).
- `GRADES` et `PAYMENTS` portent une colonne `row_version` : verrouillage optimiste obligatoire (Décision D-09, `Volume_0_Vision_Architecture.md` §0.13).
- `SCHOOL_REGISTRATION_REQUESTS` et `SUBSCRIPTION_PAYMENTS` sont des tables plateforme (comme `SUBSCRIPTIONS`) : pas de RLS par `school_id`, accès réservé au Super Admin (et, pour `SUBSCRIPTION_PAYMENTS` en lecture seule, au Directeur de l'école concernée). Voir Volume 1 §11.5-11.6, Volume 3 §5.7-5.8, Volume 7 §12bis.
- Le détail des contraintes, index, et policies RLS complètes est dans `Volume_3_DDS.md` — ce fichier n'a pas vocation à être exhaustif, seulement à donner une vue d'ensemble rapide à un agent avant de plonger dans le DDS.
