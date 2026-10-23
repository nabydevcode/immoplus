namespace ImmoPlus.Api.Tests.Outils;

using Npgsql;
using Testcontainers.PostgreSql;

/// <summary>
/// Serveur PostgreSQL des tests. Par défaut, un conteneur jetable (Testcontainers, Docker requis) démarré une fois pour
/// tout le lancement de « dotnet test » et détruit à la fin. Variable IMMOPLUS_TESTS_POSTGRES : chaîne de connexion
/// d'administration d'un serveur existant (ex. le service compose : Host=localhost;Port=5433;Username=immoplus;
/// Password=…;Database=postgres) — les tests n'y créent et n'y suppriment que des bases « immoplus_test_… ».
/// </summary>
public static class ServeurPostgresTests
{
    private static readonly Lazy<Task<string>> ChaineAdministration = new(DemarrerAsync);

    private static async Task<string> DemarrerAsync()
    {
        var existante = Environment.GetEnvironmentVariable("IMMOPLUS_TESTS_POSTGRES");
        if (!string.IsNullOrWhiteSpace(existante))
        {
            return existante;
        }
        var conteneur = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await conteneur.StartAsync();
        return conteneur.GetConnectionString();
    }

    /// <summary>Crée une base vide et renvoie sa chaîne de connexion (les migrations s'appliquent au démarrage de l'API).</summary>
    public static async Task<string> CreerBaseAsync(string nom)
    {
        var administration = await ChaineAdministration.Value;
        await using (var connexion = new NpgsqlConnection(administration))
        {
            await connexion.OpenAsync();
            await using var commande = new NpgsqlCommand($"CREATE DATABASE \"{nom}\"", connexion);
            await commande.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(administration) { Database = nom }.ConnectionString;
    }

    public static async Task SupprimerBaseAsync(string nom, string chaineBase)
    {
        if (!nom.StartsWith("immoplus_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refus de supprimer la base « {nom} » : seules les bases de test sont supprimées");
        }
        // Connexions du pool encore ouvertes sur cette base : fermées avant la suppression.
        await using (var source = NpgsqlDataSource.Create(chaineBase)) { }
        NpgsqlConnection.ClearAllPools();
        await using var connexion = new NpgsqlConnection(await ChaineAdministration.Value);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{nom}\" WITH (FORCE)", connexion);
        await commande.ExecuteNonQueryAsync();
    }
}
