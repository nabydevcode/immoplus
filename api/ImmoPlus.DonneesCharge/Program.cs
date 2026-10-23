// Données de charge d'ImmoPlus : remplit une base PostgreSQL JETABLE avec un volume réaliste, pour mesurer les requêtes
// (EXPLAIN ANALYZE) et faire les tests de charge (k6). Jamais la base réelle : « immoplus » est refusée, et le nom de la
// base cible doit commencer par « immoplus_charge » ou « immoplus_perf ».
//
//   dotnet run --project api/ImmoPlus.DonneesCharge -c Release -- \
//     --cible "Host=localhost;Port=5433;Database=immoplus_charge;Username=immoplus" --fichier-env infra/postgres.env \
//     [--remplacer] [--utilisateurs 100000] [--bailleurs 5000] [--logements 20000] [--reservations 50000] \
//     [--candidatures 10000] [--messages 200000] [--favoris 50000] [--photos-par-logement 3] [--graine 42] \
//     [--s3-fichier-env infra/stockage.env --s3-url http://localhost:9000 --s3-bucket immoplus-charge]
//
// - La base est créée si besoin (--remplacer : supprimée puis recréée), puis les migrations EF de l'API sont appliquées :
//   schéma et index identiques à l'API. Insertion massive par COPY binaire, Id fixés puis séquences recalées, ANALYZE final.
// - Tous les comptes sont en @test-charge.local avec le même mot de passe connu (MotDePasseCharge) : admin@test-charge.local,
//   bailleur1@test-charge.local (40 logements), locataire1@test-charge.local (réservations, candidatures, favoris)…
// - Photos : toutes les lignes pointent vers quelques objets de référence partagés (photos/charge/reference-N.jpg et leurs
//   miniatures), envoyés dans le bucket indiqué (jamais « immoplus ») si les options --s3-* sont fournies.
// Le mot de passe PostgreSQL est lu dans le fichier d'environnement (POSTGRES_PASSWORD) et n'est jamais affiché.

using System.Diagnostics;
using System.Globalization;
using Amazon.S3;
using Amazon.S3.Model;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SkiaSharp;

const string MotDePasseCharge = "ChargeImmo!2026";
const int NombrePhotosReference = 5;

var options = Options.Lire(args);
if (options is null)
{
    Console.Error.WriteLine("Usage : --cible <chaîne PostgreSQL> [--fichier-env infra/postgres.env] [--remplacer] [--utilisateurs N] "
        + "[--bailleurs N] [--logements N] [--reservations N] [--candidatures N] [--messages N] [--favoris N] [--photos-par-logement N] "
        + "[--graine N] [--s3-fichier-env infra/stockage.env] [--s3-url URL] [--s3-bucket immoplus-charge]");
    return 2;
}

