namespace ImmoPlus.Api.Exceptions;

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }

    /// <param name="code">Code stable que le front peut tester (ex. « EmailNonConfirme ») ; renvoyé en plus du message.</param>
    public ConflictException(string message, string code) : base(message)
    {
        Code = code;
    }

    public string? Code { get; }
}
