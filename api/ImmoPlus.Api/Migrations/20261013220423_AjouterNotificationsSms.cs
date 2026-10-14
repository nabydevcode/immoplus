using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterNotificationsSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotificationsSms",
                table: "Utilisateurs",
                type: "INTEGER",
                nullable: false,
                // Comptes existants : notifications SMS activées par défaut, comme pour les nouveaux comptes.
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "NotificationsSms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Canal = table.Column<int>(type: "INTEGER", nullable: false),
                    Texte = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceFournisseur = table.Column<string>(type: "TEXT", nullable: true),
                    Erreur = table.Column<string>(type: "TEXT", nullable: true),
                    DateEnvoi = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Evenement = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_NotificationsSms_DateEnvoi",
                table: "NotificationsSms",
                column: "DateEnvoi");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationsSms_UtilisateurId_DateEnvoi",
                table: "NotificationsSms",
                columns: new[] { "UtilisateurId", "DateEnvoi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationsSms");

            migrationBuilder.DropColumn(
                name: "NotificationsSms",
                table: "Utilisateurs");
        }
    }
}
