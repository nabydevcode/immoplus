using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterApportEtRemiseCles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateApportPaye",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateRemiseCles",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModePaiementApport",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceApport",
                table: "Locations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateApportPaye",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "DateRemiseCles",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ModePaiementApport",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ReferenceApport",
                table: "Locations");
        }
    }
}
