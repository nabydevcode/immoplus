using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterRappelsEcheances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateDernierRecapRetards",
                table: "Utilisateurs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDernierRappelRetard",
                table: "Mensualites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateRappelEcheance",
                table: "Mensualites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDernierRappelRetard",
                table: "Loyers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateRappelEcheance",
                table: "Loyers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateDernierRecapRetards",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "DateDernierRappelRetard",
                table: "Mensualites");

            migrationBuilder.DropColumn(
                name: "DateRappelEcheance",
                table: "Mensualites");

            migrationBuilder.DropColumn(
                name: "DateDernierRappelRetard",
                table: "Loyers");

            migrationBuilder.DropColumn(
                name: "DateRappelEcheance",
                table: "Loyers");
        }
    }
}
