// Migration des données réelles d'ImmoPlus : SQLite (copie cohérente de immoplus.db) → PostgreSQL.
//
// 1. Copie cohérente de la base, faite par le conteneur (jamais « cp » d'une base ouverte) :
//      docker compose exec -T api sqlite3 immoplus.db ".backup '/src/copie-migration.db'"
//    puis déplacer api/ImmoPlus.Api/copie-migration.db hors du dépôt.
// 2. Migration (base cible vide ; les migrations EF y sont appliquées d'abord) :
//      dotnet run --project api/ImmoPlus.MigrationDonnees -- --source /chemin/copie-migration.db \
//        --cible "Host=localhost;Port=5433;Database=immoplus;Username=immoplus" --fichier-env infra/postgres.env
//    --remplacer : vide d'abord la base cible (TRUNCATE) au lieu de refuser une base non vide.
//    --controle-seul : ne copie rien, compare seulement la source et la cible.
// Le mot de passe est lu dans le fichier d'environnement (POSTGRES_PASSWORD) et n'est jamais affiché.
//
// Les Id sont conservés, les séquences recalées (sans jamais réutiliser un Id déjà attribué par SQLite), le tout dans une
// seule transaction : en cas d'erreur, la cible reste vide. Contrôles avant/après : lignes et dernier Id par table, sommes
// de tous les montants, utilisateurs par rôle et statut, puis comparaison ligne à ligne de toutes les colonnes.
// Code de sortie 0 seulement si tout est identique.

using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

// Dernière migration SQLite : la source doit avoir exactement le schéma du modèle actuel.
const string DerniereMigrationSqlite = "20261014064108_AjouterJournalSignalementsIdentiteAvisLocation";

var options = LireArguments(args);
if (options is null)
{
    Console.Error.WriteLine("Usage : --source <copie.db> --cible <chaîne PostgreSQL> [--fichier-env infra/postgres.env] [--remplacer | --controle-seul]");
    return 2;
}

var cheminSource = Path.GetFullPath(options.Source);
if (!File.Exists(cheminSource))
{
    Console.Error.WriteLine($"Source introuvable : {cheminSource}");
    return 2;
}
if (string.Equals(Path.GetFileName(cheminSource), "immoplus.db", StringComparison.OrdinalIgnoreCase))
{
    // La base réelle reste la source de vérité : on ne travaille que sur une copie faite par « .backup ».
    Console.Error.WriteLine("Refus : indiquez une COPIE de la base (sqlite3 .backup), jamais immoplus.db elle-même.");
    return 2;
}
var chaineSource = new SqliteConnectionStringBuilder { DataSource = cheminSource, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();

var chaineCible = new NpgsqlConnectionStringBuilder(options.Cible);
if (options.FichierEnv is not null)
{
    var motDePasse = File.ReadAllLines(options.FichierEnv)
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal))
        .Select(l => l["POSTGRES_PASSWORD=".Length..].Trim().Trim('"'))
        .FirstOrDefault();
    if (string.IsNullOrEmpty(motDePasse))
    {
        Console.Error.WriteLine($"POSTGRES_PASSWORD absent de {options.FichierEnv}");
        return 2;
    }
    chaineCible.Password = motDePasse;
}
Console.WriteLine($"Source : {cheminSource} (lecture seule)");
Console.WriteLine($"Cible  : {chaineCible.Host}:{chaineCible.Port}/{chaineCible.Database} (utilisateur {chaineCible.Username})");

await using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(chaineSource).Options);
var optionsCible = new DbContextOptionsBuilder<AppDbContext>();
AppDbContext.Configurer(optionsCible, chaineCible.ConnectionString);
await using var cible = new AppDbContext(optionsCible.Options);
source.Database.SetCommandTimeout(300);
cible.Database.SetCommandTimeout(300);

await using var sqlite = new SqliteConnection(chaineSource);
await sqlite.OpenAsync();
var migrationSource = (string?)await Scalaire(sqlite, "SELECT MAX(MigrationId) FROM __EFMigrationsHistory");
if (migrationSource != DerniereMigrationSqlite)
{
    Console.Error.WriteLine($"Schéma source inattendu : dernière migration « {migrationSource} », attendu « {DerniereMigrationSqlite} »");
    return 3;
}

// Tables dans l'ordre des clés étrangères (parents d'abord).
var types = TrierParDependances(cible.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.GetTableName() is not null).ToList());

