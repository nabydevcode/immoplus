namespace ImmoPlus.Api.Middleware;

using ImmoPlus.Api.Exceptions;
using System.Text.Json;

public class MiddlewareException
{
    private readonly RequestDelegate _next;
    public MiddlewareException(RequestDelegate next)
    {
        _next = next;
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
        catch (InvalidOperationException ex)
        {
            await EcrireErreur(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            await EcrireErreur(context, StatusCodes.Status500InternalServerError, "Une erreur interne est survenue.");
            Console.WriteLine(ex);
        }
    }

    public static async Task EcrireErreur(HttpContext context, int statusCode, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;
        var reponse = JsonSerializer.Serialize(new { message });
        await context.Response.WriteAsync(reponse);
    }
}
