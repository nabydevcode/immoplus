using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class CopieEnvoyesImap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CopieEnvoyes",
                table: "MessagesSortants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopieErreur",
                table: "MessagesSortants",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CopieProchaineTentative",
                table: "MessagesSortants",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CopieTentatives",
                table: "MessagesSortants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MessagesSortants_CopieProchaineTentative",
                table: "MessagesSortants",
                column: "CopieProchaineTentative",
                filter: "\"CopieEnvoyes\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessagesSortants_CopieProchaineTentative",
                table: "MessagesSortants");

            migrationBuilder.DropColumn(
                name: "CopieEnvoyes",
                table: "MessagesSortants");

            migrationBuilder.DropColumn(
                name: "CopieErreur",
                table: "MessagesSortants");

            migrationBuilder.DropColumn(
                name: "CopieProchaineTentative",
                table: "MessagesSortants");

            migrationBuilder.DropColumn(
                name: "CopieTentatives",
                table: "MessagesSortants");
        }
    }
}
