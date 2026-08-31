using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools.Commands.RevertToTest;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Garantie de sécurité : « repasser en mode test » ne doit être possible que sur un environnement
/// jetable. En production, la route n'est même pas montée (Program.cs) — mais le Handler porte la
/// MÊME garde en défense de profondeur, et elle est vérifiée AVANT tout accès à la base. Ce test
/// prouve qu'un mauvais câblage (route montée par erreur) ne suffirait pas à quitter le mode réel.
/// </summary>
public class RevertToTestCommandHandlerGuardTests
{
    private sealed class SandboxMode(bool enabled) : ISandboxModeProvider
    {
        public bool RevertToTestEnabled => enabled;
    }

    [Fact]
    public async Task When_Revert_Is_Disabled_The_Handler_Refuses_Before_Touching_Anything()
    {
        // dbContext / tenant / currentUser sont volontairement null : la garde de drapeau tombe
        // AVANT qu'ils ne soient déréférencés. Si un jour ce n'était plus le cas, ce test lèverait un
        // NullReferenceException au lieu du NotFoundException attendu — et signalerait la régression.
        var handler = new RevertToTestCommandHandler(
            dbContext: null!,
            tenantProvider: null!,
            currentUser: null!,
            sandboxMode: new SandboxMode(enabled: false),
            logger: NullLogger<RevertToTestCommandHandler>.Instance);

        var act = () => handler.Handle(new RevertToTestCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>(
            "en production, quitter le mode réel n'existe pas — 404, comme une route absente");
    }
}
