using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterLoyers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LoyerId",
                table: "Paiements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Loyers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    DateEcheance = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MontantDu = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MontantPaye = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Statut = table.Column<int>(type: "INTEGER", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_LoyerId",
                table: "Paiements",
                column: "LoyerId");

            migrationBuilder.CreateIndex(
                name: "IX_Loyers_LocationId",
                table: "Loyers",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Paiements_Loyers_LoyerId",
                table: "Paiements",
                column: "LoyerId",
                principalTable: "Loyers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Paiements_Loyers_LoyerId",
                table: "Paiements");

            migrationBuilder.DropTable(
                name: "Loyers");

            migrationBuilder.DropIndex(
                name: "IX_Paiements_LoyerId",
                table: "Paiements");

            migrationBuilder.DropColumn(
                name: "LoyerId",
                table: "Paiements");
        }
    }
}
