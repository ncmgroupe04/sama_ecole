namespace SamaEcole.Persistence;

/// <summary>
/// Script de RÉCONCILIATION des séjours Internat avec l'ancien modèle (<c>enrollments.BoardingStatus/RoomId</c>), exécuté
/// UNE SEULE FOIS par la migration <c>ReconcileBoardingWithLegacyModel</c> (lot C de la spec Internat Pavillon/Lit).
///
/// Pourquoi : la reprise du lot A (<c>AddBoardingDormitoryModel</c>) était un instantané ; l'ancien écran a continué
/// d'écrire les colonnes héritées depuis. Règle de priorité : <b>l'ancien modèle prévaut pour les SÉJOURS</b> (avant le
/// lot C, rien d'autre n'écrit <c>boarding_enrollments</c>) ; la <b>structure</b> (pavillons, chambres, lits) est
/// ADDITIVE : on crée ce qui manque, on ne modifie ni ne supprime rien de créé par la nouvelle API ou par un Directeur.
///
/// <b>À USAGE UNIQUE — ne jamais le rejouer après la bascule</b> : ensuite les colonnes héritées ne sont plus
/// maintenues, et le rejouer fermerait de vrais séjours. Ce n'est donc volontairement PAS une fonction SQL durable.
/// <b>Figé après fusion : ne jamais modifier ce texte</b> (la migration l'exécute tel quel ; les tests le pinnent).
/// </summary>
public static class BoardingReconciliationSql
{
    public const string Script = """
        DO $reconcile$
        DECLARE v_n integer;
        BEGIN
            -- R1. Pavillons manquants : un par bâtiment vivant ayant une chambre Dortoir vivante, Id = Building.Id.
            --     Additif (ON CONFLICT DO NOTHING) ; un nom déjà pris par un pavillon vivant n'est PAS écrasé.
            INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
            SELECT b."Id", b."SchoolId", b."Name",
                   CASE g.gender WHEN 'M' THEN 'Garcons' WHEN 'F' THEN 'Filles' ELSE 'Mixte' END, NOW(), FALSE
            FROM buildings b
            LEFT JOIN LATERAL (
                SELECT CASE WHEN count(DISTINCT s."Gender") = 1 THEN min(s."Gender") END AS gender
                FROM enrollments e
                JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
                JOIN rooms r ON r."SchoolId" = e."SchoolId" AND r."Id" = e."RoomId"
                JOIN students s ON s."SchoolId" = e."SchoolId" AND s."Id" = e."StudentId"
                WHERE r."BuildingId" = b."Id" AND r."Type" = 'Dortoir' AND NOT r."IsDeleted"
                  AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
            ) g ON TRUE
            WHERE NOT b."IsDeleted"
              AND EXISTS (SELECT 1 FROM rooms r WHERE r."SchoolId" = b."SchoolId" AND r."BuildingId" = b."Id"
                          AND r."Type" = 'Dortoir' AND NOT r."IsDeleted")
              AND NOT EXISTS (SELECT 1 FROM dormitories d
                              WHERE d."SchoolId" = b."SchoolId" AND d."Name" = b."Name" AND NOT d."IsDeleted")
            ON CONFLICT ("Id") DO NOTHING;
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R1 pavillons créés : %', v_n;

            -- R2. Chambres manquantes : Id = Room.Id, uniquement sous un pavillon vivant.
            INSERT INTO dormitory_rooms ("Id","SchoolId","DormitoryId","Name","CreatedAt","IsDeleted")
            SELECT r."Id", r."SchoolId", r."BuildingId", r."Name", NOW(), FALSE
            FROM rooms r
            JOIN dormitories d ON d."SchoolId" = r."SchoolId" AND d."Id" = r."BuildingId" AND NOT d."IsDeleted"
            WHERE r."Type" = 'Dortoir' AND NOT r."IsDeleted"
            ON CONFLICT ("Id") DO NOTHING;
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R2 chambres créées : %', v_n;

            -- R3a. Lits des chambres reprises qui n'en ont encore aucun : max(capacité, internes de l'année active).
            INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
            SELECT gen_random_uuid(), dr."SchoolId", dr."Id", n::int, 'Available', NOW(), FALSE
            FROM dormitory_rooms dr
            JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id" AND r."Type" = 'Dortoir'
            CROSS JOIN LATERAL generate_series(1, GREATEST(r."Capacity", (
                SELECT count(*) FROM enrollments e
                JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
                WHERE e."SchoolId" = r."SchoolId" AND e."RoomId" = r."Id" AND e."BoardingStatus" = 'Interne'
                  AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"))::int) AS n
            WHERE NOT dr."IsDeleted"
              AND NOT EXISTS (SELECT 1 FROM beds b WHERE b."DormitoryRoomId" = dr."Id");
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R3a lits créés : %', v_n;

            -- R3b. Complément : une chambre reprise qui a moins de lits utilisables que d'internes dans l'ancien modèle
            --      reçoit des lits supplémentaires, numérotés APRÈS le plus grand numéro existant (supprimés compris).
            INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
            SELECT gen_random_uuid(), dr."SchoolId", dr."Id", m.max_no + g::int, 'Available', NOW(), FALSE
            FROM dormitory_rooms dr
            JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id" AND r."Type" = 'Dortoir'
            CROSS JOIN LATERAL (
                SELECT COALESCE(max(b."BedNumber"), 0) AS max_no,
                       count(*) FILTER (WHERE NOT b."IsDeleted" AND b."Status" = 'Available') AS usable
                FROM beds b WHERE b."DormitoryRoomId" = dr."Id") m
            CROSS JOIN LATERAL (
                SELECT count(*) AS need FROM enrollments e
                JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
                WHERE e."SchoolId" = r."SchoolId" AND e."RoomId" = r."Id" AND e."BoardingStatus" = 'Interne'
                  AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted") o
            CROSS JOIN LATERAL generate_series(1, GREATEST(o.need - m.usable, 0)::int) AS g
            WHERE NOT dr."IsDeleted";
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'R3b lits ajoutés : %', v_n;

            -- S1. Clore les séjours actifs que l'ancien modèle ne justifie plus : inscription Externe, annulée,
            --     supprimée ou hors année active. EndDate = fin de l'année si elle est inactive, sinon aujourd'hui ;
            --     jamais avant StartDate (contrainte CK_boarding_enrollments_dates).
            UPDATE boarding_enrollments be
            SET "IsActive" = FALSE, "BedId" = NULL, "UpdatedAt" = NOW(),
                "EndDate" = GREATEST(be."StartDate", COALESCE(
                    (SELECT y."EndDate" FROM enrollments e
                     JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId"
                     WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId" AND NOT y."IsActive"), CURRENT_DATE))
            WHERE be."IsActive" AND NOT be."IsDeleted"
              AND NOT EXISTS (
                  SELECT 1 FROM enrollments e
                  JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
                  WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
                    AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted");
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S1 séjours clos : %', v_n;

            -- S2. Aligner le régime ; un demi-pensionnaire n'a jamais de lit (CK_boarding_enrollments_half_board_no_bed).
            UPDATE boarding_enrollments be
            SET "Regime" = e."BoardingStatus", "UpdatedAt" = NOW(),
                "BedId" = CASE WHEN e."BoardingStatus" = 'DemiPensionnaire' THEN NULL ELSE be."BedId" END
            FROM enrollments e
            WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
              AND be."IsActive" AND NOT be."IsDeleted" AND be."Regime" <> e."BoardingStatus";
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S2 régimes alignés : %', v_n;

            -- S3. Libérer d'abord les lits des internes dont la chambre a changé (ou a disparu) dans l'ancien modèle :
            --     l'index unique du lit est immédiat, une permutation en une seule instruction échouerait.
            UPDATE boarding_enrollments be
            SET "BedId" = NULL, "UpdatedAt" = NOW()
            FROM enrollments e
            WHERE e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
              AND be."IsActive" AND NOT be."IsDeleted" AND be."Regime" = 'Interne' AND be."BedId" IS NOT NULL
              AND (e."RoomId" IS NULL
                   OR NOT EXISTS (SELECT 1 FROM beds b WHERE b."Id" = be."BedId" AND b."DormitoryRoomId" = e."RoomId"));
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S3 lits libérés : %', v_n;

            -- S4. Séjours manquants : inscription pensionnaire de l'année active sans séjour actif.
            INSERT INTO boarding_enrollments
                ("Id","SchoolId","StudentId","EnrollmentId","Regime","BedId","StartDate","EndDate","IsActive","AllowedExitPersons","CreatedAt","IsDeleted")
            SELECT gen_random_uuid(), e."SchoolId", e."StudentId", e."Id", e."BoardingStatus", NULL,
                   (e."EnrolledAt" AT TIME ZONE 'UTC')::date, NULL, TRUE, '[]'::jsonb, NOW(), FALSE
            FROM enrollments e
            JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
            WHERE e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
              AND NOT EXISTS (SELECT 1 FROM boarding_enrollments be
                              WHERE be."SchoolId" = e."SchoolId" AND be."EnrollmentId" = e."Id"
                                AND be."IsActive" AND NOT be."IsDeleted");
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S4 séjours créés : %', v_n;

            -- S5. Asseoir les internes sans lit dans leur chambre de l'ancien modèle : lits libres et disponibles par
            --     numéro croissant, internes par ordre d'inscription. Faute de place, ils restent « en attente ».
            WITH need AS (
                SELECT be."Id" AS stay_id, e."RoomId" AS room_id,
                       row_number() OVER (PARTITION BY e."RoomId" ORDER BY e."EnrolledAt", be."Id") AS rn
                FROM boarding_enrollments be
                JOIN enrollments e ON e."SchoolId" = be."SchoolId" AND e."Id" = be."EnrollmentId"
                WHERE be."IsActive" AND NOT be."IsDeleted" AND be."Regime" = 'Interne' AND be."BedId" IS NULL
                  AND e."RoomId" IS NOT NULL
                  AND EXISTS (SELECT 1 FROM dormitory_rooms dr WHERE dr."Id" = e."RoomId" AND NOT dr."IsDeleted")
            ), free AS (
                SELECT b."Id" AS bed_id, b."DormitoryRoomId" AS room_id,
                       row_number() OVER (PARTITION BY b."DormitoryRoomId" ORDER BY b."BedNumber") AS rn
                FROM beds b
                WHERE NOT b."IsDeleted" AND b."Status" = 'Available'
                  AND NOT EXISTS (SELECT 1 FROM boarding_enrollments x
                                  WHERE x."BedId" = b."Id" AND x."IsActive" AND NOT x."IsDeleted")
            )
            UPDATE boarding_enrollments be
            SET "BedId" = f.bed_id, "UpdatedAt" = NOW()
            FROM need n JOIN free f ON f.room_id = n.room_id AND f.rn = n.rn
            WHERE be."Id" = n.stay_id;
            GET DIAGNOSTICS v_n = ROW_COUNT; RAISE NOTICE 'S5 internes assis : %', v_n;
        END
        $reconcile$;
        """;
}