if (!options.ControleSeul)
{
    Console.WriteLine("\nApplication des migrations PostgreSQL…");
    await cible.Database.MigrateAsync();

    await using var transaction = await cible.Database.BeginTransactionAsync();
    var nonVides = new List<string>();
    foreach (var type in types)
    {
        if (await Compter(cible, type) > 0)
        {
            nonVides.Add(type.GetTableName()!);
        }
    }
    if (nonVides.Count > 0)
    {
        if (!options.Remplacer)
        {
            Console.Error.WriteLine($"La base cible contient déjà des données ({string.Join(", ", nonVides)}). "
                + "Rien n'a été modifié. Relancez avec --remplacer pour la vider d'abord, ou --controle-seul pour comparer.");
            return 4;
        }
        Console.WriteLine($"--remplacer : vidage de {string.Join(", ", nonVides)}");
        var tables = string.Join(", ", types.Select(t => $"\"{t.GetTableName()}\""));
        // Noms de tables issus du modèle EF (aucune saisie) : pas de risque d'injection.
        await Scalaire(cible, $"TRUNCATE {tables} RESTART IDENTITY CASCADE");
    }

    Console.WriteLine("\nCopie des données (Id conservés) :");
    var copier = typeof(Outils).GetMethod(nameof(Outils.CopierAsync))!;
    foreach (var type in types)
    {
        var nombre = await (Task<int>)copier.MakeGenericMethod(type.ClrType).Invoke(null, [source, cible])!;
        Console.WriteLine($"  {type.GetTableName(),-22} {nombre,6} ligne(s)");
    }

    // Séquences : prochain Id = max(dernier Id, dernier Id jamais attribué par SQLite) + 1, pour ne jamais réattribuer
    // l'Id d'une ligne supprimée (comportement AUTOINCREMENT de SQLite).
    var sequencesSqlite = new Dictionary<string, long>();
    await using (var commande = new SqliteCommand("SELECT name, seq FROM sqlite_sequence", sqlite))
    await using (var lecteur = await commande.ExecuteReaderAsync())
    {
        while (await lecteur.ReadAsync())
        {
            sequencesSqlite[lecteur.GetString(0)] = lecteur.GetInt64(1);
        }
    }
    Console.WriteLine("\nSéquences recalées (prochain Id) :");
    foreach (var type in types.Where(AIdentite))
    {
        var table = type.GetTableName()!;
        var maxId = await Scalaire(cible, $"SELECT COALESCE(MAX(\"Id\"), 0) FROM \"{table}\"");
        var suivant = Math.Max(Convert.ToInt64(maxId), sequencesSqlite.GetValueOrDefault(table)) + 1;
        await Scalaire(cible, $"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), {suivant}, false)");
        Console.WriteLine($"  {table,-22} {suivant,6}");
    }

    await transaction.CommitAsync();
    Console.WriteLine("\nCopie validée (transaction unique).");
}

// --- Contrôles avant / après ---
Console.WriteLine("\nContrôles (source SQLite / cible PostgreSQL) :");
var statsSource = await Outils.StatistiquesSqliteAsync(sqlite, types);
var statsCible = await Outils.StatistiquesPostgresAsync(cible, types);
var ecarts = 0;
Console.WriteLine($"  {"Contrôle",-55} {"Source",20} {"Cible",20}");
foreach (var cle in statsSource.Keys.Union(statsCible.Keys).Order(StringComparer.Ordinal))
{
    var a = statsSource.GetValueOrDefault(cle, "(absent)");
    var b = statsCible.GetValueOrDefault(cle, "(absent)");
    var identique = a == b;
    ecarts += identique ? 0 : 1;
    Console.WriteLine($"  {cle,-55} {a,20} {b,20}{(identique ? "" : "   <-- ÉCART")}");
}

Console.WriteLine("\nComparaison ligne à ligne (toutes les colonnes) :");
var comparer = typeof(Outils).GetMethod(nameof(Outils.ComparerLignesAsync))!;
foreach (var type in types)
{
    var (lignes, differences, exemples) = await (Task<(int, int, List<string>)>)comparer.MakeGenericMethod(type.ClrType).Invoke(null, [source, cible, type])!;
    ecarts += differences;
    Console.WriteLine($"  {type.GetTableName(),-22} {lignes,6} ligne(s) comparée(s), {differences} différence(s)");
    foreach (var exemple in exemples)
    {
        Console.WriteLine($"      {exemple}");
    }
}

Console.WriteLine(ecarts == 0 ? "\nRÉSULTAT : identique." : $"\nRÉSULTAT : {ecarts} écart(s) — ne pas basculer l'API sur PostgreSQL.");
return ecarts == 0 ? 0 : 1;

static bool AIdentite(IEntityType type) =>
    type.FindPrimaryKey() is { Properties: [{ Name: "Id", ClrType: var t } p] } && t == typeof(int) && p.ValueGenerated == ValueGenerated.OnAdd;