var chaine = new NpgsqlConnectionStringBuilder(options.Cible);
var nomBase = chaine.Database ?? "";
if (string.Equals(nomBase, "immoplus", StringComparison.OrdinalIgnoreCase)
    || !(nomBase.StartsWith("immoplus_charge", StringComparison.Ordinal) || nomBase.StartsWith("immoplus_perf", StringComparison.Ordinal)))
{
    Console.Error.WriteLine($"Refus : la base cible « {nomBase} » n'est pas une base jetable. Utilisez immoplus_charge ou immoplus_perf_… (jamais immoplus).");
    return 2;
}
if (options.FichierEnv is not null)
{
    var motDePasse = LireVariable(options.FichierEnv, "POSTGRES_PASSWORD");
    if (string.IsNullOrEmpty(motDePasse))
    {
        Console.Error.WriteLine($"POSTGRES_PASSWORD introuvable dans {options.FichierEnv}");
        return 2;
    }
    chaine.Password = motDePasse;
}
if (options.Bailleurs < 1 || options.Utilisateurs < options.Bailleurs + 2 || options.Logements < 2)
{
    Console.Error.WriteLine("Volumes incohérents : il faut au moins 1 bailleur, 1 locataire en plus de l'admin et 2 logements.");
    return 2;
}
if (string.Equals(options.S3Bucket, "immoplus", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Refus : le bucket « immoplus » est celui des vrais fichiers. Utilisez immoplus-charge.");
    return 2;
}

var chrono = Stopwatch.StartNew();
Console.WriteLine($"Base cible : {nomBase} sur {chaine.Host}:{chaine.Port}");

// --- 1. Base (création ou remplacement) et schéma de l'API ---
var maintenance = new NpgsqlConnectionStringBuilder(chaine.ConnectionString) { Database = "postgres", Pooling = false };
await using (var connexion = new NpgsqlConnection(maintenance.ConnectionString))
{
    await connexion.OpenAsync();
    var existe = await Scalaire<bool>(connexion, $"SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = '{nomBase}')");
    if (existe && options.Remplacer)
    {
        Console.WriteLine("Suppression de l'ancienne base (--remplacer)…");
        await Executer(connexion, $"DROP DATABASE \"{nomBase}\" WITH (FORCE)");
        existe = false;
    }
    if (!existe)
    {
        await Executer(connexion, $"CREATE DATABASE \"{nomBase}\"");
    }
}
chaine.Pooling = false;
await using (var contexte = new AppDbContext(((DbContextOptionsBuilder<AppDbContext>)AppDbContext.Configurer(new DbContextOptionsBuilder<AppDbContext>(), chaine.ConnectionString)).Options))
{
    await contexte.Database.MigrateAsync();
    if (await contexte.Utilisateurs.AnyAsync())
    {
        Console.Error.WriteLine("La base cible contient déjà des données : relancez avec --remplacer pour la recréer.");
        return 2;
    }
}
Console.WriteLine($"Schéma prêt ({chrono.Elapsed.TotalSeconds:0.0} s)");

// --- 2. Génération ---
var g = new Generateur(options, BCrypt.Net.BCrypt.HashPassword(MotDePasseCharge));
g.Generer();

// --- 3. Insertion massive (COPY binaire), dans une transaction : en cas d'erreur la base reste vide ---
await using (var connexion = new NpgsqlConnection(chaine.ConnectionString))
{
    await connexion.OpenAsync();
    await Executer(connexion, "SET synchronous_commit = off");
    await using var transaction = await connexion.BeginTransactionAsync();
    foreach (var table in g.Tables)
    {
        var debut = chrono.Elapsed;
        await Copier(connexion, table);
        Console.WriteLine($"  {table.Nom,-22} {table.Lignes.Count,9:N0} lignes ({(chrono.Elapsed - debut).TotalSeconds:0.0} s)");
    }
    // Id fixés par l'outil : les séquences repartent après le plus grand (les créations par l'API ne se chevauchent pas).
    foreach (var table in g.Tables.Where(t => t.Colonnes[0] == "Id"))
    {
        await Executer(connexion, $"SELECT setval(pg_get_serial_sequence('\"{table.Nom}\"', 'Id'), GREATEST((SELECT max(\"Id\") FROM \"{table.Nom}\"), 1))");
    }
    await transaction.CommitAsync();
    Console.WriteLine("ANALYZE…");
    await Executer(connexion, "ANALYZE");
    var taille = await Scalaire<string>(connexion, $"SELECT pg_size_pretty(pg_database_size('{nomBase}'))");
    Console.WriteLine($"Taille de la base : {taille}");
}

// --- 4. Photos de référence (facultatif) ---
if (options.S3FichierEnv is not null)
{
    await EnvoyerPhotosAsync(options);
}

Console.WriteLine();
Console.WriteLine($"Terminé en {chrono.Elapsed.TotalSeconds:0.0} s.");
Console.WriteLine($"Comptes (mot de passe « {MotDePasseCharge} ») : admin@test-charge.local, bailleur1@test-charge.local … bailleur{options.Bailleurs}@test-charge.local, "
    + $"locataire1@test-charge.local … locataire{options.Utilisateurs - options.Bailleurs - 1}@test-charge.local");
return 0;

static async Task Copier(NpgsqlConnection connexion, Table table)
{
    var colonnes = string.Join(", ", table.Colonnes.Select(c => $"\"{c}\""));
    await using var import = await connexion.BeginBinaryImportAsync($"COPY \"{table.Nom}\" ({colonnes}) FROM STDIN (FORMAT BINARY)");
    foreach (var ligne in table.Lignes)
    {
        await import.StartRowAsync();
        foreach (var valeur in ligne)
        {
            switch (valeur)
            {
                case null: await import.WriteNullAsync(); break;
                case int i: await import.WriteAsync(i, NpgsqlDbType.Integer); break;
                case string s: await import.WriteAsync(s, NpgsqlDbType.Text); break;
                case bool b: await import.WriteAsync(b, NpgsqlDbType.Boolean); break;
                case decimal d: await import.WriteAsync(d, NpgsqlDbType.Numeric); break;
                case double x: await import.WriteAsync(x, NpgsqlDbType.Double); break;
                // Colonnes « timestamp without time zone » en UTC (comme l'API).
                case DateTime t: await import.WriteAsync(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), NpgsqlDbType.Timestamp); break;
                default: throw new InvalidOperationException($"Type non géré : {valeur.GetType()}");
            }
        }
    }
    await import.CompleteAsync();
}

static async Task Executer(NpgsqlConnection connexion, string sql)
{
    await using var commande = new NpgsqlCommand(sql, connexion) { CommandTimeout = 0 };
    await commande.ExecuteNonQueryAsync();
}

static async Task<T> Scalaire<T>(NpgsqlConnection connexion, string sql)
{
    await using var commande = new NpgsqlCommand(sql, connexion);
    return (T)(await commande.ExecuteScalarAsync())!;
}

static string? LireVariable(string fichier, string nom) =>
    File.ReadAllLines(fichier)
        .Select(l => l.Trim())
        .Where(l => l.StartsWith(nom + "=", StringComparison.Ordinal))
        .Select(l => l[(nom.Length + 1)..].Trim().Trim('"'))
        .FirstOrDefault();

