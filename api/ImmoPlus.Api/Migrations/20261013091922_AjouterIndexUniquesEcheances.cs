using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterIndexUniquesEcheances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mensualites_FinancementLocationId",
                table: "Mensualites");

            migrationBuilder.DropIndex(
                name: "IX_Loyers_LocationId",
                table: "Loyers");

            migrationBuilder.CreateIndex(
                name: "IX_Mensualites_FinancementLocationId_NumeroEcheance",
                table: "Mensualites",
                columns: new[] { "FinancementLocationId", "NumeroEcheance" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loyers_LocationId_Numero",
                table: "Loyers",
                columns: new[] { "LocationId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mensualites_FinancementLocationId_NumeroEcheance",
                table: "Mensualites");

            migrationBuilder.DropIndex(
                name: "IX_Loyers_LocationId_Numero",
                table: "Loyers");

            migrationBuilder.CreateIndex(
                name: "IX_Mensualites_FinancementLocationId",
                table: "Mensualites",
                column: "FinancementLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Loyers_LocationId",
                table: "Loyers",
                column: "LocationId");
        }
    }
}
