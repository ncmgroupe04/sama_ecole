START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers') THEN
    ALTER TABLE exam_dossiers ADD "FeeReceiptPresent" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers') THEN
    ALTER TABLE exam_dossiers ADD "PhotoPresent" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928183148_AddPhotoAndFeeReceiptToExamDossiers') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260928183148_AddPhotoAndFeeReceiptToExamDossiers', '9.0.1');
    END IF;
END $EF$;
COMMIT;

