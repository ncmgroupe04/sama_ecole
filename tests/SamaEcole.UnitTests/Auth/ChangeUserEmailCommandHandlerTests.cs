using SamaEcole.Application.Auth.Commands.ChangeEmail;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace SamaEcole.UnitTests.Auth;

/// <summary>
/// Décision de sécurité de ChangeUserEmailCommandHandler : même politique que
/// ChangePasswordCommandHandler — preuve du mot de passe actuel exigée avant tout changement, TOUTES
/// les sessions révoquées après (l'e-mail EST l'identifiant de login). Le Handler ne passe jamais par
/// IApplicationDbContext : `users` est sous RLS (ticket JGK-A03) et un Super Admin n'a aucun SchoolId
/// de session — tout transite par IAuthStore, ici substitué (NSubstitute), jamais par une vraie base.
/// </summary>
public class ChangeUserEmailCommandHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string CurrentPasswordHash = "hash-du-mot-de-passe-actuel";
    private const string CorrectCurrentPassword = "Mot-De-Passe-Actuel-9!";

    private static AuthUser BuildUser(string email = "ancien.email@sama-ecole.sn") => new(
        Id: UserId,
        SchoolId: Guid.NewGuid(),
        Email: email,
        PasswordHash: CurrentPasswordHash,
        FullName: "Utilisateur Test",
        Role: Role.Directeur,
        Status: EntityStatus.Active,
        AccessFailedCount: 0,
        LockoutEndAt: null);

    private static (ChangeUserEmailCommandHandler Handler, ICurrentUserService CurrentUser, IAuthStore AuthStore, IPasswordHasher PasswordHasher)
        BuildHandler(AuthUser? currentUser, Guid? currentUserId = null)
    {
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.UserId.Returns(currentUserId ?? UserId);

        var authStore = Substitute.For<IAuthStore>();
        authStore.FindUserByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(currentUser);

        var passwordHasher = Substitute.For<IPasswordHasher>();

        var handler = new ChangeUserEmailCommandHandler(
            currentUserService, authStore, passwordHasher, NullLogger<ChangeUserEmailCommandHandler>.Instance);

        return (handler, currentUserService, authStore, passwordHasher);
    }

    [Fact]
    public async Task An_Incorrect_Current_Password_Should_Be_Rejected_And_Change_Nothing()
    {
        var user = BuildUser();
        var (handler, _, authStore, passwordHasher) = BuildHandler(user);

        passwordHasher.Verify(CurrentPasswordHash, "Ceci-Nest-Pas-Le-Bon-9!").Returns(false);

        var command = new ChangeUserEmailCommand("nouveau.email@sama-ecole.sn", "Ceci-Nest-Pas-Le-Bon-9!");

        var act = () => handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainKey(nameof(ChangeUserEmailCommand.CurrentPassword));

        await authStore.DidNotReceive().ChangeEmailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Duplicate_Email_Caught_By_The_Precheck_Should_Be_Rejected_As_A_Validation_Error()
    {
        var user = BuildUser();
        var (handler, _, authStore, passwordHasher) = BuildHandler(user);

        passwordHasher.Verify(CurrentPasswordHash, CorrectCurrentPassword).Returns(true);

        // Un AUTRE compte détient déjà cet e-mail : le pré-contrôle applicatif doit le voir avant
        // d'atteindre la base (message plus rapide, plus clair — voir le commentaire du Handler).
        authStore.FindUserByEmailAsync("deja.pris@sama-ecole.sn", Arg.Any<CancellationToken>())
            .Returns(BuildUser("deja.pris@sama-ecole.sn") with { Id = Guid.NewGuid() });

        var command = new ChangeUserEmailCommand("Deja.Pris@Sama-Ecole.sn", CorrectCurrentPassword);

        var act = () => handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainKey(nameof(ChangeUserEmailCommand.NewEmail));

        await authStore.DidNotReceive().ChangeEmailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Le filet de sécurité RÉEL contre une course entre deux changements simultanés reste la
    /// contrainte SQL (AuthStore.SetEmailAsync, catch du 23505) : le pré-contrôle applicatif peut être
    /// franchi par deux requêtes concurrentes avant que l'une ne commite. Le Handler ne doit PAS
    /// avaler cette exception : elle doit remonter telle quelle jusqu'au pipeline d'erreurs (409).
    /// </summary>
    [Fact]
    public async Task A_Duplicate_Detected_Only_By_The_Database_Should_Propagate_As_A_409_Conflict()
    {
        var user = BuildUser();
        var (handler, _, authStore, passwordHasher) = BuildHandler(user);

        passwordHasher.Verify(CurrentPasswordHash, CorrectCurrentPassword).Returns(true);
        authStore.FindUserByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AuthUser?)null);

        var duplicate = new DuplicateRecordException("Un compte utilise déjà cette adresse e-mail.", "users / IX_users_Email");
        authStore.ChangeEmailAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw duplicate);

        var command = new ChangeUserEmailCommand("course.email@sama-ecole.sn", CorrectCurrentPassword);

        var act = () => handler.Handle(command, CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateRecordException>()).Which.Should().BeSameAs(duplicate);
    }

    [Fact]
    public async Task A_Correct_Current_Password_Should_Change_The_Email_Normalized_And_Revoke_Sessions()
    {
        var user = BuildUser();
        var (handler, _, authStore, passwordHasher) = BuildHandler(user);

        passwordHasher.Verify(CurrentPasswordHash, CorrectCurrentPassword).Returns(true);
        authStore.FindUserByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AuthUser?)null);
        authStore.ChangeEmailAsync(UserId, "nouveau.email@sama-ecole.sn", Arg.Any<CancellationToken>())
            .Returns(3);

        // Casse et espaces volontairement irréguliers : EmailNormalizer doit produire la forme
        // canonique AVANT d'atteindre IAuthStore (même règle que CreateUserCommandHandler).
        var command = new ChangeUserEmailCommand("  Nouveau.Email@Sama-Ecole.SN  ", CorrectCurrentPassword);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Email.Should().Be("nouveau.email@sama-ecole.sn");
        result.RevokedSessions.Should().Be(3);

        await authStore.Received(1).ChangeEmailAsync(UserId, "nouveau.email@sama-ecole.sn", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changing_To_The_Same_Email_With_Different_Case_Should_Not_Trigger_The_Duplicate_Precheck()
    {
        // Un compte ne doit jamais se voir opposer « déjà pris » à cause de sa propre adresse — le
        // pré-contrôle compare sans tenir compte de la casse, comme l'index citext en base.
        var user = BuildUser("directeur@sama-ecole.sn");
        var (handler, _, authStore, passwordHasher) = BuildHandler(user);

        passwordHasher.Verify(CurrentPasswordHash, CorrectCurrentPassword).Returns(true);
        authStore.ChangeEmailAsync(UserId, "directeur@sama-ecole.sn", Arg.Any<CancellationToken>()).Returns(1);

        var command = new ChangeUserEmailCommand("Directeur@Sama-Ecole.sn", CorrectCurrentPassword);

        await handler.Handle(command, CancellationToken.None);

        // Aucun besoin d'interroger FindUserByEmailAsync : la nouvelle valeur normalisée est
        // strictement identique à celle déjà enregistrée sur CE compte.
        await authStore.DidNotReceive().FindUserByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Unknown_Current_User_Should_Be_Rejected()
    {
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.UserId.Returns((Guid?)null);

        var authStore = Substitute.For<IAuthStore>();
        var passwordHasher = Substitute.For<IPasswordHasher>();

        var handler = new ChangeUserEmailCommandHandler(
            currentUserService, authStore, passwordHasher, NullLogger<ChangeUserEmailCommandHandler>.Instance);

        var command = new ChangeUserEmailCommand("nouveau.email@sama-ecole.sn", CorrectCurrentPassword);

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
