# Jangalekat.Domain

Cœur métier pur. Aucune dépendance vers un autre projet, aucun package externe hormis des utilitaires purs.

- `Entities/` — entités métier (Student, Teacher, Enrollment, Grade, Payment...). Chacune hérite de `AuditableEntity` (voir `Common/`).
- `Enums/` — enums métier (Role, EnrollmentType, PaymentMethod...).
- `Common/` — classes de base : `AuditableEntity` (Id UUID v7, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted), `ITenantEntity` (SchoolId).

Référence : docs/Volume_3_DDS.md, docs/ERD.md.
