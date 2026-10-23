namespace ImmoPlus.Api.Middleware;

using ImmoPlus.Api.Exceptions;
using System.Data.Common;
using System.Text.Json;

public class MiddlewareException
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MiddlewareException> _logger;

    public MiddlewareException(RequestDelegate next, ILogger<MiddlewareException> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            await EcrireErreur(context, StatusCodes.Status404NotFound, ex.Message);
        }
        catch (ConflictException ex)
        {
            await EcrireErreur(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (TropDeTentativesException ex)
        {
            await EcrireErreur(context, StatusCodes.Status429TooManyRequests, ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            // Corps JSON illisible, paramètre obligatoire absent ou mal typé : erreur de saisie (400), pas une panne du serveur.
            await EcrireErreur(context, ex.StatusCode, ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? "Fichier ou requête trop volumineux"
                : "Requête invalide : vérifiez les champs envoyés (format JSON, paramètres obligatoires et leur type)");
        }
        catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested && EstIndisponibiliteBase(ex))
        {
            // Base saturée ou injoignable (pool Npgsql épuisé, « too many clients », délai dépassé…) : EF Core l'enveloppe parfois
            // dans une InvalidOperationException (« transient failure ») qui finirait sinon en 400 avec un message technique.
            _logger.LogError(ex, "Base de données indisponible sur {Methode} {Chemin}", context.Request.Method, context.Request.Path);
            if (!context.Response.HasStarted)
            {
                context.Response.Headers.RetryAfter = SecondesAvantNouvelEssai.ToString();
            }
            await EcrireErreur(context, StatusCodes.Status503ServiceUnavailable, MessageIndisponible);
        }
        catch (InvalidOperationException ex)
        {
            await EcrireErreur(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Le client a fermé la connexion : rien à lui répondre.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur non gérée sur {Methode} {Chemin}", context.Request.Method, context.Request.Path);
            await EcrireErreur(context, StatusCodes.Status500InternalServerError, "Une erreur interne est survenue.");
        }
    }

    public const string MessageIndisponible = "Service momentanément indisponible, réessayez dans quelques instants";
    public const int SecondesAvantNouvelEssai = 5;

    /// <summary>
    /// Panne passagère de la base dans la chaîne d'exceptions : erreur de connexion ou de pool Npgsql, délai dépassé, ressources
    /// PostgreSQL épuisées (53xxx, dont « too many clients »), arrêt ou démarrage du serveur (57P0x), connexion perdue (08xxx),
    /// conflit de sérialisation ou interblocage (40xxx). Une erreur de données ou de requête (contrainte, syntaxe : classes 22,
    /// 23, 42…) reste une erreur interne : réessayer n'y changerait rien.
    /// </summary>
    public static bool EstIndisponibiliteBase(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is Npgsql.PostgresException pg)
            {
                return pg.SqlState.StartsWith("53", StringComparison.Ordinal) || pg.SqlState.StartsWith("57P", StringComparison.Ordinal)
                    || pg.SqlState.StartsWith("08", StringComparison.Ordinal) || pg.SqlState.StartsWith("40", StringComparison.Ordinal);
            }
            if (ex is DbException or TimeoutException)
            {
                return true;
            }
        }
        return false;
    }

    public static async Task EcrireErreur(HttpContext context, int statusCode, string message)
    {
        // Réponse déjà partie (fichier en cours d'envoi) : impossible de la remplacer par un message d'erreur.
        if (context.Response.HasStarted)
        {
            return;
        }
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;
        var reponse = JsonSerializer.Serialize(new { message });
        await context.Response.WriteAsync(reponse);
    }
}