// Quelques JPEG générés (dégradé + numéro) et leurs miniatures, aux clés partagées par toutes les photos de la base de charge.
static async Task EnvoyerPhotosAsync(Options options)
{
    var s3 = new OptionsStockageS3
    {
        Url = options.S3Url ?? LireVariable(options.S3FichierEnv!, "Stockage__S3__Url") ?? "",
        Bucket = options.S3Bucket,
        CleAcces = LireVariable(options.S3FichierEnv!, "Stockage__S3__CleAcces") ?? "",
        CleSecrete = LireVariable(options.S3FichierEnv!, "Stockage__S3__CleSecrete") ?? "",
        Region = LireVariable(options.S3FichierEnv!, "Stockage__S3__Region") ?? "garage"
    };
    using var client = s3.CreerClient();
    for (var n = 1; n <= NombrePhotosReference; n++)
    {
        foreach (var (prefixe, largeur, hauteur) in new[] { (ClesStockage.Photos, 1280, 853), (ClesStockage.Miniatures, 480, 320) })
        {
            using var surface = SKSurface.Create(new SKImageInfo(largeur, hauteur));
            var teinte = (float)(n * 360.0 / NombrePhotosReference);
            using (var pinceau = new SKPaint())
            {
                pinceau.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(largeur, hauteur),
                    [SKColor.FromHsl(teinte, 55, 55), SKColor.FromHsl((teinte + 40) % 360, 45, 30)], SKShaderTileMode.Clamp);
                surface.Canvas.DrawRect(0, 0, largeur, hauteur, pinceau);
            }
            using (var police = new SKFont { Size = hauteur / 6f })
            using (var texte = new SKPaint { Color = SKColors.White, IsAntialias = true })
            {
                surface.Canvas.DrawText($"ImmoPlus {n}", largeur / 12f, hauteur / 2f, SKTextAlign.Left, police, texte);
            }
            using var image = surface.Snapshot();
            using var donnees = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            var cle = ClesStockage.Cle(prefixe, $"charge/reference-{n}.jpg");
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = s3.Bucket,
                Key = cle,
                InputStream = new MemoryStream(donnees.ToArray()),
                ContentType = "image/jpeg"
            });
        }
    }
    Console.WriteLine($"{NombrePhotosReference} photos de référence (et miniatures) envoyées dans le bucket « {s3.Bucket} »");
}

sealed record Table(string Nom, string[] Colonnes)
{
    public List<object?[]> Lignes { get; } = new();
    public void Ajouter(params object?[] valeurs) => Lignes.Add(valeurs);
}

sealed class Options
{
    public string Cible { get; private set; } = "";
    public string? FichierEnv { get; private set; }
    public bool Remplacer { get; private set; }
    public int Utilisateurs { get; private set; } = 100_000;
    public int Bailleurs { get; private set; } = 5_000;
    public int Logements { get; private set; } = 20_000;
    public int Reservations { get; private set; } = 50_000;
    public int Candidatures { get; private set; } = 10_000;
    public int Messages { get; private set; } = 200_000;
    public int Favoris { get; private set; } = 50_000;
    public int PhotosParLogement { get; private set; } = 3;
    public int Graine { get; private set; } = 42;
    public string? S3FichierEnv { get; private set; }
    public string? S3Url { get; private set; }
    public string S3Bucket { get; private set; } = "immoplus-charge";

    public static Options? Lire(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Valeur() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Valeur manquante après {args[i]}");
            int Entier() => int.Parse(Valeur(), CultureInfo.InvariantCulture);
            switch (args[i])
            {
                case "--cible": o.Cible = Valeur(); break;
                case "--fichier-env": o.FichierEnv = Valeur(); break;
                case "--remplacer": o.Remplacer = true; break;
                case "--utilisateurs": o.Utilisateurs = Entier(); break;
                case "--bailleurs": o.Bailleurs = Entier(); break;
                case "--logements": o.Logements = Entier(); break;
                case "--reservations": o.Reservations = Entier(); break;
                case "--candidatures": o.Candidatures = Entier(); break;
                case "--messages": o.Messages = Entier(); break;
                case "--favoris": o.Favoris = Entier(); break;
                case "--photos-par-logement": o.PhotosParLogement = Entier(); break;
                case "--graine": o.Graine = Entier(); break;
                case "--s3-fichier-env": o.S3FichierEnv = Valeur(); break;
                case "--s3-url": o.S3Url = Valeur(); break;
                case "--s3-bucket": o.S3Bucket = Valeur(); break;
                default: return null;
            }
        }
        return string.IsNullOrWhiteSpace(o.Cible) ? null : o;
    }
}

/// <summary>Données cohérentes avec les règles de l'API (statuts, dates, montants), en mémoire, prêtes pour COPY.</summary>
sealed class Generateur(Options o, string hash)
{
    private readonly Random _r = new(o.Graine);
    private readonly DateTime _maintenant = DateTime.UtcNow;
    private DateTime Aujourdhui => _maintenant.Date;

