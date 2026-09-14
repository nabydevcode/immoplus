using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterValidationUtilisateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Statut",
                table: "Utilisateurs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Statut",
                table: "Utilisateurs");
        }
    }
}
