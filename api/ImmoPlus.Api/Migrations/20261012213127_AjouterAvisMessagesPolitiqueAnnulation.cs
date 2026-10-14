using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterAvisMessagesPolitiqueAnnulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreation",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnnuleePar",
                table: "Reservations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAnnulation",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDernierEmailMessageHote",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDernierEmailMessageVoyageur",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateRemboursement",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeRemboursement",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontantARembourser",
                table: "Reservations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontantRembourse",
                table: "Reservations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotifAnnulation",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolitiqueAnnulation",
                table: "Reservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // Annonces et réservations existantes : politique modérée

            migrationBuilder.AddColumn<string>(
                name: "ReferenceRemboursement",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreation",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolitiqueAnnulation",
                table: "Appartements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // Annonces et réservations existantes : politique modérée

            migrationBuilder.CreateTable(
                name: "Avis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: false),
                    CibleId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sens = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<int>(type: "INTEGER", nullable: false),
                    Proprete = table.Column<int>(type: "INTEGER", nullable: true),
                    Communication = table.Column<int>(type: "INTEGER", nullable: true),
                    Emplacement = table.Column<int>(type: "INTEGER", nullable: true),
                    RapportQualitePrix = table.Column<int>(type: "INTEGER", nullable: true),
                    Commentaire = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReponseHote = table.Column<string>(type: "TEXT", nullable: true),
                    DateReponse = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: false),
                    Contenu = table.Column<string>(type: "TEXT", nullable: false),
                    DateEnvoi = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateLecture = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                name: "IX_Avis_ReservationId_Sens",
                table: "Avis",
                columns: new[] { "ReservationId", "Sens" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_AuteurId",
                table: "Messages",
                column: "AuteurId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReservationId",
                table: "Messages",
                column: "ReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Avis");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropColumn(
                name: "DateCreation",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "AnnuleePar",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DateAnnulation",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DateDernierEmailMessageHote",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DateDernierEmailMessageVoyageur",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DateRemboursement",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ModeRemboursement",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "MontantARembourser",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "MontantRembourse",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "MotifAnnulation",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PolitiqueAnnulation",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReferenceRemboursement",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DateCreation",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "PolitiqueAnnulation",
                table: "Appartements");
        }
    }
}