    public List<Table> Tables { get; } = new();

    // Villes guinéennes (centre approximatif) et quartiers ; Conakry pèse la moitié des annonces.
    private static readonly (string Ville, double Lat, double Lon, int Poids, string[] Quartiers)[] Villes =
    [
        ("Conakry", 9.5370, -13.6773, 50, ["Kaloum", "Dixinn", "Matam", "Ratoma", "Matoto", "Kipé", "Taouyah", "Lambanyi", "Nongo", "Kaporo",
            "Sonfonia", "Hamdallaye", "Bambéto", "Cosa", "Koloma", "Camayenne", "Landréah", "Minière", "Coléah", "Gbessia"]),
        ("Kindia", 10.0569, -12.8658, 6, ["Abattoir", "Banlieue", "Manquepas", "Kahouréah", "Condetta"]),
        ("Boké", 10.9409, -14.2967, 4, ["Kadiguel", "Dibia", "Bambaya", "Boké-Centre"]),
        ("Kamsar", 10.6500, -14.6167, 3, ["Filima", "Kassopo", "Taady", "Cité ANAIM"]),
        ("Labé", 11.3182, -12.2833, 5, ["Daka", "Kouroula", "Tata", "Pounthioun", "Dow Saré"]),
        ("Mamou", 10.3755, -12.0915, 3, ["Almamya", "Poudrière", "Petel", "Horé Fello"]),
        ("Kankan", 10.3854, -9.3057, 6, ["Bordo", "Kabada", "Salamani", "Missira", "Banankoroda"]),
        ("N'Zérékoré", 7.7562, -8.8179, 5, ["Gonia", "Dorota", "Mohomou", "Bellevue", "Commercial"]),
        ("Siguiri", 11.4167, -9.1667, 3, ["Bolibana", "Siguiri-Koura", "Kankan-Koura"]),
        ("Faranah", 10.0404, -10.7434, 2, ["Abattoir", "Tonkolon", "Faranah-Centre"]),
        ("Dubréka", 9.7911, -13.5231, 4, ["Kalogo", "Khorira", "Dubréka-Centre", "Badi"]),
        ("Coyah", 9.7085, -13.3843, 4, ["Kouriah", "Manéah", "Coyah-Centre", "Wonkifong"]),
        ("Kissidougou", 9.1848, -10.0999, 2, ["Dandou", "Kissi-Kaba", "Madina"]),
        ("Guéckédou", 8.5672, -10.1333, 2, ["Ouendé", "Nyaloulou", "Guéckédou-Centre"]),
        ("Fria", 10.3667, -13.5833, 1, ["Cité", "Kéwéwol", "Fria-Centre"])
    ];

    private static readonly string[] Prenoms = ["Mamadou", "Aissatou", "Ibrahima", "Mariama", "Alpha", "Fatoumata", "Oumar", "Kadiatou", "Sekou",
        "Hawa", "Abdoulaye", "Fanta", "Moussa", "Djénabou", "Thierno", "Mabinty", "Lansana", "Saran", "Elhadj", "Nènè", "Boubacar", "Maimouna",
        "Ousmane", "Adama", "Facinet", "Koumba", "Sidiki", "Aminata", "Cellou", "Binta"];
    private static readonly string[] Noms = ["Diallo", "Barry", "Bah", "Sow", "Camara", "Condé", "Touré", "Sylla", "Keita", "Kouyaté", "Soumah",
        "Bangoura", "Cissé", "Kaba", "Doumbouya", "Baldé", "Konaté", "Fofana", "Traoré", "Sacko", "Kourouma", "Haba", "Loua", "Millimono"];
    private static readonly string[] Phrases = ["Bonjour, le logement est-il toujours disponible à ces dates ?", "Merci pour votre réponse rapide.",
        "Nous arriverons vers 15 h, est-ce possible ?", "Le quartier est-il calme le soir ?", "Y a-t-il un groupe électrogène en cas de coupure ?",
        "Parfait, à bientôt !", "Pouvez-vous m'indiquer comment accéder au logement ?", "Le gardien vous accueillera à l'entrée.",
        "La clim fonctionne très bien, merci.", "Nous avons passé un excellent séjour.", "Est-ce qu'il y a un parking sécurisé ?",
        "Je confirme notre arrivée demain matin."];
    private static readonly string[] Commentaires = ["Logement propre et bien situé, hôte très réactif.", "Très bon rapport qualité-prix.",
        "Quartier calme, je recommande.", "Quelques coupures d'eau mais l'hôte a vite réglé le problème.", "Séjour agréable, conforme à l'annonce.",
        "Accueil chaleureux, appartement spacieux.", "Bien, mais un peu bruyant le week-end."];
    private static readonly string[] Modes = ["Espèces", "Orange Money", "MTN MoMo", "Virement"];

