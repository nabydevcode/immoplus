namespace ImmoPlus.Api.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Ressources protégées par un verrou PostgreSQL. Les valeurs servent de clé : ne jamais les renuméroter.
/// </summary>
public enum RessourceVerrouillee
{
    /// <summary>Logement : attribution d'un dossier, création d'une réservation sur ses dates.</summary>
    Appartement = 1,
    /// <summary>Réservation : paiements, annulation, remboursement, expiration, réponse de l'hôte.</summary>
    Reservation = 2,
    /// <summary>Financement de l'avance : échéancier et paiement des mensualités.</summary>
    Financement = 3,
    /// <summary>Location : paiement des loyers.</summary>
    Location = 4,
    /// <summary>Traitement planifié (id : VerrouPlanifie.Rappels…) : un seul exécutant sur l'ensemble des instances.</summary>
    ServicePlanifie = 5,
    /// <summary>Compte utilisateur : confirmation de l'adresse email (admins prévenus une seule fois).</summary>
    Utilisateur = 6
}

/// <summary>
/// Sérialisation des opérations concurrentes sur une même ressource. Avec SQLite, une transaction prenait le verrou
/// d'écriture de toute la base ; PostgreSQL (Read Committed) laisse deux transactions lire le même « reste dû » en même
/// temps. Un verrou consultatif de transaction (pg_advisory_xact_lock) fait attendre la seconde jusqu'à la validation
/// ou l'annulation de la première ; ses lectures suivantes voient alors les données à jour. Il ne bloque ni les simples
/// lectures, ni les autres ressources, et se libère tout seul à la fin de la transaction (même en cas d'erreur).
/// À appeler au début de la transaction, AVANT de lire les données à contrôler.
/// </summary>
public static class VerrousBase
{
    public static async Task VerrouillerAsync(this DatabaseFacade database, RessourceVerrouillee ressource, int id,
        CancellationToken annulation = default)
    {
        if (database.CurrentTransaction is null)
        {
            // Hors transaction, le verrou serait relâché dès la fin de la requête : erreur de programmation.
            throw new InvalidOperationException("Un verrou de ressource doit être pris dans une transaction");
        }
        await database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({(int)ressource}, {id})", annulation);
    }

    /// <summary>Plusieurs ressources du même type, toujours dans l'ordre croissant (deux traitements ne s'interbloquent pas).</summary>
    public static async Task VerrouillerAsync(this DatabaseFacade database, RessourceVerrouillee ressource, IEnumerable<int> ids,
        CancellationToken annulation = default)
    {
        foreach (var id in ids.Distinct().Order())
        {
            await database.VerrouillerAsync(ressource, id, annulation);
        }
    }
}

/// <summary>
/// Verrou consultatif de session PostgreSQL (pg_try_advisory_lock) pour les traitements planifiés : une seule instance
/// de l'API les exécute à la fois. Choisi plutôt qu'un verrou Redis : il fonctionne aussi sans Redis, il est porté par la
/// base que toutes les instances partagent déjà, et il est libéré automatiquement si l'instance s'arrête (connexion fermée),
/// sans durée d'expiration à deviner pour un traitement long. Connexion dédiée sans pool : rien ne survit à la libération.
/// </summary>
public sealed class VerrouPlanifie : IAsyncDisposable
{
    /// <summary>Rappels des échéances, expirations des réservations, purges (RappelsServices).</summary>
    public const int Rappels = 1;

    /// <summary>Maintenance fréquente des réservations et des loyers (MaintenanceServices).</summary>
    public const int Maintenance = 2;

    private readonly Npgsql.NpgsqlConnection _connexion;
    private readonly int _id;

    private VerrouPlanifie(Npgsql.NpgsqlConnection connexion, int id)
    {
        _connexion = connexion;
        _id = id;
    }

    /// <summary>Le verrou si personne ne le détient, sinon null (une autre instance travaille déjà).</summary>
    public static async Task<VerrouPlanifie?> EssayerAsync(string chaineConnexion, int id, CancellationToken annulation = default)
    {
        var connexion = new Npgsql.NpgsqlConnection(new Npgsql.NpgsqlConnectionStringBuilder(chaineConnexion) { Pooling = false }.ConnectionString);
        try
        {
            await connexion.OpenAsync(annulation);
            await using var commande = new Npgsql.NpgsqlCommand("SELECT pg_try_advisory_lock(@type, @id)", connexion);
            commande.Parameters.AddWithValue("type", (int)RessourceVerrouillee.ServicePlanifie);
            commande.Parameters.AddWithValue("id", id);
            if (await commande.ExecuteScalarAsync(annulation) is true)
            {
                return new VerrouPlanifie(connexion, id);
            }
        }
        catch
        {
            await connexion.DisposeAsync();
            throw;
        }
        await connexion.DisposeAsync();
        return null;
    }

    /// <summary>Attend le verrou (essai toutes les 500 ms) : exécution demandée par un admin pendant un passage planifié.</summary>
    public static async Task<VerrouPlanifie> AttendreAsync(string chaineConnexion, int id, CancellationToken annulation = default)
    {
        while (true)
        {
            if (await EssayerAsync(chaineConnexion, id, annulation) is { } verrou)
            {
                return verrou;
            }
            await Task.Delay(500, annulation);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var commande = new Npgsql.NpgsqlCommand("SELECT pg_advisory_unlock(@type, @id)", _connexion);
            commande.Parameters.AddWithValue("type", (int)RessourceVerrouillee.ServicePlanifie);
            commande.Parameters.AddWithValue("id", _id);
            await commande.ExecuteScalarAsync();
        }
        catch (Npgsql.NpgsqlException)
        {
            // Connexion perdue : PostgreSQL a déjà libéré le verrou avec la session.
        }
        await _connexion.DisposeAsync();
    }
}

/// <summary>Lecture des erreurs PostgreSQL remontées par EF Core.</summary>
public static class ErreursBase
{
    /// <summary>Index unique de l'email, insensible à la casse (lower("Email")), créé par la migration InitialePostgres.</summary>
    public const string IndexEmailUnique = "IX_Utilisateurs_Email_Minuscules";

    /// <summary>Violation d'une contrainte d'unicité (code 23505), éventuellement d'un index précis.</summary>
    public static bool EstViolationUnicite(DbUpdateException exception, string? index = null) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
        && (index is null || pg.ConstraintName == index);
}
