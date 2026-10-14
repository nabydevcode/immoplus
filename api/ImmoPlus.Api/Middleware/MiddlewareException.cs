namespace ImmoPlus.Api.Middleware;

using ImmoPlus.Api.Exceptions;
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