    private int Entre(int min, int max) => _r.Next(min, max + 1);
    private bool Proba(double p) => _r.NextDouble() < p;
    private T Un<T>(IReadOnlyList<T> liste) => liste[_r.Next(liste.Count)];
    private DateTime Avant(DateTime d) => d > _maintenant ? _maintenant : d;
    private static decimal Arrondi(decimal montant, decimal pas) => Math.Round(montant / pas, 0, MidpointRounding.AwayFromZero) * pas;

    public void Generer()
    {
        // --- Utilisateurs : 1 admin, puis les bailleurs, puis les locataires ---
        var utilisateurs = new Table("Utilisateurs", ["Id", "Nom", "Telephone", "Email", "MotDePasseHash", "Role", "Statut", "DateCreation",
            "NotificationsSms", "IdentiteVerifiee", "MotifDesactivation", "DateDesactivation", "DateVerificationIdentite"]);
        var nbLocataires = o.Utilisateurs - o.Bailleurs - 1;
        var premierLocataire = o.Bailleurs + 2;
        var statuts = new int[o.Utilisateurs + 1];
        for (var id = 1; id <= o.Utilisateurs; id++)
        {
            var (role, email) = id == 1 ? (0, "admin@test-charge.local")
                : id < premierLocataire ? (1, $"bailleur{id - 1}@test-charge.local")
                : (2, $"locataire{id - premierLocataire + 1}@test-charge.local");
            // Les comptes « 1 » de chaque rôle (identifiants connus) restent toujours valides.
            var connu = id == 1 || id == 2 || id == premierLocataire;
            var statut = connu ? 1 : _r.NextDouble() switch { < 0.02 => 0, < 0.03 => 3, _ => 1 };
            statuts[id] = statut;
            var creation = Aujourdhui.AddDays(-Entre(30, 1100)).AddMinutes(Entre(0, 1439));
            var verifiee = role == 1 && Proba(0.3);
            utilisateurs.Ajouter(id, $"{Un(Prenoms)} {Un(Noms)}", $"+2246{id:D8}", email, hash, role, statut, creation,
                false, verifiee, statut == 3 ? "Compte de test désactivé" : null, statut == 3 ? creation.AddDays(10) : null,
                verifiee ? creation.AddDays(2) : null);
        }
        Tables.Add(utilisateurs);
        int Bailleur() => 2 + (int)(o.Bailleurs * Math.Pow(_r.NextDouble(), 2)); // quelques gros hôtes, beaucoup de petits
        int Locataire() => premierLocataire + _r.Next(nbLocataires);

        // --- Logements : un sur deux en séjour court ---
        var logements = new Table("Appartements", ["Id", "BailleurId", "Adresse", "Description", "Disponible", "EstDesactive", "DateDesactivation",
            "TypeOffre", "Ville", "Quartier", "Capacite", "Chambres", "Lits", "SallesDeBain", "Equipements", "LoyerMensuel", "DureeAvanceExigee",
            "PrixParNuit", "DureeMaxMois", "FraisMenage", "Caution", "ReductionSemainePct", "ReductionMoisPct", "NuitsMin", "ReservationInstantanee",
            "PolitiqueAnnulation", "HeureArrivee", "HeureDepart", "InstructionsArrivee", "Latitude", "Longitude", "DateCreation"]);
        var poidsTotal = Villes.Sum(v => v.Poids);
        var infos = new (int Bailleur, bool Court, decimal Prix, int Avance, decimal Menage, int NuitsMin, DateTime Creation)[o.Logements + 1];
        var lignesLogements = new object?[o.Logements + 1][];
        for (var id = 1; id <= o.Logements; id++)
        {
            var tirage = _r.Next(poidsTotal);
            var ville = Villes.First(v => (tirage -= v.Poids) < 0);
            var quartier = Un(ville.Quartiers);
            var court = id % 2 == 0;
            var bailleur = id <= 40 ? 2 : Bailleur(); // bailleur1 : 40 logements (compte connu)
            var chambres = Entre(1, 5);
            var equipements = AppartementServices.EquipementsAutorises.Where(_ => Proba(0.4)).ToList();
            var creation = Aujourdhui.AddDays(-Entre(1, 1000)).AddMinutes(Entre(0, 1439));
            var desactive = Proba(0.03) && id > 40;
            decimal prix = court ? Arrondi(Entre(150_000, 1_500_000), 10_000) : Arrondi(Entre(1_500_000, 15_000_000), 50_000);
            var avance = court ? 0 : Un(new[] { 3, 6, 12 });
            var menage = court ? Arrondi(Entre(0, 100_000), 5_000) : 0m;
            var nuitsMin = court ? Un(new[] { 1, 1, 1, 2, 3 }) : 0;
            infos[id] = (bailleur, court, prix, avance, menage, nuitsMin, creation);
            lignesLogements[id] =
            [
                id, bailleur, $"Rue {Entre(1, 400)}, {quartier}", $"{(court ? "Appartement meublé" : "Logement")} de {chambres} chambre(s) à {quartier}, {ville.Ville}.",
                !desactive && Proba(0.92), desactive, desactive ? creation.AddDays(30) : null,
                court ? 1 : 0, ville.Ville, quartier, chambres * 2, chambres, chambres + Entre(0, 2), Entre(1, chambres),
                equipements.Count == 0 ? null : string.Join(',', equipements),
                court ? null : prix, court ? null : avance,
                court ? prix : null, court ? Entre(1, 3) : null, court ? menage : null, court ? Arrondi(prix * 2, 10_000) : null,
                court ? Un(new[] { 0, 5, 10 }) : null, court ? Un(new[] { 0, 10, 20 }) : null, court ? nuitsMin : null,
                court && Proba(0.5), Entre(0, 2), court ? "14:00" : null, court ? "11:00" : null,
                court ? "Le gardien remet les clés à l'arrivée." : null,
                Math.Round(ville.Lat + (_r.NextDouble() - 0.5) * 0.08, 6), Math.Round(ville.Lon + (_r.NextDouble() - 0.5) * 0.08, 6),
                creation
            ];
        }

        // --- Photos : clés partagées (quelques objets de référence dans le bucket de charge) ---
        var photos = new Table("PhotosAppartement", ["Id", "AppartementId", "NomFichierOriginal", "CheminStockage", "Ordre", "Est360", "Libelle", "DateUpload"]);
        var idPhoto = 0;
        for (var id = 1; id <= o.Logements; id++)
        {
            for (var n = 0; n < o.PhotosParLogement; n++)
            {
                var reference = 1 + (id + n) % 5;
                photos.Ajouter(++idPhoto, id, $"photo-{n + 1}.jpg", $"charge/reference-{reference}.jpg", n, false, n == 0 ? "Séjour" : null, infos[id].Creation);
            }
        }

        // --- Réservations (séjours courts), dates sans chevauchement par logement ---
        var reservations = new Table("Reservations", ["Id", "LocataireId", "AppartementId", "DateArrivee", "DateDepart", "NombreVoyageurs",
            "NombreNuits", "PrixParNuit", "ReductionMontant", "FraisMenage", "Caution", "MontantTotal", "MotifRefus", "DateReponseHote", "Statut",
            "DateCreation", "PolitiqueAnnulation", "AnnuleePar", "MotifAnnulation", "DateAnnulation", "MontantARembourser"]);
        var paiements = new Table("Paiements", ["Id", "MensualiteId", "ReservationId", "LoyerId", "Montant", "DatePaiement", "ModePaiement", "Reference"]);
        var idPaiement = 0;
        var courts = Enumerable.Range(1, o.Logements).Where(id => infos[id].Court).ToArray();
        // Environ 18 jours par séjour (battement + nuits) : chaque calendrier commence assez tôt pour qu'un tiers des séjours soit à venir.
        var parLogement = courts.Length == 0 ? 0 : (double)o.Reservations / courts.Length;
        var curseur = courts.ToDictionary(id => id, _ => Aujourdhui.AddDays(-(int)(parLogement * 18 * 0.7) - Entre(0, 30)));
        var sejours = new List<(int Id, int Locataire, int Logement, DateTime Arrivee, DateTime Depart, int Statut, DateTime Creation)>();
        var premierLocataireId = premierLocataire;
        for (var id = 1; id <= o.Reservations && courts.Length > 0; id++)
        {
            var logement = courts[(id - 1) % courts.Length];
            var info = infos[logement];
            var arrivee = curseur[logement].AddDays(Entre(0, 25));
            var nuits = Math.Max(info.NuitsMin, Entre(1, 10));
            var depart = arrivee.AddDays(nuits);
            curseur[logement] = depart;
            var locataire = id <= 30 ? premierLocataireId : Locataire(); // locataire1 : 30 réservations
            int statut;
            if (depart <= Aujourdhui)
            {
                statut = _r.NextDouble() switch { < 0.85 => 3, < 0.95 => 2, _ => 5 }; // Terminee, Annulee, Refusee
            }
            else if (arrivee <= Aujourdhui)
            {
                statut = 1; // en cours : Confirmee
            }
            else
            {
                // Demandes et réservations en attente : récentes, et les « en attente » ont un acompte (pas d'expiration automatique).
                statut = _r.NextDouble() switch { < 0.80 => 1, < 0.88 => 2, < 0.95 => 4, _ => 0 };
            }
            var creation = statut is 0 or 4
                ? _maintenant.AddMinutes(-Entre(10, 600))
                : Avant(arrivee.AddDays(-Entre(1, 30)).AddMinutes(Entre(0, 1439)));
            var sousTotal = info.Prix * nuits;
            var total = sousTotal + info.Menage;
            var reponse = statut is 1 or 3 or 5 ? Avant(creation.AddHours(Entre(1, 30))) : (DateTime?)null;
            reservations.Ajouter(id, locataire, logement, arrivee, depart, Entre(1, 4), nuits, info.Prix, 0m, info.Menage, Arrondi(info.Prix * 2, 10_000),
                total, statut == 5 ? "Dates non disponibles" : null, reponse, statut, creation, Entre(0, 2),
                statut == 2 ? 0 : null, statut == 2 ? "Changement de programme" : null, statut == 2 ? Avant(creation.AddDays(1)) : null,
                statut == 2 ? 0m : null);
            if (statut is 1 or 3)
            {
                paiements.Ajouter(++idPaiement, null, id, null, total, Avant(creation.AddHours(Entre(1, 20))), Un(Modes), $"CH{id:D6}");
            }
            else if (statut == 0)
            {
                paiements.Ajouter(++idPaiement, null, id, null, Arrondi(total * 0.3m, 1_000), Avant(creation.AddMinutes(5)), Un(Modes), $"AC{id:D6}");
            }
            sejours.Add((id, locataire, logement, arrivee, depart, statut, creation));
        }

        // --- Messages : conversations des réservations non annulées, quelques non lus récents ---
        var messages = new Table("Messages", ["Id", "ReservationId", "AuteurId", "Contenu", "DateEnvoi", "DateLecture"]);
        var conversations = sejours.Where(s => s.Statut != 2).ToList();
        for (var id = 1; id <= o.Messages && conversations.Count > 0; id++)
        {
            var s = conversations[(id - 1) % conversations.Count];
            var hote = infos[s.Logement].Bailleur;
            var auteur = id % 2 == 0 ? s.Locataire : hote;
            var fin = Avant(s.Depart.AddDays(2));
            var envoi = s.Creation.AddSeconds(_r.NextDouble() * Math.Max(60, (fin - s.Creation).TotalSeconds));
            envoi = Avant(envoi);
            var lu = envoi < _maintenant.AddDays(-2) || Proba(0.7);
            messages.Ajouter(id, s.Id, auteur, Un(Phrases), envoi, lu ? Avant(envoi.AddMinutes(Entre(1, 600))) : null);
        }

        // --- Avis sur les séjours terminés ---
        var avis = new Table("Avis", ["Id", "ReservationId", "LocationId", "AuteurId", "CibleId", "AppartementId", "Sens", "Note", "Proprete",
            "Communication", "Emplacement", "RapportQualitePrix", "Commentaire", "DateCreation", "ReponseHote", "DateReponse", "Masque"]);
        var idAvis = 0;
        foreach (var s in sejours.Where(s => s.Statut == 3))
        {
            var hote = infos[s.Logement].Bailleur;
            var date = Avant(s.Depart.AddDays(Entre(1, 10)));
            if (Proba(0.5))
            {
                var repondu = Proba(0.3);
                avis.Ajouter(++idAvis, s.Id, null, s.Locataire, hote, s.Logement, 0, Entre(3, 5), Entre(3, 5), Entre(3, 5), Entre(3, 5), Entre(3, 5),
                    Un(Commentaires), date, repondu ? "Merci pour votre séjour !" : null, repondu ? Avant(date.AddDays(1)) : null, Proba(0.01));
            }
            if (Proba(0.3))
            {
                avis.Ajouter(++idAvis, s.Id, null, hote, s.Locataire, s.Logement, 1, Entre(3, 5), null, null, null, null,
                    "Voyageur respectueux, je recommande.", date, null, null, false);
            }
        }

        // --- Candidatures, locations, financements, mensualités, loyers (longue durée) ---
        var candidatures = new Table("Candidatures", ["Id", "LocataireId", "AppartementId", "ApportInitial", "SituationProfessionnelle", "Statut",
            "DateSoumission", "DateTraitement", "MotifRefus"]);
        var locations = new Table("Locations", ["Id", "LocataireId", "AppartementId", "CandidatureId", "DateDebut", "DateFin", "Statut",
            "DateApportPaye", "ModePaiementApport", "ReferenceApport", "DateRemiseCles"]);
        var financements = new Table("FinancementLocations", ["Id", "LocationId", "MontantTotal", "DureeMois", "ApportInitial", "MontantARembourser",
            "TauxFrais", "NombreMensualites", "MontantMensualite", "Statut"]);
        var mensualites = new Table("Mensualites", ["Id", "FinancementLocationId", "NumeroEcheance", "DateEcheance", "MontantDu", "MontantPaye", "Statut"]);
        var loyers = new Table("Loyers", ["Id", "LocationId", "Numero", "DateEcheance", "MontantDu", "MontantPaye", "Statut"]);
        var longs = Enumerable.Range(1, o.Logements).Where(id => !infos[id].Court).ToArray();
        var attribues = new HashSet<int>();
        int idLocation = 0, idFinancement = 0, idMensualite = 0, idLoyer = 0;
        for (var id = 1; id <= o.Candidatures && longs.Length > 0; id++)
        {
            var logement = longs[_r.Next(longs.Length)];
            var info = infos[logement];
            var locataire = id <= 10 ? premierLocataireId : Locataire();
            var soumission = Aujourdhui.AddDays(-Entre(1, 700)).AddMinutes(Entre(0, 1439));
            // Une seule attribution par logement ; les dossiers des 2 derniers mois restent souvent à l'étude.
            var statut = _r.NextDouble() switch { < 0.40 => 0, < 0.45 => 5, < 0.65 => 2, < 0.75 => 3, < 0.80 => 4, _ => 1 };
            if (statut == 1 && (!attribues.Add(logement) || soumission > Aujourdhui.AddDays(-60)))
            {
                statut = 3;
            }
            var traitement = statut is 0 or 5 ? (DateTime?)null : soumission.AddDays(Entre(2, 20));
            var montantTotal = info.Prix * info.Avance;
            var apport = Arrondi(montantTotal * 0.3m, 1_000);
            candidatures.Ajouter(id, locataire, logement, apport, Entre(0, 4), statut, soumission, traitement,
                statut == 2 ? "Dossier incomplet" : null);
            if (statut != 1)
            {
                continue;
            }

            var debut = traitement!.Value.Date.AddDays(7);
            var terminee = Proba(0.25) && debut.AddMonths(info.Avance + 3) < Aujourdhui;
            var finLocation = terminee ? debut.AddMonths(info.Avance + 3) : (DateTime?)null;
            locations.Ajouter(++idLocation, locataire, logement, id, debut, finLocation, terminee ? 1 : 0,
                debut.AddDays(-2), Un(Modes), $"AP{id:D6}", debut);
            if (!terminee)
            {
                lignesLogements[logement]![4] = false; // logement loué : plus disponible
            }

            var aRembourser = montantTotal - apport;
            var (mensualite, derniere) = FinancementServices.Echeancier(aRembourser, 0.03m, info.Avance);
            var financementId = ++idFinancement;
            var toutPaye = true;
            for (var n = 1; n <= info.Avance; n++)
            {
                var echeance = debut.AddMonths(n);
                var du = n == info.Avance ? derniere : mensualite;
                var paye = echeance <= Aujourdhui && Proba(0.92);
                toutPaye &= paye;
                mensualites.Ajouter(++idMensualite, financementId, n, echeance, du, paye ? du : 0m, paye ? 2 : 0);
                if (paye)
                {
                    paiements.Ajouter(++idPaiement, idMensualite, null, null, du, Avant(echeance.AddDays(Entre(-3, 5))), Un(Modes), null);
                }
            }
            financements.Ajouter(financementId, idLocation, montantTotal, info.Avance, apport, aRembourser, 0.03m, info.Avance, mensualite, toutPaye ? 2 : 1);

            // Loyers mensuels après la période couverte par l'avance.
            var finLoyers = finLocation ?? Aujourdhui;
            var numero = 0;
            for (var echeance = debut.AddMonths(info.Avance); echeance < finLoyers; echeance = echeance.AddMonths(1))
            {
                var paye = Proba(0.85);
                loyers.Ajouter(++idLoyer, idLocation, ++numero, echeance, info.Prix, paye ? info.Prix : 0m, paye ? 2 : 0);
                if (paye)
                {
                    paiements.Ajouter(++idPaiement, null, null, idLoyer, info.Prix, Avant(echeance.AddDays(Entre(-2, 6))), Un(Modes), null);
                }
            }
        }
        foreach (var ligne in lignesLogements.Skip(1))
        {
            logements.Ajouter(ligne!);
        }

        // --- Favoris (locataires) et dates bloquées ---
        var favoris = new Table("Favoris", ["UtilisateurId", "AppartementId", "DateCreation"]);
        var paires = new HashSet<(int, int)>();
        for (var n = 0; n < 25 && n < o.Logements; n++)
        {
            paires.Add((premierLocataireId, n + 1)); // locataire1 : 25 favoris
        }
        var essais = 0;
        while (paires.Count < o.Favoris && essais++ < o.Favoris * 3)
        {
            paires.Add((Locataire(), 1 + _r.Next(o.Logements)));
        }
        foreach (var (utilisateur, logement) in paires)
        {
            favoris.Ajouter(utilisateur, logement, Aujourdhui.AddDays(-Entre(0, 365)).AddMinutes(Entre(0, 1439)));
        }
        var indisponibilites = new Table("Indisponibilites", ["Id", "AppartementId", "DateDebut", "DateFin", "Motif", "DateCreation"]);
        for (var id = 1; id <= courts.Length / 10; id++)
        {
            var logement = courts[_r.Next(courts.Length)];
            var debutBloque = Aujourdhui.AddDays(200 + id % 60);
            indisponibilites.Ajouter(id, logement, debutBloque, debutBloque.AddDays(Entre(2, 7)), "Travaux", _maintenant.AddDays(-Entre(0, 30)));
        }

        // Ordre d'insertion compatible avec les clés étrangères.
        Tables.AddRange([logements, photos, reservations, candidatures, locations, financements, mensualites, loyers, paiements, messages,
            avis, favoris, indisponibilites]);
    }
}
