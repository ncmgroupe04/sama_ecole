using FluentAssertions;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Schools;

/// <summary>
/// Ticket JGK-B02 — les paramètres d'établissement pilotent RÉELLEMENT la numérotation.
///
/// Un réglage qui ne change rien serait pire que pas de réglage : le Directeur croirait avoir
/// personnalisé ses matricules alors que le format resterait celui codé en dur. Ces tests vérifient
/// donc l'effet, pas seulement le stockage.
/// </summary>
public class SchoolSettingsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task SetFormatAsync(Guid schoolId, string studentFormat)
    {
        await using var owner = _db.NewOwnerContext();
        owner.SchoolSettings.Add(new SchoolSettings
        {
            SchoolId = schoolId,
            StudentMatriculeFormat = studentFormat
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_School_Without_Settings_Should_Fall_Back_To_The_Defaults()
    {
        // Les écoles créées avant JGK-B02 n'ont pas de ligne de réglages : une inscription ne doit pas
        // dépendre d'un écran de paramètres que personne n'a encore ouvert.
        await using var db = _db.NewAppContext(EcoleA);

        var matricule = await _db.NewGenerator(db)
            .GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        matricule.Should().Be($"ELEV-{_year}-0001");
    }

    [Fact]
    public async Task The_Configured_Format_Should_Actually_Drive_The_Generated_Matricule()
    {
        await SetFormatAsync(EcoleA, "BAOBAB/{YEAR}/{SEQ:5}");

        await using var db = _db.NewAppContext(EcoleA);

        var matricule = await _db.NewGenerator(db)
            .GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        matricule.Should().Be($"BAOBAB/{_year}/00001",
            "le format paramétré doit s'appliquer, sinon le réglage ne sert à rien");
    }

    [Fact]
    public async Task Each_School_Should_Keep_Its_Own_Format()
    {
        await SetFormatAsync(EcoleA, "AAA-{SEQ:3}");
        await SetFormatAsync(EcoleB, "BBB-{SEQ:2}");

        await using var dbA = _db.NewAppContext(EcoleA);
        var matriculeA = await _db.NewGenerator(dbA)
            .GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        await using var dbB = _db.NewAppContext(EcoleB);
        var matriculeB = await _db.NewGenerator(dbB)
            .GenerateNextStudentMatriculeAsync(EcoleB, CancellationToken.None);

        matriculeA.Should().Be("AAA-001");
        matriculeB.Should().Be("BBB-01");
    }

    [Fact]
    public async Task Settings_Of_Another_School_Must_Never_Be_Readable()
    {
        await SetFormatAsync(EcoleA, "AAA-{SEQ:3}");
        await SetFormatAsync(EcoleB, "BBB-{SEQ:2}");

        // SQL brut, hors EF : seule la policy RLS fait foi.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "StudentMatriculeFormat" FROM school_settings;""";

        var formats = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            formats.Add(reader.GetString(0));
        }

        formats.Should().ContainSingle().Which.Should().Be("AAA-{SEQ:3}");
        formats.Should().NotContain("BBB-{SEQ:2}");
    }

    [Fact]
    public async Task Provisioning_A_School_Should_Create_Its_Default_Settings()
    {
        // Critère du ticket : « les valeurs par défaut sont appliquées à la création ».
        var nouvelleEcole = Guid.NewGuid();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Id = nouvelleEcole, Name = "École neuve" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        // Session sans tenant : c'est le contexte d'un Super Admin (ticket JGK-B01).
        await using (var db = _db.NewAppContext(schoolId: null))
        {
            await _db.NewProvisioningStore(db).CreateInitialDirectorAsync(
                nouvelleEcole, "directrice@neuve.sn", "hash", "Directrice",
                Domain.Enums.Role.Directeur, CancellationToken.None);
        }

        await using var appDb = _db.NewAppContext(nouvelleEcole);
        var settings = appDb.SchoolSettings.Single();

        settings.GradingScale.Should().Be(SchoolSettingsDefaults.GradingScale);
        settings.StudentMatriculeFormat.Should().Be(SchoolSettingsDefaults.StudentMatriculeFormat);
        settings.TeacherMatriculeFormat.Should().Be(SchoolSettingsDefaults.TeacherMatriculeFormat);
        settings.AutoLogoutMinutes.Should().Be(SchoolSettingsDefaults.AutoLogoutMinutes);
        settings.DateFormat.Should().Be(SchoolSettingsDefaults.DateFormat);
    }
}
