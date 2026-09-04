using SamaEcole.Tools.Commands;

// Outils internes (voir README.md de ce dossier). Un argument = une commande ; le code retour est le
// contrat avec le déploiement (0 = succès, non nul = arrêt du déploiement).
return args switch
{
    ["migrate", ..] => await MigrateCommand.RunAsync(ConnectionArgument(args), Console.Out, Console.Error),
    ["seed-superadmin", ..] => await SeedSuperAdminCommand.RunAsync(args, Console.Out, Console.Error),
    _ => Usage()
};

/// <summary>--connection &lt;chaîne&gt; : surcharge explicite, sinon ConnectionStrings__Migrations.</summary>
static string? ConnectionArgument(string[] args)
{
    var index = Array.IndexOf(args, "--connection");

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine("""
        SamaEcole.Tools

        Commandes :
          migrate [--connection <chaîne>]   Applique les migrations EF Core en attente.
                                            À défaut d'argument, lit ConnectionStrings__Migrations
                                            (rôle propriétaire sama_ecole — jamais le rôle applicatif).

          seed-superadmin --email <adresse> --password <mot de passe> [--name <nom>] [--connection <chaîne>]
                                            Crée le premier compte Super Admin s'il n'existe pas déjà
                                            (idempotent par e-mail). Même résolution de connexion que
                                            migrate — rôle propriétaire requis.
        """);

    return 1;
}
