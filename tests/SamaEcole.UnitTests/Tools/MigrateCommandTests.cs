using FluentAssertions;
using SamaEcole.Tools.Commands;
using Xunit;

namespace SamaEcole.UnitTests.Tools;

/// <summary>
/// Résolution de la chaîne de connexion de la commande `migrate`. Le comportement critique est le
/// REFUS de tout repli : cette commande tourne en production avec le rôle propriétaire, et une
/// variable d'environnement oubliée doit interrompre le déploiement — jamais faire migrer
/// silencieusement une base locale, ou pire une base voisine.
///
/// Ces tests manipulent une variable d'environnement de PROCESSUS : ils sont regroupés dans une même
/// collection non parallélisée, sans quoi deux tests concurrents se voleraient la valeur.
/// </summary>
[Collection(nameof(MigrateCommandTests))]
[CollectionDefinition(nameof(MigrateCommandTests), DisableParallelization = true)]
public class MigrateCommandTests : IDisposable
{
    private const string Variable = "ConnectionStrings__Migrations";

    private readonly string? _original = Environment.GetEnvironmentVariable(Variable);

    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _original);

    [Fact]
    public void An_Explicit_Argument_Wins_Over_The_Environment()
    {
        Environment.SetEnvironmentVariable(Variable, "Host=depuis-env");

        MigrateCommand.ResolveConnectionString("Host=explicite").Should().Be("Host=explicite");
    }

    [Fact]
    public void The_Environment_Variable_Is_Used_When_No_Argument_Is_Given()
    {
        Environment.SetEnvironmentVariable(Variable, "Host=depuis-env");

        MigrateCommand.ResolveConnectionString(null).Should().Be("Host=depuis-env");
    }

    /// <summary>
    /// Le cas qui compte : rien de configuré → erreur explicite, jamais une chaîne par défaut.
    /// DesignTimeDbContextFactory, elle, retombe sur la base de dev — c'est acceptable pour
    /// l'outillage local de `dotnet ef`, jamais pour un déploiement.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Missing_Connection_String_Fails_Instead_Of_Falling_Back(string? environmentValue)
    {
        Environment.SetEnvironmentVariable(Variable, environmentValue);

        var act = () => MigrateCommand.ResolveConnectionString(null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__Migrations*");
    }

    /// <summary>
    /// Un argument vide ou blanc n'est pas une surcharge : on retombe sur l'environnement, sinon on
    /// échoue. Sans quoi `--connection ""` migrerait sur une chaîne vide.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Argument_Does_Not_Count_As_An_Override(string argument)
    {
        Environment.SetEnvironmentVariable(Variable, "Host=depuis-env");

        MigrateCommand.ResolveConnectionString(argument).Should().Be("Host=depuis-env");
    }

    /// <summary>
    /// ConnectionStrings__Default (rôle APPLICATIF) n'est jamais un repli : il n'a pas les droits DDL
    /// et ne doit pas les acquérir. Un déploiement mal configuré doit échouer bruyamment.
    /// </summary>
    [Fact]
    public void The_Application_Role_Connection_String_Is_Never_Used_As_A_Fallback()
    {
        Environment.SetEnvironmentVariable(Variable, null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=role-applicatif");

        try
        {
            var act = () => MigrateCommand.ResolveConnectionString(null);

            act.Should().Throw<InvalidOperationException>(
                "le rôle applicatif ne doit jamais servir de repli pour appliquer des migrations");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        }
    }
}
