namespace ImmoPlus.Api.Services;

using System.Net.Mail;

public class EmailServices
{
    private readonly IConfiguration _configuration;

    public EmailServices(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool EstConfigure => !string.IsNullOrWhiteSpace(_configuration["Smtp:Host"]);

    public async Task EnvoyerAsync(string destinataire, string sujet, string corpsHtml)
    {
        using var client = new SmtpClient(_configuration["Smtp:Host"], _configuration.GetValue("Smtp:Port", 25));
        using var message = new MailMessage(_configuration["Smtp:Expediteur"] ?? "no-reply@immoplus.local", destinataire)
        {
            Subject = sujet,
            Body = corpsHtml,
            IsBodyHtml = true
        };
        // Envoi fait par la boîte d'envoi, dans une transaction : un serveur muet ne la bloque pas plus de 30 s.
        using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, delai.Token);
    }
}
