using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AjouterSejourCourtEnrichi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Caution",
                table: "Reservations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateReponseHote",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FraisMenage",
                table: "Reservations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "MotifRefus",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NombreNuits",
                table: "Reservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NombreVoyageurs",
                table: "Reservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "PrixParNuit",
                table: "Reservations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReductionMontant",
                table: "Reservations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Capacite",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Caution",
                table: "Appartements",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Chambres",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Equipements",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FraisMenage",
                table: "Appartements",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Lits",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NuitsMin",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Quartier",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReductionMoisPct",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReductionSemainePct",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReservationInstantanee",
                table: "Appartements",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SallesDeBain",
                table: "Appartements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ville",
                table: "Appartements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Indisponibilites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppartementId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateFin = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Motif = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indisponibilites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Indisponibilites_Appartements_AppartementId",
                        column: x => x.AppartementId,
                        principalTable: "Appartements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Indisponibilites_AppartementId",
                table: "Indisponibilites",
                column: "AppartementId");

            // Réservations existantes : détail du prix reconstitué (aucune réduction ni frais à l'époque), 1 voyageur.
            migrationBuilder.Sql(@"
                UPDATE Reservations
                SET NombreNuits = CAST(ROUND(julianday(date(DateDepart)) - julianday(date(DateArrivee))) AS INTEGER)
                WHERE NombreNuits = 0;
                UPDATE Reservations
                SET PrixParNuit = CAST(ROUND(CAST(MontantTotal AS REAL) / NombreNuits, 2) AS TEXT)
                WHERE NombreNuits > 0;");

            // Séjours courts existants : 1 nuit minimum, comme le défaut des nouvelles annonces.
            migrationBuilder.Sql("UPDATE Appartements SET NuitsMin = 1 WHERE TypeOffre = 1 AND NuitsMin IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Indisponibilites");

            migrationBuilder.DropColumn(
                name: "Caution",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DateReponseHote",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "FraisMenage",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "MotifRefus",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "NombreNuits",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "NombreVoyageurs",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PrixParNuit",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReductionMontant",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Capacite",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Caution",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Chambres",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Equipements",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "FraisMenage",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Lits",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "NuitsMin",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Quartier",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "ReductionMoisPct",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "ReductionSemainePct",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "ReservationInstantanee",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "SallesDeBain",
                table: "Appartements");

            migrationBuilder.DropColumn(
                name: "Ville",
                table: "Appartements");
        }
    }
}