static async Task<long> Compter(AppDbContext contexte, IEntityType type) =>
    Convert.ToInt64(await Scalaire(contexte, $"SELECT COUNT(*) FROM \"{type.GetTableName()}\""));

static async Task<object?> Scalaire(object connexionOuContexte, string sql)
{
    if (connexionOuContexte is SqliteConnection sqlite)
    {
        await using var commande = new SqliteCommand(sql, sqlite);
        var valeur = await commande.ExecuteScalarAsync();
        return valeur is DBNull ? null : valeur;
    }
    var contexte = (AppDbContext)connexionOuContexte;
    var connexion = contexte.Database.GetDbConnection();
    if (connexion.State != System.Data.ConnectionState.Open)
    {
        await contexte.Database.OpenConnectionAsync();
    }
    await using var cmd = connexion.CreateCommand();
    cmd.CommandText = sql;
    cmd.Transaction = contexte.Database.CurrentTransaction?.GetDbTransaction();
    var resultat = await cmd.ExecuteScalarAsync();
    return resultat is DBNull ? null : resultat;
}

static List<IEntityType> TrierParDependances(List<IEntityType> types)
{
    var restants = types.OrderBy(t => t.GetTableName(), StringComparer.Ordinal).ToList();
    var tries = new List<IEntityType>();
    while (restants.Count > 0)
    {
        var pret = restants.FirstOrDefault(t => t.GetForeignKeys()
            .Select(fk => fk.PrincipalEntityType)
            .All(p => p == t || tries.Contains(p)))
            ?? throw new InvalidOperationException("Dépendance circulaire entre tables : ordre de copie impossible");
        tries.Add(pret);
        restants.Remove(pret);
    }
    return tries;
}

static OptionsMigration? LireArguments(string[] args)
{
    string? source = null, cible = null, env = null;
    bool remplacer = false, controleSeul = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--source" when i + 1 < args.Length: source = args[++i]; break;
            case "--cible" when i + 1 < args.Length: cible = args[++i]; break;
            case "--fichier-env" when i + 1 < args.Length: env = args[++i]; break;
            case "--remplacer": remplacer = true; break;
            case "--controle-seul": controleSeul = true; break;
            default: return null;
        }
    }
    return source is null || cible is null || (remplacer && controleSeul) ? null : new OptionsMigration(source, cible, env, remplacer, controleSeul);
}

record OptionsMigration(string Source, string Cible, string? FichierEnv, bool Remplacer, bool ControleSeul);

static class Outils
{
    public static async Task<int> CopierAsync<T>(AppDbContext source, AppDbContext cible) where T : class
    {
        var lignes = await source.Set<T>().AsNoTracking().ToListAsync();
        cible.Set<T>().AddRange(lignes);
        await cible.SaveChangesAsync();
        cible.ChangeTracker.Clear();
        return lignes.Count;
    }

    /// <summary>
    /// Toutes les propriétés de chaque ligne, source et cible, appariées par clé primaire. Les dates sont comparées à la
    /// microseconde près (précision de PostgreSQL ; SQLite garde le dixième de microseconde), les montants en valeur.
    /// </summary>
    public static async Task<(int Lignes, int Differences, List<string> Exemples)> ComparerLignesAsync<T>(AppDbContext source, AppDbContext cible, IEntityType type)
        where T : class
    {
        var proprietes = type.GetProperties().Where(p => p.PropertyInfo is not null).ToList();
        var cle = type.FindPrimaryKey()!.Properties;
        string Cle(T ligne) => string.Join("|", cle.Select(p => Convert.ToString(p.PropertyInfo!.GetValue(ligne), CultureInfo.InvariantCulture)));

        var lignesSource = (await source.Set<T>().AsNoTracking().ToListAsync()).ToDictionary(Cle);
        var lignesCible = (await cible.Set<T>().AsNoTracking().ToListAsync()).ToDictionary(Cle);
        var differences = 0;
        var exemples = new List<string>();
        foreach (var id in lignesSource.Keys.Union(lignesCible.Keys))
        {
            string? ecart = null;
            if (!lignesSource.TryGetValue(id, out var a))
            {
                ecart = $"Id {id} : absent de la source";
            }
            else if (!lignesCible.TryGetValue(id, out var b))
            {
                ecart = $"Id {id} : absent de la cible";
            }
            else
            {
                var colonnes = proprietes.Where(p => !Egales(p.PropertyInfo!.GetValue(a), p.PropertyInfo.GetValue(b))).Select(p => p.Name).ToList();
                if (colonnes.Count > 0)
                {
                    ecart = $"Id {id} : {string.Join(", ", colonnes)}";
                }
            }
            if (ecart is not null)
            {
                differences++;
                if (exemples.Count < 5)
                {
                    exemples.Add(ecart);
                }
            }
        }
        return (lignesSource.Count, differences, exemples);
    }

