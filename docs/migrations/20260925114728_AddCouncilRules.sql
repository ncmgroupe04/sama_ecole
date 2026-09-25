START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings ADD "CouncilEliminatoryGrade" numeric(4,2) NOT NULL DEFAULT 5.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings ADD "CouncilEncouragementsMin" numeric(4,2) NOT NULL DEFAULT 12.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings ADD "CouncilFelicitationsMin" numeric(4,2) NOT NULL DEFAULT 14.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings ADD "CouncilHonorRollMin" numeric(4,2) NOT NULL DEFAULT 12.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings ADD "CouncilPromotionMin" numeric(4,2) NOT NULL DEFAULT 10.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    ALTER TABLE school_settings ADD "CouncilRepeatMin" numeric(4,2) NOT NULL DEFAULT 8.5;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925114728_AddCouncilRules') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925114728_AddCouncilRules', '9.0.1');
    END IF;
END $EF$;
COMMIT;

