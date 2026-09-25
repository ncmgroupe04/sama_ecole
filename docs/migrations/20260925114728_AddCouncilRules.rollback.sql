START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings DROP COLUMN "CouncilEliminatoryGrade";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings DROP COLUMN "CouncilEncouragementsMin";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings DROP COLUMN "CouncilFelicitationsMin";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings DROP COLUMN "CouncilHonorRollMin";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings DROP COLUMN "CouncilPromotionMin";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings DROP COLUMN "CouncilRepeatMin";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925114728_AddCouncilRules';
    END IF;
END $EF$;
COMMIT;

