using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterReinitialisationMotDePasse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "JetonReinitialisationExpiration",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JetonReinitialisationHash",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JetonReinitialisationExpiration",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "JetonReinitialisationHash",
                table: "Utilisateurs");
        }
    }
}
