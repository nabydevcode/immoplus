using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class IndexReservationsHote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AppartementId_Id",
                table: "Reservations",
                columns: new[] { "AppartementId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_AppartementId_Id",
                table: "Reservations");
        }
    }
}
