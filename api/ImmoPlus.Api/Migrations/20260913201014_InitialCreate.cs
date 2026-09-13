using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Utilisateurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    MotDePasseHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utilisateurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Appartements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BailleurId = table.Column<int>(type: "INTEGER", nullable: false),
                    Adresse = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Disponible = table.Column<bool>(type: "INTEGER", nullable: false),
                    TypeOffre = table.Column<int>(type: "INTEGER", nullable: false),
                    LoyerMensuel = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    DureeAvanceExigee = table.Column<int>(type: "INTEGER", nullable: true),
                    PrixParNuit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    DureeMaxMois = table.Column<int>(type: "INTEGER", nullable: true)
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
                name: "Candidatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocataireId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    SituationProfessionnelle = table.Column<int>(type: "INTEGER", nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false),
                    DateSoumission = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateTraitement = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MotifRefus = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocataireId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateArrivee = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDepart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MontantTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Proprietaire = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeDocument = table.Column<int>(type: "INTEGER", nullable: false),
                    NomFichierOriginal = table.Column<string>(type: "TEXT", nullable: false),
                    CheminStockage = table.Column<string>(type: "TEXT", nullable: false),
                    TailleOctets = table.Column<long>(type: "INTEGER", nullable: false),
                    DateUpload = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nom = table.Column<string>(type: "TEXT", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    SituationProfessionnelle = table.Column<int>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocataireId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidatureId = table.Column<int>(type: "INTEGER", nullable: true),
                    DateDebut = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateFin = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "FinancementLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    MontantTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DureeMois = table.Column<int>(type: "INTEGER", nullable: false),
                    ApportInitial = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MontantARembourser = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TauxFrais = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    NombreMensualites = table.Column<int>(type: "INTEGER", nullable: false),
                    MontantMensualite = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "Mensualites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FinancementLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    NumeroEcheance = table.Column<int>(type: "INTEGER", nullable: false),
                    DateEcheance = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MontantDu = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MontantPaye = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MensualiteId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReservationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Montant = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DatePaiement = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModePaiement = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paiements", x => x.Id);
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

            migrationBuilder.CreateIndex(
                name: "IX_Appartements_BailleurId",
                table: "Appartements",
                column: "BailleurId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_AppartementId",
                table: "Candidatures",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_LocataireId",
                table: "Candidatures",
                column: "LocataireId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CandidatureId",
                table: "Documents",
                column: "CandidatureId");

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
                name: "IX_Mensualites_FinancementLocationId",
                table: "Mensualites",
                column: "FinancementLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_MensualiteId",
                table: "Paiements",
                column: "MensualiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_ReservationId",
                table: "Paiements",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AppartementId",
                table: "Reservations",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LocataireId",
                table: "Reservations",
                column: "LocataireId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Garants");

            migrationBuilder.DropTable(
                name: "Paiements");

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
