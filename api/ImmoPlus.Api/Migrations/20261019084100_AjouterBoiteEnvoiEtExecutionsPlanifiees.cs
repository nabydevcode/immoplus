using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterBoiteEnvoiEtExecutionsPlanifiees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionsPlanifiees",
                columns: table => new
                {
                    Nom = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DerniereExecution = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Instance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionsPlanifiees", x => x.Nom);
                });

            migrationBuilder.CreateTable(
                name: "MessagesSortants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Destinataire = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UtilisateurId = table.Column<int>(type: "integer", nullable: true),
                    Sujet = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Contenu = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Evenement = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Canal = table.Column<int>(type: "integer", nullable: true),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    Tentatives = table.Column<int>(type: "integer", nullable: false),
                    ProchaineTentative = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DerniereErreur = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateDerniereTentative = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateEnvoi = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagesSortants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessagesSortants_DateCreation",
                table: "MessagesSortants",
                column: "DateCreation");

            migrationBuilder.CreateIndex(
                name: "IX_MessagesSortants_Statut_ProchaineTentative",
                table: "MessagesSortants",
                columns: new[] { "Statut", "ProchaineTentative" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionsPlanifiees");

            migrationBuilder.DropTable(
                name: "MessagesSortants");
        }
    }
}
