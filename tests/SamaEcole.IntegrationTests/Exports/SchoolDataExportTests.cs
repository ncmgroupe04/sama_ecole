using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Exports;
using SamaEcole.Application.Exports.Queries.ExportSchoolData;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using SamaEcole.Persistence.Exports;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Exports;

/// <summary>
/// « Exporter mes données » : l'archive ne contient QUE l'école courante (RLS + filtre EF), jamais de secret, jamais de ligne supprimée,
/// et exige le mot de passe du Directeur. Toutes les lectures passent par le rôle applicatif (soumis à la RLS), comme en production.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SchoolDataExportTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DirecteurA = Guid.Parse("dddddddd-1111-0000-0000-00000000000a");
    private static readonly Guid DirecteurB = Guid.Parse("dddddddd-1111-0000-0000-00000000000b");

    private const string Password = "Motdepasse!Solide2026";
    private static readonly string Hash = new IdentityPasswordHasher().Hash(Password);
    private static readonly string HashB = new IdentityPasswordHasher().Hash("Autre!Secret2026");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École des Baobabs" }, new School { Id = EcoleB, Name = "École B" });
        owner.Users.AddRange(
            new User { Id = DirecteurA, SchoolId = EcoleA, Email = "dir@a.sn", PasswordHash = Hash, FullName = "Fatou Ndiaye", Role = Role.Directeur },
            new User { Id = DirecteurB, SchoolId = EcoleB, Email = "dir@b.sn", PasswordHash = HashB, FullName = "Moussa Diop", Role = Role.Directeur });
        var supprime = new Building { SchoolId = EcoleA, Name = "Bâtiment supprimé A" };
        supprime.SoftDelete("test");
        owner.Buildings.AddRange(
            new Building { SchoolId = EcoleA, Name = "Bâtiment A" },
            supprime,
            new Building { SchoolId = EcoleB, Name = "Bâtiment B" });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Dictionary<string, string>> ExportFilesAsync(Guid schoolId)
    {
        await using var ctx = _db.NewAppContext(schoolId);
        var exporter = new SchoolDataExporter(ctx, new FixedTenant(schoolId), TimeProvider.System);

        using var buffer = new MemoryStream();
        await exporter.ExportAsync(buffer, CancellationToken.None);
        buffer.Position = 0;

        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            });
    }

    [Fact]
    public async Task The_Archive_Contains_Only_The_Current_Schools_Rows()
    {
        var files = await ExportFilesAsync(EcoleA);

        files["donnees/buildings.csv"].Should().Contain("Bâtiment A").And.NotContain("Bâtiment B");
        files["donnees/users.csv"].Should().Contain("dir@a.sn").And.NotContain("dir@b.sn");
        files.Values.Should().NotContain(content => content.Contains("École B") || content.Contains("Moussa Diop"),
            "aucune donnée de l'école B ne doit apparaître, quel que soit le fichier");
    }

    [Fact]
    public async Task The_Archive_Never_Contains_Password_Hashes_Or_Authentication_Token_Tables()
    {
        var files = await ExportFilesAsync(EcoleA);

        files.Keys.Should().NotContain(k => k.Contains("refresh_tokens") || k.Contains("password_reset_tokens"));
        var usersHeader = files["donnees/users.csv"].Split('\n')[0];
        usersHeader.Should().NotContainEquivalentOf("PasswordHash").And.NotContain("AccessFailedCount").And.NotContain("LockoutEndAt");
        files.Values.Should().NotContain(content => content.Contains(Hash) || content.Contains(HashB), "un hachage de mot de passe ne sort jamais");
    }

    [Fact]
    public async Task Soft_Deleted_Rows_Are_Not_Exported()
    {
        var files = await ExportFilesAsync(EcoleA);

        files["donnees/buildings.csv"].Should().NotContain("Bâtiment supprimé A");
    }

    [Fact]
    public async Task The_Manifest_Lists_Row_Counts_And_The_Readme_Explains_The_Format()
    {
        var files = await ExportFilesAsync(EcoleA);

        files.Should().ContainKeys("manifest.json", "LISEZMOI.txt");
        files["manifest.json"].Should().Contain("\"etablissement\": \"École des Baobabs\"");
        files["manifest.json"].Should().Contain("\"table\": \"buildings\"");
        files["LISEZMOI.txt"].Should().Contain("UTF-8").And.Contain("point-virgule").And.Contain("mots de passe");
    }

    [Fact]
    public async Task The_Manifest_Is_Valid_Json_Without_A_Byte_Order_Mark_While_Csv_Files_Keep_One_For_Excel()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var exporter = new SchoolDataExporter(ctx, new FixedTenant(EcoleA), TimeProvider.System);
        using var buffer = new MemoryStream();
        await exporter.ExportAsync(buffer, CancellationToken.None);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

        byte[] Raw(string name)
        {
            using var stream = zip.GetEntry(name)!.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        var manifest = Raw("manifest.json");
        var csv = Raw("donnees/buildings.csv");

        // Les lecteurs JSON standard refusent un BOM (EF BB BF) ; Excel, lui, en a besoin pour lire l'UTF-8 d'un CSV.
        manifest.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        csv.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        System.Text.Json.JsonDocument.Parse(manifest).RootElement.GetProperty("tables").GetArrayLength().Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task An_Empty_Table_Produces_No_File_But_Is_Listed_In_The_Manifest()
    {
        var files = await ExportFilesAsync(EcoleA);

        files.Keys.Should().NotContain("donnees/grades.csv");
        files["manifest.json"].Should().Contain("\"table\": \"grades\"");
    }

    [Fact]
    public async Task No_Exported_Column_Looks_Like_A_Secret_Unless_It_Was_Reviewed()
    {
        await using var ctx = _db.NewOwnerContext();

        var suspicious = SchoolDataExporter.ExportedColumns(ctx)
            .Where(c => SensitiveColumnRules.LooksSensitive(c.Column) && !SensitiveColumnRules.ReviewedAllowed.Contains(c.Column))
            .Select(c => $"{c.Table}.{c.Column}")
            .ToList();

        suspicious.Should().BeEmpty(
            "une colonne nommée comme un secret doit être ajoutée à SensitiveColumnRules.ExcludedColumns (ou revue et autorisée) avant de pouvoir être exportée");
    }

    [Fact]
    public async Task Every_Tenant_Table_Is_Part_Of_The_Archive()
    {
        await using var ctx = _db.NewOwnerContext();

        var tenantTables = ctx.Model.GetEntityTypes()
            .Where(t => typeof(SamaEcole.Domain.Common.ITenantEntity).IsAssignableFrom(t.ClrType) && !t.IsOwned() && t.GetTableName() is not null)
            .Select(t => t.GetTableName()!)
            .Where(t => !SensitiveColumnRules.ExcludedTables.Contains(t))
            .Distinct();
        var exported = SchoolDataExporter.ExportedColumns(ctx).Select(c => c.Table).Distinct().ToHashSet();

        tenantTables.Where(t => !exported.Contains(t)).Should().BeEmpty("toute table d'établissement doit figurer dans l'export global");
    }

    [Fact]
    public async Task Every_Exported_Table_Is_Scoped_By_A_Global_Filter_Or_By_An_Explicit_School_Condition()
    {
        await using var ctx = _db.NewOwnerContext();

        // Les seules tables sans filtre global sont bornées EXPLICITEMENT dans SchoolDataExporter.Query : la liste est figée ici pour qu'un
        // ajout non filtré (qui exporterait les données d'autres écoles) fasse échouer ce test.
        var explicitlyScoped = new[] { typeof(School), typeof(User), typeof(UserSchool) };

        var unfiltered = ctx.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.GetTableName() is not null && t.BaseType is null)
            .Where(t => typeof(SamaEcole.Domain.Common.ITenantEntity).IsAssignableFrom(t.ClrType) || explicitlyScoped.Contains(t.ClrType))
            .Where(t => t.GetQueryFilter() is null && !explicitlyScoped.Contains(t.ClrType))
            .Select(t => t.GetTableName()!)
            .ToList();

        unfiltered.Should().BeEmpty("toute table exportée doit avoir un Global Query Filter, sinon l'export fuirait entre établissements");
    }

    // ------------------------------------------------------------------ Handler

    private ExportSchoolDataQueryHandler NewHandler(SamaEcole.Persistence.ApplicationDbContext ctx, Guid user, Guid school) =>
        new(ctx, new TestCurrentUser(user, Role.Directeur), new IdentityPasswordHasher(),
            new SchoolDataExporter(ctx, new FixedTenant(school), TimeProvider.System),
            NullLogger<ExportSchoolDataQueryHandler>.Instance);

    [Fact]
    public async Task A_Wrong_Password_Is_Refused_On_The_Password_Field_And_Nothing_Is_Exported()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = NewHandler(ctx, DirecteurA, EcoleA);

        var act = async () => await handler.Handle(new ExportSchoolDataQuery("mauvais"), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Should().ContainKey("Password").WhoseValue.Should().Contain("Mot de passe incorrect.");
    }

    [Fact]
    public async Task Another_Users_Password_Does_Not_Unlock_The_Export()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = NewHandler(ctx, DirecteurA, EcoleA);

        var act = async () => await handler.Handle(new ExportSchoolDataQuery("Autre!Secret2026"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_Right_Password_Returns_A_Readable_Zip_Named_After_The_School()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = NewHandler(ctx, DirecteurA, EcoleA);

        await using var result = await handler.Handle(new ExportSchoolDataQuery(Password), CancellationToken.None);

        result.Content.Position.Should().Be(0);
        result.FileName.Should().MatchRegex(@"^export-ecole-des-baobabs-\d{4}-\d{2}-\d{2}\.zip$");
        result.TableCount.Should().BeGreaterThan(0);
        using var zip = new ZipArchive(result.Content, ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().Contain(["manifest.json", "LISEZMOI.txt", "donnees/buildings.csv"]);
    }

    // ------------------------------------------------------------------ CSV

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("il dit \"oui\"", "\"il dit \"\"oui\"\"\"")]
    [InlineData("ligne1\nligne2", "\"ligne1\nligne2\"")]
    [InlineData("=SOMME(A1:A9)", "'=SOMME(A1:A9)")]
    [InlineData("+221770000000", "'+221770000000")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("-25000", "-25000")]
    [InlineData("-0.5", "-0.5")]
    [InlineData("", "")]
    public void Csv_Escaping_Protects_Delimiters_And_Neutralises_Formulas(string raw, string expected) =>
        SchoolDataExporter.Escape(raw).Should().Be(expected);
}

file sealed class FixedTenant(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
