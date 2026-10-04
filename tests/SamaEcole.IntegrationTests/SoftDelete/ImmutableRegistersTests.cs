using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Disbursements;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence.Migrations;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Conception soft delete 2026-10-01 §3.3 / §8 — les registres financiers, l'audit et les mouvements de stock
/// sont append-only pour l'application : la BASE (rôle <c>sama_ecole_app</c>, NOBYPASSRLS) refuse la suppression,
/// et une correction se fait par contre-écriture. Les privilèges sont lus et exercés avec le rôle APPLICATIF, pas
/// avec le propriétaire (qui contournerait tout).
/// </summary>
[Trait("Category", "MultiTenant")]
public class ImmutableRegistersTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly Guid Financier = Guid.Parse("7f000000-0000-0000-0000-000000000001");

    /// <summary>Écritures comptables, preuves et journaux : jamais supprimables par l'application.</summary>
    public static TheoryData<string> ImmutableRegisters => new()
    {
        "payments", "payment_breakdowns", "subscription_payments", "fiche_paies", "taxe_declarations", "Disbursements",
        "audit_logs", "stock_movements", "fee_change_history", "employee_contract_histories", "user_status_history",
        "student_mutation_certificates",
    };

    /// <summary>Journaux strictement append-only : ni DELETE ni UPDATE.</summary>
    public static TheoryData<string> AppendOnlyLogs => new()
    {
        "audit_logs", "stock_movements", "fee_change_history", "employee_contract_histories", "user_status_history",
    };

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<bool> HasPrivilegeAsync(string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT has_table_privilege('sama_ecole_app', format('public.%I', @t), @p)";
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("p", privilege);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task App_Role_Can_Delete_Only_From_The_Technical_Allowlist()
    {
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
              AND has_table_privilege('sama_ecole_app', c.oid, 'DELETE')
            ORDER BY c.relname
            """;

        var deletable = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) deletable.Add(reader.GetString(0));
        }

        deletable.Should().BeEquivalentTo(RevokeDeleteOnBusinessTables.TechnicalTablesKeepingDelete,
            "toute nouvelle table supprimable par l'application doit être une décision explicite, jamais un héritage");
    }

    [Theory]
    [MemberData(nameof(ImmutableRegisters))]
    public async Task Immutable_Registers_Cannot_Be_Deleted_By_The_Application_Role(string table)
    {
        (await HasPrivilegeAsync(table, "DELETE")).Should().BeFalse();

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var delete = connection.CreateCommand();
        delete.CommandText = $"DELETE FROM \"{table}\" WHERE false";

        var act = async () => await delete.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [MemberData(nameof(AppendOnlyLogs))]
    public async Task Append_Only_Logs_Cannot_Be_Updated_Either(string table)
    {
        (await HasPrivilegeAsync(table, "UPDATE")).Should().BeFalse();
        (await HasPrivilegeAsync(table, "INSERT")).Should().BeTrue("un journal reste alimentable par l'application");
    }

    private async Task<Guid> SeedDisbursementAsync(Guid school, decimal amount, decimal vat)
    {
        await using var owner = _db.NewOwnerContext();
        var d = new Disbursement
        {
            SchoolId = school, Reason = "Achat de craies", Category = DisbursementCategory.Fournitures, Amount = amount,
            VatRate = 0.18m, VatAmount = vat, PaymentMethod = PaymentMethod.Cash, Date = new DateOnly(2026, 10, 1), Beneficiary = "Papeterie Sow"
        };
        owner.Disbursements.Add(d);
        await owner.SaveChangesAsync(CancellationToken.None);
        return d.Id;
    }

    private async Task CancelAsync(Guid school, Guid id)
    {
        await using var ctx = _db.NewAppContext(school);
        await new DeleteDisbursementCommandHandler(ctx, TimeProvider.System, new NoOpKpiCache())
            .Handle(new DeleteDisbursementCommand(id), CancellationToken.None);
    }

    [Fact]
    public async Task Cancelling_A_Disbursement_Adds_A_Negative_Reversal_And_Never_Touches_The_Original()
    {
        var id = await SeedDisbursementAsync(EcoleA, 10_000m, 1_525.42m);

        await CancelAsync(EcoleA, id);

        await using var owner = _db.NewOwnerContext();
        var rows = await owner.Disbursements.IgnoreQueryFilters().Where(d => d.SchoolId == EcoleA).ToListAsync();
        rows.Should().HaveCount(2);

        var original = rows.Single(d => d.Id == id);
        original.IsDeleted.Should().BeFalse("l'écriture d'origine n'est jamais supprimée");
        original.Amount.Should().Be(10_000m);
        original.ReversalOfId.Should().BeNull();

        var reversal = rows.Single(d => d.ReversalOfId == id);
        reversal.Amount.Should().Be(-10_000m);
        reversal.VatAmount.Should().Be(-1_525.42m);
        reversal.VatRate.Should().Be(0.18m);
        reversal.Category.Should().Be(DisbursementCategory.Fournitures);
        reversal.IsDeleted.Should().BeFalse();

        rows.Sum(d => d.Amount).Should().Be(0m, "les agrégats (solde, trésorerie) s'annulent");
        rows.Sum(d => d.VatAmount).Should().Be(0m, "la TVA déductible s'annule aussi");
    }

    [Fact]
    public async Task Cancelling_Twice_Or_Cancelling_A_Reversal_Is_Refused()
    {
        var id = await SeedDisbursementAsync(EcoleA, 5_000m, 0m);
        await CancelAsync(EcoleA, id);

        var twice = async () => await CancelAsync(EcoleA, id);
        (await twice.Should().ThrowAsync<BusinessRuleException>()).Which.Code
            .Should().Be(DeleteDisbursementCommandHandler.AlreadyReversedCode);

        Guid reversalId;
        await using (var owner = _db.NewOwnerContext())
        {
            reversalId = (await owner.Disbursements.IgnoreQueryFilters().SingleAsync(d => d.ReversalOfId == id)).Id;
        }

        var reversalOfReversal = async () => await CancelAsync(EcoleA, reversalId);
        (await reversalOfReversal.Should().ThrowAsync<BusinessRuleException>()).Which.Code
            .Should().Be(DeleteDisbursementCommandHandler.CannotReverseReversalCode);

        await using var check = _db.NewOwnerContext();
        (await check.Disbursements.IgnoreQueryFilters().CountAsync(d => d.SchoolId == EcoleA)).Should().Be(2);
    }

    [Fact]
    public async Task The_Database_Refuses_A_Second_Reversal_And_A_Positive_Reversal()
    {
        var id = await SeedDisbursementAsync(EcoleA, 3_000m, 0m);

        Disbursement Reversal(decimal amount) => new()
        {
            SchoolId = EcoleA, Reason = "x", Category = DisbursementCategory.Divers, Amount = amount,
            PaymentMethod = PaymentMethod.Cash, Date = new DateOnly(2026, 10, 2), Beneficiary = "y", ReversalOfId = id
        };

        await using (var first = _db.NewOwnerContext())
        {
            first.Disbursements.Add(Reversal(-3_000m));
            await first.SaveChangesAsync(CancellationToken.None);
        }

        await using (var second = _db.NewOwnerContext())
        {
            second.Disbursements.Add(Reversal(-3_000m));
            await second.Invoking(c => c.SaveChangesAsync(CancellationToken.None)).Should().ThrowAsync<Exception>(
                "une seule contre-écriture par décaissement, même en concurrence");
        }

        var other = await SeedDisbursementAsync(EcoleA, 100m, 0m);
        await using var positive = _db.NewOwnerContext();
        var wrongSign = Reversal(+100m);
        wrongSign.ReversalOfId = other;
        positive.Disbursements.Add(wrongSign);
        await positive.Invoking(c => c.SaveChangesAsync(CancellationToken.None)).Should().ThrowAsync<Exception>(
            "une contre-écriture est toujours en montant négatif");
    }

    [Fact]
    public async Task Another_Tenant_Cannot_Cancel_The_Disbursement()
    {
        var id = await SeedDisbursementAsync(EcoleA, 7_000m, 0m);

        var act = async () => await CancelAsync(EcoleB, id);

        await act.Should().ThrowAsync<NotFoundException>();
        await using var owner = _db.NewOwnerContext();
        (await owner.Disbursements.IgnoreQueryFilters().CountAsync(d => d.ReversalOfId == id)).Should().Be(0);
    }
}

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
