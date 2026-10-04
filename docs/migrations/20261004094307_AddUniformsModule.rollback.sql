START TRANSACTION;
DROP TABLE student_uniforms;

DROP TABLE uniform_variants;

DROP TABLE uniform_items;

ALTER TABLE stock_movements DROP CONSTRAINT "AK_stock_movements_SchoolId_Id";

ALTER TABLE inventory_items DROP CONSTRAINT "AK_inventory_items_SchoolId_Id";

ALTER TABLE enrollment_fee_lines DROP CONSTRAINT "AK_enrollment_fee_lines_SchoolId_Id";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261004094307_AddUniformsModule';

COMMIT;

