using SamaEcole.IntegrationTests.Common;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Insertions et lectures en SQL BRUT sous le rôle applicatif (<c>sama_ecole_app</c>) pour les tables Internat :
/// seules la policy RLS, les index et les CHECK de la base font foi, jamais EF Core. Les écoles, années, élèves et
/// inscriptions sont semés à part (contexte propriétaire).
/// </summary>
internal sealed class BoardingSqlSeed(RlsTestDatabase db)
{
    public Task<Guid> InsertDormitoryAsync(Guid session, Guid school, string name = "Pavillon A", string gender = "Garcons")
        => InsertAsync(session, """
            INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
            VALUES (@id,@school,@name,@gender,NOW(),FALSE)
            """, ("school", school), ("name", name), ("gender", gender));

    public Task<Guid> InsertRoomAsync(Guid session, Guid school, Guid dormitory, string name = "Ch. 1")
        => InsertAsync(session, """
            INSERT INTO dormitory_rooms ("Id","SchoolId","DormitoryId","Name","CreatedAt","IsDeleted")
            VALUES (@id,@school,@dormitory,@name,NOW(),FALSE)
            """, ("school", school), ("dormitory", dormitory), ("name", name));

    public Task<Guid> InsertBedAsync(Guid session, Guid school, Guid room, int number = 1, string status = "Available")
        => InsertAsync(session, """
            INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
            VALUES (@id,@school,@room,@number,@status,NOW(),FALSE)
            """, ("school", school), ("room", room), ("number", number), ("status", status));

    public Task<Guid> InsertBoarderAsync(
        Guid session, Guid school, Guid student, Guid enrollment,
        string regime = "Interne", Guid? bed = null, bool active = true)
        => InsertAsync(session, """
            INSERT INTO boarding_enrollments
                ("Id","SchoolId","StudentId","EnrollmentId","Regime","BedId","StartDate","EndDate","IsActive",
                 "AllowedExitPersons","CreatedAt","IsDeleted")
            VALUES (@id,@school,@student,@enrollment,@regime,@bed,DATE '2026-09-15',
                    CASE WHEN @active THEN NULL ELSE DATE '2026-10-01' END,@active,'[]'::jsonb,NOW(),FALSE)
            """, ("school", school), ("student", student), ("enrollment", enrollment), ("regime", regime),
            ("bed", bed), ("active", active));

    public Task<Guid> InsertLeaveAsync(
        Guid session, Guid school, Guid boarder,
        string leave = "2026-10-02", string expected = "2026-10-04", string? actual = null)
        => InsertAsync(session, """
            INSERT INTO boarding_leaves
                ("Id","SchoolId","BoardingEnrollmentId","LeaveDate","ExpectedReturnDate","ActualReturnDate","Reason",
                 "AccompaniedBy","IsCompanionUnlisted","CreatedAt","IsDeleted")
            VALUES (@id,@school,@boarder,@leave::date,@expected::date,@actual::date,'Weekend','Parent',FALSE,NOW(),FALSE)
            """, ("school", school), ("boarder", boarder), ("leave", leave), ("expected", expected), ("actual", actual));

    public Task<Guid> InsertAttendanceAsync(Guid session, Guid school, Guid boarder, string date = "2026-10-02")
        => InsertAsync(session, """
            INSERT INTO boarding_attendances
                ("Id","SchoolId","BoardingEnrollmentId","Date","IsPresent","CreatedAt","IsDeleted")
            VALUES (@id,@school,@boarder,@date::date,TRUE,NOW(),FALSE)
            """, ("school", school), ("boarder", boarder), ("date", date));

    public async Task ExecuteAsync(Guid? session, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await db.OpenRawAppConnectionAsync(session);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync(Guid? session, string table)
    {
        await using var connection = await db.OpenRawAppConnectionAsync(session);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT count(*) FROM {table} WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> InsertAsync(Guid session, string sql, params (string Name, object? Value)[] parameters)
    {
        var id = Guid.NewGuid();
        await using var connection = await db.OpenRawAppConnectionAsync(session);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
        return id;
    }
}
