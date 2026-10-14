using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterJournalSignalementsIdentiteAvisLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateVerificationIdentite",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IdentiteVerifiee",
                table: "Utilisateurs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "ReservationId",
                table: "Avis",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "Avis",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Masque",
                table: "Avis",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "JournalActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: true),
                    UtilisateurNom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Cible = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CibleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AdresseIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PiecesIdentite",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Chemin = table.Column<string>(type: "TEXT", nullable: false),
                    CheminVerso = table.Column<string>(type: "TEXT", nullable: true),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false),
                    MotifRefus = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DateEnvoi = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDecision = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecideParId = table.Column<int>(type: "INTEGER", nullable: true)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: true),
                    AuteurEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Cible = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CibleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Motif = table.Column<int>(type: "INTEGER", nullable: false),
                    Texte = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false),
                    DecisionAdmin = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ActionAppliquee = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateTraitement = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TraiteParId = table.Column<int>(type: "INTEGER", nullable: true),
                    AdresseIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_Avis_LocationId_Sens",
                table: "Avis",
                columns: new[] { "LocationId", "Sens" },
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_Avis_Locations_LocationId",
                table: "Avis",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Avis_Locations_LocationId",
                table: "Avis");

            migrationBuilder.DropTable(
                name: "JournalActions");

            migrationBuilder.DropTable(
                name: "PiecesIdentite");

            migrationBuilder.DropTable(
                name: "Signalements");

            migrationBuilder.DropIndex(
                name: "IX_Avis_LocationId_Sens",
                table: "Avis");

            migrationBuilder.DropColumn(
                name: "DateVerificationIdentite",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "IdentiteVerifiee",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Avis");

            migrationBuilder.DropColumn(
                name: "Masque",
                table: "Avis");

            migrationBuilder.AlterColumn<int>(
                name: "ReservationId",
                table: "Avis",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
