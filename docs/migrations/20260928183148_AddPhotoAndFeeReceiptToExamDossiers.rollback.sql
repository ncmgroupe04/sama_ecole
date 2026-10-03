START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers') THEN
    ALTER TABLE exam_dossiers DROP COLUMN "FeeReceiptPresent";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers') THEN
    ALTER TABLE exam_dossiers DROP COLUMN "PhotoPresent";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers';
    END IF;
END $EF$;
COMMIT;