    private static bool Egales(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (DateTime x, DateTime y) => Math.Abs((x - y).Ticks) < 10,
        (decimal x, decimal y) => x == y,
        _ => Equals(a, b)
    };

    // --- Statistiques indépendantes d'EF : SQL brut des deux côtés ---

    private static IEnumerable<IProperty> Montants(IEntityType type) =>
        type.GetProperties().Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(decimal));

    public static async Task<SortedDictionary<string, string>> StatistiquesSqliteAsync(SqliteConnection connexion, List<IEntityType> types)
    {
        var resultat = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var table = type.GetTableName()!;
            resultat[$"{table} : lignes"] = Convert.ToString(await Lire(connexion, $"SELECT COUNT(*) FROM \"{table}\""), CultureInfo.InvariantCulture)!;
            if (type.FindProperty("Id") is { ClrType: var t } && t == typeof(int))
            {
                resultat[$"{table} : dernier Id"] = Convert.ToString(await Lire(connexion, $"SELECT MAX(\"Id\") FROM \"{table}\""), CultureInfo.InvariantCulture) ?? "-";
            }
            foreach (var montant in Montants(type))
            {
                // decimal stocké en TEXT par EF/SQLite : additionné en C# (exact), pas par SUM() (flottant).
                var colonne = montant.GetColumnName();
                decimal somme = 0;
                await using var commande = new SqliteCommand($"SELECT \"{colonne}\" FROM \"{table}\" WHERE \"{colonne}\" IS NOT NULL", connexion);
                await using var lecteur = await commande.ExecuteReaderAsync();
                while (await lecteur.ReadAsync())
                {
                    somme += decimal.Parse(lecteur.GetString(0), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                resultat[$"{table} : somme {colonne}"] = Format(somme);
            }
        }
        await AjouterUtilisateursAsync(resultat, connexion, "SELECT \"Role\", \"Statut\", COUNT(*) FROM \"Utilisateurs\" GROUP BY 1, 2");
        return resultat;
    }

    public static async Task<SortedDictionary<string, string>> StatistiquesPostgresAsync(AppDbContext contexte, List<IEntityType> types)
    {
        var connexion = (NpgsqlConnection)contexte.Database.GetDbConnection();
        if (connexion.State != System.Data.ConnectionState.Open)
        {
            await connexion.OpenAsync();
        }
        var resultat = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var table = type.GetTableName()!;
            resultat[$"{table} : lignes"] = Convert.ToString(await Lire(connexion, $"SELECT COUNT(*) FROM \"{table}\""), CultureInfo.InvariantCulture)!;
            if (type.FindProperty("Id") is { ClrType: var t } && t == typeof(int))
            {
                resultat[$"{table} : dernier Id"] = Convert.ToString(await Lire(connexion, $"SELECT MAX(\"Id\") FROM \"{table}\""), CultureInfo.InvariantCulture) ?? "-";
            }
            foreach (var montant in Montants(type))
            {
                var colonne = montant.GetColumnName();
                var somme = await Lire(connexion, $"SELECT COALESCE(SUM(\"{colonne}\"), 0) FROM \"{table}\"");
                resultat[$"{table} : somme {colonne}"] = Format(Convert.ToDecimal(somme, CultureInfo.InvariantCulture));
            }
        }
        await AjouterUtilisateursAsync(resultat, connexion, "SELECT \"Role\", \"Statut\", COUNT(*) FROM \"Utilisateurs\" GROUP BY 1, 2");
        return resultat;
    }

    private static async Task AjouterUtilisateursAsync(SortedDictionary<string, string> resultat, System.Data.Common.DbConnection connexion, string sql)
    {
        await using var commande = connexion.CreateCommand();
        commande.CommandText = sql;
        await using var lecteur = await commande.ExecuteReaderAsync();
        while (await lecteur.ReadAsync())
        {
            var role = (RoleUtilisateur)Convert.ToInt32(lecteur.GetValue(0), CultureInfo.InvariantCulture);
            var statut = (StatutUtilisateur)Convert.ToInt32(lecteur.GetValue(1), CultureInfo.InvariantCulture);
            resultat[$"Utilisateurs {role} / {statut}"] = Convert.ToString(lecteur.GetValue(2), CultureInfo.InvariantCulture)!;
        }
    }

    private static async Task<object?> Lire(System.Data.Common.DbConnection connexion, string sql)
    {
        await using var commande = connexion.CreateCommand();
        commande.CommandText = sql;
        var valeur = await commande.ExecuteScalarAsync();
        return valeur is DBNull ? null : valeur;
    }

    private static string Format(decimal valeur) => valeur.ToString("#,0.##", CultureInfo.InvariantCulture);
}
