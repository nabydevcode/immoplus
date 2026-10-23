namespace ImmoPlus.Api.Services;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Sérialisation JSON des dates : toutes les dates de l'API sont en UTC (colonnes « timestamp without time zone », voir AppDbContext).
/// Les horodatages sont donc écrits avec « Z » (ex. « 2026-10-13T09:41:12.1234567Z ») pour que le client les convertisse
/// à l'heure locale. Les dates « jour » (minuit pile : arrivée, départ, échéance…) restent « yyyy-MM-ddT00:00:00 », sans « Z »,
/// pour ne jamais se décaler d'un jour dans un fuseau négatif. La lecture est inchangée (comportement par défaut).
/// </summary>
public class ConvertisseurDateUtc : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        if (value.TimeOfDay == TimeSpan.Zero)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            return;
        }
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString("O", CultureInfo.InvariantCulture));
    }
}
