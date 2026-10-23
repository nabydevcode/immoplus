using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialePostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "JournalActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UtilisateurId = table.Column<int>(type: "integer", nullable: true),
                    UtilisateurNom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Cible = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CibleId = table.Column<int>(type: "integer", nullable: true),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AdresseIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Utilisateurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Telephone = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    MotDePasseHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    JetonReinitialisationHash = table.Column<string>(type: "text", nullable: true),
                    JetonReinitialisationExpiration = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateDernierRecapRetards = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MotifDesactivation = table.Column<string>(type: "text", nullable: true),
                    DateDesactivation = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NotificationsSms = table.Column<bool>(type: "boolean", nullable: false),
                    IdentiteVerifiee = table.Column<bool>(type: "boolean", nullable: false),
                    DateVerificationIdentite = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utilisateurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Appartements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BailleurId = table.Column<int>(type: "integer", nullable: false),
                    Adresse = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Disponible = table.Column<bool>(type: "boolean", nullable: false),
                    EstDesactive = table.Column<bool>(type: "boolean", nullable: false),
                    DateDesactivation = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TypeOffre = table.Column<int>(type: "integer", nullable: false),
                    Ville = table.Column<string>(type: "text", nullable: true),
                    Quartier = table.Column<string>(type: "text", nullable: true),
                    Capacite = table.Column<int>(type: "integer", nullable: true),
                    Chambres = table.Column<int>(type: "integer", nullable: true),
                    Lits = table.Column<int>(type: "integer", nullable: true),
                    SallesDeBain = table.Column<int>(type: "integer", nullable: true),
                    Equipements = table.Column<string>(type: "text", nullable: true),
                    LoyerMensuel = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DureeAvanceExigee = table.Column<int>(type: "integer", nullable: true),
                    PrixParNuit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DureeMaxMois = table.Column<int>(type: "integer", nullable: true),
                    FraisMenage = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Caution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ReductionSemainePct = table.Column<int>(type: "integer", nullable: true),
                    ReductionMoisPct = table.Column<int>(type: "integer", nullable: true),
                    NuitsMin = table.Column<int>(type: "integer", nullable: true),
                    ReservationInstantanee = table.Column<bool>(type: "boolean", nullable: false),
                    PolitiqueAnnulation = table.Column<int>(type: "integer", nullable: false),
                    HeureArrivee = table.Column<string>(type: "text", nullable: true),
                    HeureDepart = table.Column<string>(type: "text", nullable: true),
                    InstructionsArrivee = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appartements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appartements_Utilisateurs_BailleurId",
                        column: x => x.BailleurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationsSms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UtilisateurId = table.Column<int>(type: "integer", nullable: false),
                    Telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Canal = table.Column<int>(type: "integer", nullable: false),
                    Texte = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    ReferenceFournisseur = table.Column<string>(type: "text", nullable: true),
                    Erreur = table.Column<string>(type: "text", nullable: true),
                    DateEnvoi = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Evenement = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationsSms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationsSms_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PiecesIdentite",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UtilisateurId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Chemin = table.Column<string>(type: "text", nullable: false),
                    CheminVerso = table.Column<string>(type: "text", nullable: true),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    MotifRefus = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DateEnvoi = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateDecision = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DecideParId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PiecesIdentite", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PiecesIdentite_Utilisateurs_DecideParId",
                        column: x => x.DecideParId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PiecesIdentite_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Signalements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuteurId = table.Column<int>(type: "integer", nullable: true),
                    AuteurEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Cible = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CibleId = table.Column<int>(type: "integer", nullable: false),
                    Motif = table.Column<int>(type: "integer", nullable: false),
                    Texte = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DecisionAdmin = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActionAppliquee = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateTraitement = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TraiteParId = table.Column<int>(type: "integer", nullable: true),
                    AdresseIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signalements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Signalements_Utilisateurs_AuteurId",
                        column: x => x.AuteurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Signalements_Utilisateurs_TraiteParId",
                        column: x => x.TraiteParId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Candidatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocataireId = table.Column<int>(type: "integer", nullable: false),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    ApportInitial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SituationProfessionnelle = table.Column<int>(type: "integer", nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DateSoumission = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateTraitement = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MotifRefus = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidatures_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Candidatures_Utilisateurs_LocataireId",
                        column: x => x.LocataireId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Favoris",
                columns: table => new
                {
                    UtilisateurId = table.Column<int>(type: "integer", nullable: false),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favoris", x => new { x.UtilisateurId, x.AppartementId });
                    table.ForeignKey(
                        name: "FK_Favoris_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Favoris_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Indisponibilites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateFin = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Motif = table.Column<string>(type: "text", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indisponibilites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Indisponibilites_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotosAppartement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    NomFichierOriginal = table.Column<string>(type: "text", nullable: false),
                    CheminStockage = table.Column<string>(type: "text", nullable: false),
                    Ordre = table.Column<int>(type: "integer", nullable: false),
                    Est360 = table.Column<bool>(type: "boolean", nullable: false),
                    Libelle = table.Column<string>(type: "text", nullable: true),
                    DateUpload = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotosAppartement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotosAppartement_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocataireId = table.Column<int>(type: "integer", nullable: false),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    DateArrivee = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateDepart = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NombreVoyageurs = table.Column<int>(type: "integer", nullable: false),
                    NombreNuits = table.Column<int>(type: "integer", nullable: false),
                    PrixParNuit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReductionMontant = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FraisMenage = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Caution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontantTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MotifRefus = table.Column<string>(type: "text", nullable: true),
                    DateReponseHote = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PolitiqueAnnulation = table.Column<int>(type: "integer", nullable: false),
                    AnnuleePar = table.Column<int>(type: "integer", nullable: true),
                    MotifAnnulation = table.Column<string>(type: "text", nullable: true),
                    DateAnnulation = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MontantARembourser = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MontantRembourse = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DateRemboursement = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ModeRemboursement = table.Column<string>(type: "text", nullable: true),
                    ReferenceRemboursement = table.Column<string>(type: "text", nullable: true),
                    DateDernierEmailMessageVoyageur = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateDernierEmailMessageHote = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateEnvoiInstructions = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Utilisateurs_LocataireId",
                        column: x => x.LocataireId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DemandesComplement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CandidatureId = table.Column<int>(type: "integer", nullable: false),
                    Pieces = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    DateLimite = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateDemande = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletNotifie = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandesComplement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandesComplement_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CandidatureId = table.Column<int>(type: "integer", nullable: false),
                    Proprietaire = table.Column<int>(type: "integer", nullable: false),
                    TypeDocument = table.Column<int>(type: "integer", nullable: false),
                    NomFichierOriginal = table.Column<string>(type: "text", nullable: false),
                    CheminStockage = table.Column<string>(type: "text", nullable: false),
                    TailleOctets = table.Column<long>(type: "bigint", nullable: false),
                    DateUpload = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Garants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CandidatureId = table.Column<int>(type: "integer", nullable: false),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Telephone = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    SituationProfessionnelle = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Garants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Garants_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocataireId = table.Column<int>(type: "integer", nullable: false),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    CandidatureId = table.Column<int>(type: "integer", nullable: true),
                    DateDebut = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateFin = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DateApportPaye = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ModePaiementApport = table.Column<string>(type: "text", nullable: true),
                    ReferenceApport = table.Column<string>(type: "text", nullable: true),
                    DateRemiseCles = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DatePreavis = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateFinPrevue = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PreavisDonnePar = table.Column<string>(type: "text", nullable: true),
                    MotifPreavis = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Utilisateurs_LocataireId",
                        column: x => x.LocataireId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Visites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CandidatureId = table.Column<int>(type: "integer", nullable: false),
                    DateHeure = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    ReponseLocataire = table.Column<string>(type: "text", nullable: true),
                    NotesBailleur = table.Column<string>(type: "text", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Visites_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationId = table.Column<int>(type: "integer", nullable: false),
                    AuteurId = table.Column<int>(type: "integer", nullable: false),
                    Contenu = table.Column<string>(type: "text", nullable: false),
                    DateEnvoi = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateLecture = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Utilisateurs_AuteurId",
                        column: x => x.AuteurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotosEtatDesLieux",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationId = table.Column<int>(type: "integer", nullable: false),
                    Moment = table.Column<int>(type: "integer", nullable: false),
                    AuteurId = table.Column<int>(type: "integer", nullable: false),
                    Chemin = table.Column<string>(type: "text", nullable: false),
                    Commentaire = table.Column<string>(type: "text", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotosEtatDesLieux", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotosEtatDesLieux_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotosEtatDesLieux_Utilisateurs_AuteurId",
                        column: x => x.AuteurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Avis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationId = table.Column<int>(type: "integer", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    AuteurId = table.Column<int>(type: "integer", nullable: false),
                    CibleId = table.Column<int>(type: "integer", nullable: false),
                    AppartementId = table.Column<int>(type: "integer", nullable: false),
                    Sens = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<int>(type: "integer", nullable: false),
                    Proprete = table.Column<int>(type: "integer", nullable: true),
                    Communication = table.Column<int>(type: "integer", nullable: true),
                    Emplacement = table.Column<int>(type: "integer", nullable: true),
                    RapportQualitePrix = table.Column<int>(type: "integer", nullable: true),
                    Commentaire = table.Column<string>(type: "text", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReponseHote = table.Column<string>(type: "text", nullable: true),
                    DateReponse = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Masque = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Avis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Avis_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Avis_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Avis_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Avis_Utilisateurs_AuteurId",
                        column: x => x.AuteurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Avis_Utilisateurs_CibleId",
                        column: x => x.CibleId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancementLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    MontantTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DureeMois = table.Column<int>(type: "integer", nullable: false),
                    ApportInitial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontantARembourser = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TauxFrais = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    NombreMensualites = table.Column<int>(type: "integer", nullable: false),
                    MontantMensualite = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancementLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancementLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Loyers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    DateEcheance = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MontantDu = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontantPaye = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DateRappelEcheance = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateDernierRappelRetard = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loyers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loyers_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Mensualites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FinancementLocationId = table.Column<int>(type: "integer", nullable: false),
                    NumeroEcheance = table.Column<int>(type: "integer", nullable: false),
                    DateEcheance = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MontantDu = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontantPaye = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DateRappelEcheance = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateDernierRappelRetard = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mensualites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mensualites_FinancementLocations_FinancementLocationId",
                        column: x => x.FinancementLocationId,
                        principalTable: "FinancementLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Paiements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MensualiteId = table.Column<int>(type: "integer", nullable: true),
                    ReservationId = table.Column<int>(type: "integer", nullable: true),
                    LoyerId = table.Column<int>(type: "integer", nullable: true),
                    Montant = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DatePaiement = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModePaiement = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paiements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Paiements_Loyers_LoyerId",
                        column: x => x.LoyerId,
                        principalTable: "Loyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Paiements_Mensualites_MensualiteId",
                        column: x => x.MensualiteId,
                        principalTable: "Mensualites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Paiements_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionsMobiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationId = table.Column<int>(type: "integer", nullable: false),
                    UtilisateurId = table.Column<int>(type: "integer", nullable: false),
                    Operateur = table.Column<int>(type: "integer", nullable: false),
                    Telephone = table.Column<string>(type: "text", nullable: false),
                    Montant = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    ReferenceFournisseur = table.Column<string>(type: "text", nullable: true),
                    MessageErreur = table.Column<string>(type: "text", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateMaj = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PaiementId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionsMobiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionsMobiles_Paiements_PaiementId",
                        column: x => x.PaiementId,
                        principalTable: "Paiements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionsMobiles_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionsMobiles_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appartements_BailleurId",
                table: "Appartements",
                column: "BailleurId");

            migrationBuilder.CreateIndex(
                name: "IX_Avis_AppartementId",
                table: "Avis",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Avis_AuteurId",
                table: "Avis",
                column: "AuteurId");

            migrationBuilder.CreateIndex(
                name: "IX_Avis_CibleId",
                table: "Avis",
                column: "CibleId");

            migrationBuilder.CreateIndex(
                name: "IX_Avis_LocationId_Sens",
                table: "Avis",
                columns: new[] { "LocationId", "Sens" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Avis_ReservationId_Sens",
                table: "Avis",
                columns: new[] { "ReservationId", "Sens" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_AppartementId",
                table: "Candidatures",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_LocataireId",
                table: "Candidatures",
                column: "LocataireId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandesComplement_CandidatureId",
                table: "DemandesComplement",
                column: "CandidatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CandidatureId",
                table: "Documents",
                column: "CandidatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Favoris_AppartementId",
                table: "Favoris",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancementLocations_LocationId",
                table: "FinancementLocations",
                column: "LocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Garants_CandidatureId",
                table: "Garants",
                column: "CandidatureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Indisponibilites_AppartementId",
                table: "Indisponibilites",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalActions_Cible_CibleId",
                table: "JournalActions",
                columns: new[] { "Cible", "CibleId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalActions_Date",
                table: "JournalActions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_JournalActions_UtilisateurId_Date",
                table: "JournalActions",
                columns: new[] { "UtilisateurId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_AppartementId",
                table: "Locations",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CandidatureId",
                table: "Locations",
                column: "CandidatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_LocataireId",
                table: "Locations",
                column: "LocataireId");

            migrationBuilder.CreateIndex(
                name: "IX_Loyers_LocationId_Numero",
                table: "Loyers",
                columns: new[] { "LocationId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mensualites_FinancementLocationId_NumeroEcheance",
                table: "Mensualites",
                columns: new[] { "FinancementLocationId", "NumeroEcheance" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_AuteurId",
                table: "Messages",
                column: "AuteurId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReservationId",
                table: "Messages",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationsSms_DateEnvoi",
                table: "NotificationsSms",
                column: "DateEnvoi");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationsSms_UtilisateurId_DateEnvoi",
                table: "NotificationsSms",
                columns: new[] { "UtilisateurId", "DateEnvoi" });

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_LoyerId",
                table: "Paiements",
                column: "LoyerId");

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_MensualiteId",
                table: "Paiements",
                column: "MensualiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_ReservationId",
                table: "Paiements",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotosAppartement_AppartementId",
                table: "PhotosAppartement",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotosEtatDesLieux_AuteurId",
                table: "PhotosEtatDesLieux",
                column: "AuteurId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotosEtatDesLieux_ReservationId",
                table: "PhotosEtatDesLieux",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesIdentite_DecideParId",
                table: "PiecesIdentite",
                column: "DecideParId");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesIdentite_Statut_DateEnvoi",
                table: "PiecesIdentite",
                columns: new[] { "Statut", "DateEnvoi" });

            migrationBuilder.CreateIndex(
                name: "IX_PiecesIdentite_UtilisateurId",
                table: "PiecesIdentite",
                column: "UtilisateurId",
                unique: true,
                filter: "\"Statut\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AppartementId",
                table: "Reservations",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LocataireId",
                table: "Reservations",
                column: "LocataireId");

            migrationBuilder.CreateIndex(
                name: "IX_Signalements_AuteurId",
                table: "Signalements",
                column: "AuteurId");

            migrationBuilder.CreateIndex(
                name: "IX_Signalements_Cible_CibleId",
                table: "Signalements",
                columns: new[] { "Cible", "CibleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Signalements_Statut_DateCreation",
                table: "Signalements",
                columns: new[] { "Statut", "DateCreation" });

            migrationBuilder.CreateIndex(
                name: "IX_Signalements_TraiteParId",
                table: "Signalements",
                column: "TraiteParId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsMobiles_PaiementId",
                table: "TransactionsMobiles",
                column: "PaiementId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsMobiles_ReferenceFournisseur",
                table: "TransactionsMobiles",
                column: "ReferenceFournisseur",
                unique: true,
                filter: "\"ReferenceFournisseur\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsMobiles_ReservationId",
                table: "TransactionsMobiles",
                column: "ReservationId",
                unique: true,
                filter: "\"Statut\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionsMobiles_UtilisateurId",
                table: "TransactionsMobiles",
                column: "UtilisateurId");

            migrationBuilder.CreateIndex(
                name: "IX_Visites_CandidatureId",
                table: "Visites",
                column: "CandidatureId");

            // Email unique sans tenir compte de la casse (« Awa@x.gn » = « awa@x.gn ») : index sur expression, que le modèle EF
            // ne sait pas décrire. Sert aussi les recherches « lower("Email") = … » de la connexion.
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Utilisateurs_Email_Minuscules\" ON \"Utilisateurs\" (lower(\"Email\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Utilisateurs_Email_Minuscules\";");

            migrationBuilder.DropTable(
                name: "Avis");

            migrationBuilder.DropTable(
                name: "DemandesComplement");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Favoris");

            migrationBuilder.DropTable(
                name: "Garants");

            migrationBuilder.DropTable(
                name: "Indisponibilites");

            migrationBuilder.DropTable(
                name: "JournalActions");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "NotificationsSms");

            migrationBuilder.DropTable(
                name: "PhotosAppartement");

            migrationBuilder.DropTable(
                name: "PhotosEtatDesLieux");

            migrationBuilder.DropTable(
                name: "PiecesIdentite");

            migrationBuilder.DropTable(
                name: "Signalements");

            migrationBuilder.DropTable(
                name: "TransactionsMobiles");

            migrationBuilder.DropTable(
                name: "Visites");

            migrationBuilder.DropTable(
                name: "Paiements");

            migrationBuilder.DropTable(
                name: "Loyers");

            migrationBuilder.DropTable(
                name: "Mensualites");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "FinancementLocations");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Candidatures");

            migrationBuilder.DropTable(
                name: "Appartements");

            migrationBuilder.DropTable(
                name: "Utilisateurs");
        }
    }
}
