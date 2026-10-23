using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImmoPlus.Api.Migrations
{
    /// <inheritdoc />
    public partial class IndexEtPerformances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_AppartementId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_LocataireId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ReservationId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Locations_AppartementId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Indisponibilites_AppartementId",
                table: "Indisponibilites");

            migrationBuilder.DropIndex(
                name: "IX_Candidatures_AppartementId",
                table: "Candidatures");

            migrationBuilder.DropIndex(
                name: "IX_Candidatures_LocataireId",
                table: "Candidatures");

            migrationBuilder.DropIndex(
                name: "IX_Avis_AppartementId",
                table: "Avis");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_Role_Statut",
                table: "Utilisateurs",
                columns: new[] { "Role", "Statut" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AppartementId_Dates",
                table: "Reservations",
                columns: new[] { "AppartementId", "DateArrivee", "DateDepart" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Confirmees_DateDepart",
                table: "Reservations",
                column: "DateDepart",
                filter: "\"Statut\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_DateCreation",
                table: "Reservations",
                columns: new[] { "DateCreation", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_EnAttente_DateCreation",
                table: "Reservations",
                column: "DateCreation",
                filter: "\"Statut\" IN (0, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LocataireId_DateCreation",
                table: "Reservations",
                columns: new[] { "LocataireId", "DateCreation", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_NonLus",
                table: "Messages",
                columns: new[] { "ReservationId", "AuteurId" },
                filter: "\"DateLecture\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReservationId_Id",
                table: "Messages",
                columns: new[] { "ReservationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_AppartementId_Statut",
                table: "Locations",
                columns: new[] { "AppartementId", "Statut" });

            migrationBuilder.CreateIndex(
                name: "IX_Indisponibilites_AppartementId_Dates",
                table: "Indisponibilites",
                columns: new[] { "AppartementId", "DateDebut", "DateFin" });

            migrationBuilder.CreateIndex(
                name: "IX_Favoris_UtilisateurId_DateCreation",
                table: "Favoris",
                columns: new[] { "UtilisateurId", "DateCreation" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_AppartementId_Statut",
                table: "Candidatures",
                columns: new[] { "AppartementId", "Statut" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_DateSoumission",
                table: "Candidatures",
                columns: new[] { "DateSoumission", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_LocataireId_DateSoumission",
                table: "Candidatures",
                columns: new[] { "LocataireId", "DateSoumission", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Avis_AppartementId_DateCreation",
                table: "Avis",
                columns: new[] { "AppartementId", "DateCreation", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Appartements_Position",
                table: "Appartements",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Appartements_TypeOffre_Disponible",
                table: "Appartements",
                columns: new[] { "TypeOffre", "Disponible", "Id" },
                filter: "NOT \"EstDesactive\"");

            // Recherches « contient » sans accents ni casse (ville, quartier, adresse, noms, email, téléphone) : unaccent() n'est
            // pas IMMUTABLE et ne peut donc pas être indexée ; cette enveloppe l'est (dictionnaire fixé), et les requêtes l'appellent
            // via AppDbContext.SansAccents. Index GIN trigrammes (pg_trgm) : ILIKE '%…%' n'a plus besoin de lire toute la table.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION immoplus_sans_accents(texte text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, texte) $$;
                """);
            migrationBuilder.Sql("""CREATE INDEX "IX_Appartements_Ville_Trigrammes" ON "Appartements" USING gin (immoplus_sans_accents("Ville") gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_Appartements_Quartier_Trigrammes" ON "Appartements" USING gin (immoplus_sans_accents("Quartier") gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_Appartements_Adresse_Trigrammes" ON "Appartements" USING gin (immoplus_sans_accents("Adresse") gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_Utilisateurs_Nom_Trigrammes" ON "Utilisateurs" USING gin (immoplus_sans_accents("Nom") gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_Utilisateurs_Email_Trigrammes" ON "Utilisateurs" USING gin ("Email" gin_trgm_ops);""");
            migrationBuilder.Sql("""CREATE INDEX "IX_Utilisateurs_Telephone_Trigrammes" ON "Utilisateurs" USING gin ("Telephone" gin_trgm_ops);""");
            // Statistiques des nouveaux index sur expressions : sans elles, le planificateur sous-estime les lignes trouvées.
            migrationBuilder.Sql("""ANALYZE "Appartements"; ANALYZE "Utilisateurs"; ANALYZE "Reservations"; ANALYZE "Messages";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var index in new[] { "Appartements_Ville", "Appartements_Quartier", "Appartements_Adresse", "Utilisateurs_Nom",
                         "Utilisateurs_Email", "Utilisateurs_Telephone" })
            {
                migrationBuilder.Sql($"DROP INDEX IF EXISTS \"IX_{index}_Trigrammes\";");
            }
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS immoplus_sans_accents(text);");

            migrationBuilder.DropIndex(
                name: "IX_Utilisateurs_Role_Statut",
                table: "Utilisateurs");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_AppartementId_Dates",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_Confirmees_DateDepart",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_DateCreation",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_EnAttente_DateCreation",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_LocataireId_DateCreation",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Messages_NonLus",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ReservationId_Id",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Locations_AppartementId_Statut",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Indisponibilites_AppartementId_Dates",
                table: "Indisponibilites");

            migrationBuilder.DropIndex(
                name: "IX_Favoris_UtilisateurId_DateCreation",
                table: "Favoris");

            migrationBuilder.DropIndex(
                name: "IX_Candidatures_AppartementId_Statut",
                table: "Candidatures");

            migrationBuilder.DropIndex(
                name: "IX_Candidatures_DateSoumission",
                table: "Candidatures");

            migrationBuilder.DropIndex(
                name: "IX_Candidatures_LocataireId_DateSoumission",
                table: "Candidatures");

            migrationBuilder.DropIndex(
                name: "IX_Avis_AppartementId_DateCreation",
                table: "Avis");

            migrationBuilder.DropIndex(
                name: "IX_Appartements_Position",
                table: "Appartements");

            migrationBuilder.DropIndex(
                name: "IX_Appartements_TypeOffre_Disponible",
                table: "Appartements");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AppartementId",
                table: "Reservations",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LocataireId",
                table: "Reservations",
                column: "LocataireId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReservationId",
                table: "Messages",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_AppartementId",
                table: "Locations",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Indisponibilites_AppartementId",
                table: "Indisponibilites",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_AppartementId",
                table: "Candidatures",
                column: "AppartementId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_LocataireId",
                table: "Candidatures",
                column: "LocataireId");

            migrationBuilder.CreateIndex(
                name: "IX_Avis_AppartementId",
                table: "Avis",
                column: "AppartementId");
        }
    }
}
