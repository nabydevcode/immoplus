using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterPaiementMobileArriveeCarteFavoris : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateEnvoiInstructions",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeureArrivee",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeureDepart",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructionsArrivee",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Appartements",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Appartements",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Favoris",
                columns: table => new
                {
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "PhotosEtatDesLieux",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Moment = table.Column<int>(type: "INTEGER", nullable: false),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: false),
                    Chemin = table.Column<string>(type: "TEXT", nullable: false),
                    Commentaire = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "TransactionsMobiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: false),
                    Operateur = table.Column<int>(type: "INTEGER", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", nullable: false),
                    Montant = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceFournisseur = table.Column<string>(type: "TEXT", nullable: true),
                    MessageErreur = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateMaj = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaiementId = table.Column<int>(type: "INTEGER", nullable: true)
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
                name: "IX_Favoris_AppartementId",
                table: "Favoris",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Favoris");

            migrationBuilder.DropTable(
                name: "PhotosEtatDesLieux");

            migrationBuilder.DropTable(
                name: "TransactionsMobiles");

            migrationBuilder.DropColumn(
                name: "DateEnvoiInstructions",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "HeureArrivee",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "HeureDepart",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "InstructionsArrivee",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Appartements");
        }
    }
}
