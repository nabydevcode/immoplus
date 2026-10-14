using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterPhotosAppartement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotosAppartement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomFichierOriginal = table.Column<string>(type: "TEXT", nullable: false),
                    CheminStockage = table.Column<string>(type: "TEXT", nullable: false),
                    Ordre = table.Column<int>(type: "INTEGER", nullable: false),
                    DateUpload = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotosAppartement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotosAppartement_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotosAppartement_AppartementId",
                table: "PhotosAppartement",
                column: "AppartementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotosAppartement");
        }
    }
}
