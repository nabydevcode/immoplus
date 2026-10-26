using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmationEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateConfirmationEmail",
                table: "Utilisateurs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirme",
                table: "Utilisateurs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Comptes existants (créés avant la confirmation d'email) : considérés confirmés, personne n'est bloqué.
            // DateConfirmationEmail reste vide : la date réelle n'est pas connue.
            migrationBuilder.Sql("UPDATE \"Utilisateurs\" SET \"EmailConfirme\" = TRUE;");

            migrationBuilder.AddColumn<DateTime>(
                name: "JetonConfirmationEmailExpiration",
                table: "Utilisateurs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JetonConfirmationEmailHash",
                table: "Utilisateurs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_JetonConfirmationEmailHash",
                table: "Utilisateurs",
                column: "JetonConfirmationEmailHash",
                unique: true,
                filter: "\"JetonConfirmationEmailHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Utilisateurs_JetonConfirmationEmailHash",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "DateConfirmationEmail",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "EmailConfirme",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "JetonConfirmationEmailExpiration",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "JetonConfirmationEmailHash",
                table: "Utilisateurs");
        }
    }
}
