namespace ImmoPlus.Api.Exceptions;

/// <summary>Trop de tentatives (connexion) : converti en 429 par MiddlewareException.</summary>
public class TropDeTentativesException : Exception
{
    public TropDeTentativesException(string message) : base(message) { }
}
