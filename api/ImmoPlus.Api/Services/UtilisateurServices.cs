namespace ImmoPlus.Api.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class UtilisateurServices
{
    private static readonly TimeSpan DureeValiditeJeton = TimeSpan.FromHours(1);
    private static readonly TimeSpan DureeValiditeConfirmation = TimeSpan.FromHours(NotificationServices.DureeConfirmationHeures);

    public const string CodeEmailNonConfirme = "EmailNonConfirme";
    public const string MessageEmailNonConfirme = "Confirmez d'abord votre adresse email : un lien vous a été envoyé.";
    public const string MessageRenvoiConfirmation =
        "Si un compte en attente de confirmation existe pour cette adresse, un nouvel email de confirmation vient de lui être envoyé.";

    private readonly AppDbContext _context;
    private readonly TokenServices _tokenServices;
    private readonly EmailServices _emailServices;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<UtilisateurServices> _logger;
    private readonly NotificationServices _notificationServices;
    private readonly ReservationServices _reservationServices;
    private readonly LimiteurTentatives _limiteur;
    private readonly LiensFrontend _liens;
    private readonly JournalServices _journal;
    private readonly BoiteEnvoiServices _boite;
    private readonly ICacheStatutUtilisateur _cacheStatut;
    private readonly DiffusionAdmins _diffusion;

    public UtilisateurServices(AppDbContext context, TokenServices tokenServices, EmailServices emailServices,
        IHostEnvironment environment, ILogger<UtilisateurServices> logger,
        NotificationServices notificationServices, ReservationServices reservationServices, LimiteurTentatives limiteur,
        LiensFrontend liens, JournalServices journal, BoiteEnvoiServices boite, ICacheStatutUtilisateur cacheStatut,
        DiffusionAdmins diffusion)
    {
        _diffusion = diffusion;
        _boite = boite;
        _cacheStatut = cacheStatut;
        _journal = journal;
        _liens = liens;
        _notificationServices = notificationServices;
        _reservationServices = reservationServices;
        _limiteur = limiteur;
        _context = context;
        _tokenServices = tokenServices;
        _emailServices = emailServices;
        _environment = environment;
        _logger = logger;
    }

    private const int LongueurMinMotDePasse = 6;
    private const int LongueurMaxNom = 100;
    private const int LongueurMaxTelephone = 30;
    private const int LongueurMaxEmail = 200;

    /// <summary>Nom, téléphone et email obligatoires, email bien formé, longueurs bornées (inscription et profil).</summary>
    private static (string Nom, string Telephone, string Email) VerifierIdentite(string? nom, string? telephone, string? email)
    {
        nom = nom?.Trim();
        telephone = telephone?.Trim();
        email = email?.Trim();
        if (string.IsNullOrEmpty(nom) || string.IsNullOrEmpty(telephone) || string.IsNullOrEmpty(email))
        {
            throw new InvalidOperationException("Le nom, le téléphone et l'email sont obligatoires");
        }
        if (nom.Length > LongueurMaxNom || telephone.Length > LongueurMaxTelephone || email.Length > LongueurMaxEmail)
        {
            throw new InvalidOperationException($"Le nom ({LongueurMaxNom}), le téléphone ({LongueurMaxTelephone}) et l'email ({LongueurMaxEmail}) dépassent la longueur autorisée");
        }
        // MailAddress accepte « Nom <adresse> » : on exige que l'adresse seule ait été saisie.
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var adresse) || adresse.Address != email || !email.Contains('.'))
        {
            throw new InvalidOperationException("L'adresse email n'est pas valide");
        }
        return (nom, telephone, email);
    }

    /// <summary>Comparaison d'emails insensible à la casse (« Jean@X.com » et « jean@x.com » sont le même compte).</summary>
    private Task<Utilisateur?> TrouverParEmailAsync(string? email)
    {
        var cle = (email ?? "").Trim().ToLower();
        return _context.Utilisateurs.FirstOrDefaultAsync(u => u.Email.ToLower() == cle);
    }

    /// <summary>Identité et mot de passe valides, email libre (inscription et création d'un admin).</summary>
    private async Task<(string Nom, string Telephone, string Email)> VerifierNouveauCompteAsync(string? nom, string? telephone, string? email, string? motDePasse)
    {
        var identite = VerifierIdentite(nom, telephone, email);
        if (string.IsNullOrEmpty(motDePasse) || motDePasse.Length < LongueurMinMotDePasse)
        {
            throw new InvalidOperationException($"Le mot de passe doit contenir au moins {LongueurMinMotDePasse} caractères");
        }
        if (await TrouverParEmailAsync(identite.Email) is not null)
        {
            throw new ConflictException("Un utilisateur avec cet email existe déjà");
        }
        return identite;
    }

    /// <summary>
    /// Enregistrement d'un compte créé ou modifié : deux inscriptions simultanées avec le même email passent le contrôle
    /// préalable, mais l'index unique sur lower(Email) n'en accepte qu'une ; l'autre reçoit le même 409.
    /// </summary>
    private async Task EnregistrerCompteAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ErreursBase.EstViolationUnicite(ex, ErreursBase.IndexEmailUnique))
        {
            throw new ConflictException("Un utilisateur avec cet email existe déjà");
        }
    }

    public async Task<UtilisateurResponseDto> InscrireAsync(InscriptionDto dto)
    {
        var (nom, telephone, email) = await VerifierNouveauCompteAsync(dto.Nom, dto.Telephone, dto.Email, dto.MotDePasse);

        if (!Enum.TryParse<RoleUtilisateur>(dto.Role, true, out var role) || !Enum.IsDefined(role) || int.TryParse(dto.Role, out _))
        {
            throw new InvalidOperationException("Rôle invalide. Utilisez Locataire ou Bailleur");
        }

        // Seul un admin crée un admin (POST /utilisateur/admin). Exception d'amorçage / de secours : s'il n'existe
        // aucun admin actif, l'inscription Admin est acceptée et validée d'office, sinon personne ne pourrait plus rien valider.
        var aucunAdminActif = !await _context.Utilisateurs.AnyAsync(u => u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide);
        if (role == RoleUtilisateur.Admin && !aucunAdminActif)
        {
            throw new InvalidOperationException("Le rôle administrateur ne peut pas être choisi à l'inscription");
        }
        // Le tout premier compte du système est lui aussi validé d'office.
        var estPremierUtilisateur = !await _context.Utilisateurs.AnyAsync();
        // Amorçage : personne ne pourrait valider ce compte, ni donc attendre la confirmation de son email.
        var valideDOffice = estPremierUtilisateur || role == RoleUtilisateur.Admin;
        var maintenant = DateTime.UtcNow;

        var utilisateur = new Utilisateur
        {
            Nom = nom,
            Telephone = telephone,
            Email = email,
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(dto.MotDePasse),
            Role = role,
            DateCreation = maintenant,
            Statut = valideDOffice ? StatutUtilisateur.Valide : StatutUtilisateur.EnAttente,
            EmailConfirme = valideDOffice,
            DateConfirmationEmail = valideDOffice ? maintenant : null
        };
        await _context.Utilisateurs.AddAsync(utilisateur);
        if (valideDOffice)
        {
            await EnregistrerCompteAsync();
            await _diffusion.UtilisateurChangeAsync(utilisateur, "inscrit");
            return VersDto(utilisateur);
        }

        // Compte et email de confirmation enregistrés ensemble (boîte d'envoi) : pas de compte sans lien envoyé.
        var chemin = NouveauJetonConfirmation(utilisateur);
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await EnregistrerCompteAsync();
            await _notificationServices.ConfirmationEmailAsync(utilisateur, chemin);
            await transaction.CommitAsync();
        }
        await _diffusion.UtilisateurChangeAsync(utilisateur, "inscrit");
        return VersDto(utilisateur);
    }

    /// <summary>Nouveau jeton (32 octets aléatoires, seul son hash est gardé, 48 h) ; renvoie le chemin du lien à envoyer.</summary>
    private static string NouveauJetonConfirmation(Utilisateur utilisateur)
    {
        var jeton = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        utilisateur.JetonConfirmationEmailHash = HacherJeton(jeton);
        utilisateur.JetonConfirmationEmailExpiration = DateTime.UtcNow.Add(DureeValiditeConfirmation);
        return $"/confirmer-email?jeton={jeton}";
    }

    /// <summary>
    /// Lien reçu par email : l'adresse est confirmée (une seule fois) et les admins actifs sont prévenus qu'un compte attend
    /// leur validation. Un second clic sur le même lien répond « déjà confirmée » sans rien refaire.
    /// </summary>
    public async Task<ConfirmationEmailResponseDto> ConfirmerEmailAsync(ConfirmerEmailDto dto)
    {
        const string lienInvalide = "Ce lien de confirmation est invalide ou a été remplacé par un plus récent. Demandez un nouvel email de confirmation.";
        var jeton = dto.Jeton?.Trim() ?? "";
        if (jeton.Length == 0)
        {
            throw new InvalidOperationException(lienInvalide);
        }
        var hash = HacherJeton(jeton.ToLowerInvariant());
        var id = await _context.Utilisateurs.AsNoTracking().Where(u => u.JetonConfirmationEmailHash == hash)
            .Select(u => (int?)u.Id).FirstOrDefaultAsync() ?? throw new InvalidOperationException(lienInvalide);

        await using var transaction = await _context.Database.BeginTransactionAsync();
        // Deux clics simultanés : le second attend, puis voit l'adresse déjà confirmée (admins prévenus une seule fois).
        await _context.Database.VerrouillerAsync(RessourceVerrouillee.Utilisateur, id);
        var utilisateur = await _context.Utilisateurs.FirstOrDefaultAsync(u => u.Id == id && u.JetonConfirmationEmailHash == hash)
            ?? throw new InvalidOperationException(lienInvalide);

        if (utilisateur.EmailConfirme)
        {
            await transaction.CommitAsync();
            return new ConfirmationEmailResponseDto
            {
                Email = utilisateur.Email,
                Message = utilisateur.Statut == StatutUtilisateur.Valide
                    ? "Votre adresse email est déjà confirmée et votre compte est validé : vous pouvez vous connecter."
                    : "Votre adresse email est déjà confirmée. Votre compte doit maintenant être validé par un administrateur.",
                Statut = utilisateur.Statut.ToString()
            };
        }
        if (utilisateur.JetonConfirmationEmailExpiration is not { } expiration || expiration < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Ce lien de confirmation a expiré. Demandez un nouvel email de confirmation.");
        }

        utilisateur.EmailConfirme = true;
        utilisateur.DateConfirmationEmail = DateTime.UtcNow;
        // Le hash reste (second clic → « déjà confirmée ») ; sans expiration, il ne confirme plus rien.
        utilisateur.JetonConfirmationEmailExpiration = null;
        await _context.SaveChangesAsync();

        if (utilisateur.Statut == StatutUtilisateur.EnAttente)
        {
            var admins = await _context.Utilisateurs.AsNoTracking()
                .Where(u => u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide)
                .OrderBy(u => u.Id).ToListAsync();
            foreach (var admin in admins)
            {
                await _notificationServices.NouveauCompteAValiderAsync(admin, utilisateur);
            }
        }
        await transaction.CommitAsync();
        await _diffusion.UtilisateurChangeAsync(utilisateur, "emailConfirme");
        await _journal.EnregistrerPourAsync(utilisateur.Id, utilisateur.Nom, utilisateur.Role.ToString(), "utilisateur.confirmer-email",
            "Utilisateur", utilisateur.Id, $"Adresse email de {utilisateur.Nom} ({utilisateur.Email}, {utilisateur.Role}) confirmée");

        return new ConfirmationEmailResponseDto
        {
            Email = utilisateur.Email,
            Message = utilisateur.Statut == StatutUtilisateur.EnAttente
                ? "Adresse confirmée. Votre compte doit maintenant être validé par un administrateur : vous recevrez un email dès qu'il le sera."
                : "Adresse confirmée.",
            Statut = utilisateur.Statut.ToString()
        };
    }

    /// <summary>
    /// Nouvel email de confirmation (l'ancien lien ne sert plus). Toujours la même réponse, que le compte existe ou non ;
    /// au-delà de 3 renvois par heure pour une adresse, plus aucun email (anti-spam de la boîte du destinataire).
    /// </summary>
    public async Task RenvoyerConfirmationAsync(RenvoyerConfirmationDto dto)
    {
        if (!await _limiteur.AutoriserRenvoiConfirmationAsync(dto.Email))
        {
            _logger.LogWarning("Trop de renvois de l'email de confirmation pour une même adresse : demande ignorée");
            return;
        }
        var utilisateur = await TrouverParEmailAsync(dto.Email);
        if (utilisateur is null || utilisateur.EmailConfirme || utilisateur.Statut != StatutUtilisateur.EnAttente)
        {
            _logger.LogInformation("Renvoi de confirmation demandé pour une adresse inconnue ou déjà confirmée");
            return;
        }

        var chemin = NouveauJetonConfirmation(utilisateur);
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await _context.SaveChangesAsync();
            await _notificationServices.ConfirmationEmailAsync(utilisateur, chemin);
            await transaction.CommitAsync();
        }
        await _journal.EnregistrerPourAsync(utilisateur.Id, utilisateur.Nom, utilisateur.Role.ToString(), "utilisateur.renvoyer-confirmation",
            "Utilisateur", utilisateur.Id, $"Nouvel email de confirmation envoyé à {utilisateur.Email}");
    }

    /// <summary>Un admin crée un autre admin, validé d'office ; le nouvel admin reçoit un email de bienvenue.</summary>
    public async Task<UtilisateurResponseDto> CreerAdminAsync(CreerAdminDto dto)
    {
        var (nom, telephone, email) = await VerifierNouveauCompteAsync(dto.Nom, dto.Telephone, dto.Email, dto.MotDePasse);
        var admin = new Utilisateur
        {
            Nom = nom,
            Telephone = telephone,
            Email = email,
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(dto.MotDePasse),
            Role = RoleUtilisateur.Admin,
            Statut = StatutUtilisateur.Valide,
            DateCreation = DateTime.UtcNow,
            // Adresse saisie par un admin : confirmée d'office (pas d'email de confirmation).
            EmailConfirme = true,
            DateConfirmationEmail = DateTime.UtcNow
        };
        await _context.Utilisateurs.AddAsync(admin);
        await EnregistrerCompteAsync();
        await _diffusion.UtilisateurChangeAsync(admin, "adminCree");
        await _notificationServices.BienvenueAdminAsync(admin);
        await _journal.EnregistrerAsync("utilisateur.creer-admin", "Utilisateur", admin.Id, $"Compte administrateur créé pour {admin.Nom} ({admin.Email})");
        return VersDto(admin);
    }

    public async Task DemanderReinitialisationAsync(MotDePasseOublieDto dto)
    {
        // Au-delà de 5 demandes en 15 min pour un même email : même réponse, mais plus aucun email (anti-spam de la boîte du destinataire).
        if (!await _limiteur.AutoriserReinitialisationAsync(dto.Email))
        {
            _logger.LogWarning("Trop de demandes de réinitialisation pour un même email : demande ignorée");
            return;
        }
        var utilisateur = await TrouverParEmailAsync(dto.Email);
        if (utilisateur is null)
        {
            // Même réponse qu'un compte existant : la route ne doit pas permettre de deviner quels emails sont inscrits.
            _logger.LogInformation("Réinitialisation demandée pour un email inconnu");
            return;
        }

        var jeton = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        utilisateur.JetonReinitialisationHash = HacherJeton(jeton);
        utilisateur.JetonReinitialisationExpiration = DateTime.UtcNow.Add(DureeValiditeJeton);

        var lien = $"{_liens.Url}/reinitialiser-mot-de-passe?jeton={jeton}";

        // Jeton et email (boîte d'envoi) enregistrés ensemble ; l'envoi part en arrière-plan avec nouvelles tentatives
        // (SMTP non configuré : l'email attend dans la boîte d'envoi jusqu'à sa configuration).
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await _context.SaveChangesAsync();
            await _boite.AjouterEmailAsync(utilisateur.Email, utilisateur.Id, "Réinitialisation de votre mot de passe ImmoPlus",
                $"""
                <p>Bonjour {System.Net.WebUtility.HtmlEncode(utilisateur.Nom)},</p>
                <p>Vous avez demandé à réinitialiser votre mot de passe ImmoPlus.</p>
                <p><a href="{lien}">Choisir un nouveau mot de passe</a></p>
                <p>Ce lien est valable 1 heure. Si vous n'êtes pas à l'origine de cette demande, ignorez cet email.</p>
                """, "ReinitialisationMotDePasse", null);
            await transaction.CommitAsync();
        }
        if (_emailServices.EstConfigure)
        {
            return;
        }
        if (_environment.IsDevelopment())
        {
            _logger.LogWarning("SMTP non configuré, lien de réinitialisation pour {Email} : {Lien}", utilisateur.Email, lien);
        }
        else
        {
            _logger.LogWarning("Réinitialisation demandée pour {Email} : email en attente, SMTP non configuré", utilisateur.Email);
        }
    }

    public async Task ReinitialiserMotDePasseAsync(ReinitialisationMotDePasseDto dto)
    {
        if (string.IsNullOrEmpty(dto.NouveauMotDePasse) || dto.NouveauMotDePasse.Length < LongueurMinMotDePasse)
        {
            throw new InvalidOperationException($"Le mot de passe doit contenir au moins {LongueurMinMotDePasse} caractères");
        }

        var jetonHash = HacherJeton(dto.Jeton ?? string.Empty);
        var utilisateur = await _context.Utilisateurs.FirstOrDefaultAsync(u => u.JetonReinitialisationHash == jetonHash);
        if (utilisateur is null || utilisateur.JetonReinitialisationExpiration < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Ce lien de réinitialisation est invalide ou a expiré");
        }

        utilisateur.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(dto.NouveauMotDePasse);
        utilisateur.JetonReinitialisationHash = null;
        utilisateur.JetonReinitialisationExpiration = null;
        await _context.SaveChangesAsync();
    }

    private static string HacherJeton(string jeton) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jeton)));

    /// <param name="adresseIp">Adresse du client (limitation des essais par IP).</param>
    public async Task<UtilisateurResponseDto> ConnexionAsync(ConnexionDto dto, string? adresseIp = null)
    {
        await _limiteur.VerifierConnexionAsync(dto.Email, adresseIp);
        var utilisateur = await TrouverParEmailAsync(dto.Email);
        if (utilisateur is null || string.IsNullOrEmpty(dto.MotDePasse) || !BCrypt.Net.BCrypt.Verify(dto.MotDePasse, utilisateur.MotDePasseHash))
        {
            await _limiteur.EnregistrerEchecConnexionAsync(dto.Email, adresseIp);
            // Jamais le mot de passe : seulement l'email saisi (tronqué) et la raison.
            var emailSaisi = (dto.Email ?? "").Trim();
            await _journal.EnregistrerPourAsync(utilisateur?.Id, utilisateur?.Nom ?? "Visiteur", utilisateur?.Role.ToString() ?? "Anonyme",
                "connexion.echec", "Utilisateur", utilisateur?.Id,
                $"Échec de connexion pour « {(emailSaisi.Length > 200 ? emailSaisi[..200] : emailSaisi)} » ({(utilisateur is null ? "email inconnu" : "mot de passe incorrect")})");
            throw new NotFoundException("Email ou mot de passe incorrect");
        }
        await _limiteur.ReinitialiserConnexionAsync(dto.Email);

        // Seul un compte en attente peut ne pas être confirmé (comptes existants et admins : confirmés d'office).
        var emailNonConfirme = utilisateur.Statut == StatutUtilisateur.EnAttente && !utilisateur.EmailConfirme;
        if (utilisateur.Statut != StatutUtilisateur.Valide)
        {
            await _journal.EnregistrerPourAsync(utilisateur.Id, utilisateur.Nom, utilisateur.Role.ToString(), "connexion.refusee", "Utilisateur",
                utilisateur.Id, $"Connexion refusée : compte {utilisateur.Statut}{(emailNonConfirme ? " (email non confirmé)" : "")}");
        }
        if (emailNonConfirme)
        {
            // Code testé par le front : message dédié et bouton « Renvoyer l'email de confirmation ».
            throw new ConflictException(MessageEmailNonConfirme, CodeEmailNonConfirme);
        }
        if (utilisateur.Statut == StatutUtilisateur.EnAttente)
        {
            throw new ConflictException("Votre compte est en attente de validation par un administrateur");
        }
        if (utilisateur.Statut == StatutUtilisateur.Refuse)
        {
            throw new ConflictException("Votre compte n'a pas été validé par l'administrateur");
        }
        if (utilisateur.Statut == StatutUtilisateur.Desactive)
        {
            throw new ConflictException("Votre compte a été désactivé par un administrateur. Contactez ImmoPlus pour plus d'informations");
        }
        await _journal.EnregistrerPourAsync(utilisateur.Id, utilisateur.Nom, utilisateur.Role.ToString(), "connexion.reussie", "Utilisateur",
            utilisateur.Id, $"Connexion de {utilisateur.Email}");

        return new UtilisateurResponseDto
        {
            Id = utilisateur.Id,
            Nom = utilisateur.Nom,
            Email = utilisateur.Email,
            Telephone = utilisateur.Telephone,
            Role = utilisateur.Role.ToString(),
            Statut = utilisateur.Statut.ToString(),
            NotificationsSms = utilisateur.NotificationsSms,
            IdentiteVerifiee = utilisateur.IdentiteVerifiee,
            EmailConfirme = utilisateur.EmailConfirme,
            DateConfirmationEmail = utilisateur.DateConfirmationEmail,
            Token = _tokenServices.GenererToken(utilisateur)
        };
    }

    public async Task<UtilisateurResponseDto> ValiderAsync(int id)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(id);
        if (utilisateur is null)
        {
            throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        // Un compte désactivé se réactive (avec son email) ; un compte déjà actif n'a rien à valider.
        if (utilisateur.Statut is not (StatutUtilisateur.EnAttente or StatutUtilisateur.Refuse))
        {
            throw new ConflictException(utilisateur.Statut == StatutUtilisateur.Desactive
                ? "Ce compte est désactivé : utilisez la réactivation"
                : "Ce compte est déjà validé");
        }
        // L'admin ne valide qu'une adresse dont l'utilisateur a prouvé qu'elle est la sienne.
        if (!utilisateur.EmailConfirme && utilisateur.Role != RoleUtilisateur.Admin)
        {
            throw new ConflictException("L'adresse email n'est pas encore confirmée : l'utilisateur doit d'abord cliquer sur le lien reçu par email");
        }
        utilisateur.Statut = StatutUtilisateur.Valide;
        await _context.SaveChangesAsync();
        await _cacheStatut.InvaliderAsync(utilisateur.Id);
        await _diffusion.UtilisateurChangeAsync(utilisateur, "valide");
        await _notificationServices.CompteValideAsync(utilisateur);
        await _journal.EnregistrerAsync("utilisateur.valider", "Utilisateur", id, $"Compte de {utilisateur.Nom} ({utilisateur.Email}, {utilisateur.Role}) validé");

        return VersDto(utilisateur);
    }

    public async Task<UtilisateurResponseDto> RefuserAsync(int id)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(id);
        if (utilisateur is null)
        {
            throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        // Un compte actif se suspend par la désactivation (motif, email, sessions coupées), pas par un refus.
        if (utilisateur.Statut != StatutUtilisateur.EnAttente)
        {
            throw new ConflictException("Seul un compte en attente de validation peut être refusé");
        }
        utilisateur.Statut = StatutUtilisateur.Refuse;
        await _context.SaveChangesAsync();
        await _cacheStatut.InvaliderAsync(utilisateur.Id);
        await _diffusion.UtilisateurChangeAsync(utilisateur, "refuse");
        // Adresse non confirmée : rien ne prouve qu'elle appartient à la personne inscrite, on ne lui écrit pas.
        if (utilisateur.EmailConfirme)
        {
            await _notificationServices.InscriptionRefuseeAsync(utilisateur);
        }
        await _journal.EnregistrerAsync("utilisateur.refuser", "Utilisateur", id, $"Inscription de {utilisateur.Nom} ({utilisateur.Email}) refusée");

        return VersDto(utilisateur);
    }

    /// <summary>Suspend un compte validé : la connexion et les sessions en cours sont bloquées, ses annonces sont masquées.</summary>
    public async Task<UtilisateurResponseDto> DesactiverAsync(int id, int adminId, string? motif)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(id)
            ?? throw new NotFoundException("Cet utilisateur n'existe pas");
        if (id == adminId)
        {
            throw new ConflictException("Vous ne pouvez pas désactiver votre propre compte");
        }
        if (utilisateur.Statut != StatutUtilisateur.Valide)
        {
            throw new ConflictException("Seul un compte actif (validé) peut être désactivé");
        }
        if (utilisateur.Role == RoleUtilisateur.Admin
            && !await _context.Utilisateurs.AnyAsync(u => u.Id != id && u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide))
        {
            throw new ConflictException("Impossible de désactiver le dernier administrateur actif");
        }
        motif = string.IsNullOrWhiteSpace(motif) ? null : motif.Trim();
        if (motif is { Length: > 500 })
        {
            throw new InvalidOperationException("Le motif ne doit pas dépasser 500 caractères");
        }

        utilisateur.Statut = StatutUtilisateur.Desactive;
        utilisateur.MotifDesactivation = motif;
        utilisateur.DateDesactivation = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        // Sessions coupées tout de suite sur toutes les instances (cache du statut effacé).
        await _cacheStatut.InvaliderAsync(utilisateur.Id);
        await _diffusion.UtilisateurChangeAsync(utilisateur, "desactive");
        await _notificationServices.CompteDesactiveAsync(utilisateur, motif);

        var dto = VersDto(utilisateur);
        if (utilisateur.Role == RoleUtilisateur.Bailleur)
        {
            // Les voyageurs non confirmés ne restent pas en suspens ; les séjours confirmés sont signalés à l'admin, qui décide.
            var (annulees, confirmeesAVenir) = await _reservationServices.AnnulerReservationsHoteDesactiveAsync(utilisateur.Id);
            dto.ReservationsAnnulees = annulees;
            dto.ReservationsConfirmeesAVenir = confirmeesAVenir;
        }
        await _journal.EnregistrerAsync("utilisateur.desactiver", "Utilisateur", id,
            $"Compte de {utilisateur.Nom} ({utilisateur.Email}) désactivé{(motif is null ? "" : $" : {motif}")}"
            + (dto.ReservationsAnnulees is > 0 ? $" — {dto.ReservationsAnnulees} réservation(s) annulée(s)" : ""));
        return dto;
    }

    public async Task<UtilisateurResponseDto> ReactiverAsync(int id)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(id)
            ?? throw new NotFoundException("Cet utilisateur n'existe pas");
        if (utilisateur.Statut != StatutUtilisateur.Desactive)
        {
            throw new ConflictException("Ce compte n'est pas désactivé");
        }
        utilisateur.Statut = StatutUtilisateur.Valide;
        utilisateur.MotifDesactivation = null;
        utilisateur.DateDesactivation = null;
        await _context.SaveChangesAsync();
        await _cacheStatut.InvaliderAsync(utilisateur.Id);
        await _diffusion.UtilisateurChangeAsync(utilisateur, "reactive");
        await _notificationServices.CompteReactiveAsync(utilisateur);
        await _journal.EnregistrerAsync("utilisateur.reactiver", "Utilisateur", id, $"Compte de {utilisateur.Nom} ({utilisateur.Email}) réactivé");
        return VersDto(utilisateur);
    }

    public async Task<PagedResultDto<UtilisateurResponseDto>> ListerAsync(int page = 1, int taille = 20,
        string? role = null, string? statut = null, string? recherche = null, bool? emailConfirme = null)
    {
        if (page < 1) page = 1;
        // Au-delà de 100, on plafonne (au lieu de revenir silencieusement à 20) : le client voit TotalPages et pagine.
        taille = taille < 1 ? 20 : Math.Min(taille, 100);

        var requete = _context.Utilisateurs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!Enum.TryParse<RoleUtilisateur>(role.Trim(), true, out var r) || !Enum.IsDefined(r) || int.TryParse(role, out _))
            {
                throw new InvalidOperationException("Rôle invalide. Utilisez Locataire, Bailleur ou Admin");
            }
            requete = requete.Where(u => u.Role == r);
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (!Enum.TryParse<StatutUtilisateur>(statut.Trim(), true, out var st) || !Enum.IsDefined(st) || int.TryParse(statut, out _))
            {
                throw new InvalidOperationException("Statut invalide. Utilisez EnAttente, Valide, Refuse ou Desactive");
            }
            requete = requete.Where(u => u.Statut == st);
        }
        // Onglets admin : « À valider » = EnAttente + confirmé, « Email non confirmé » = EnAttente + non confirmé.
        if (emailConfirme is { } confirme)
        {
            requete = requete.Where(u => u.EmailConfirme == confirme);
        }
        if (!string.IsNullOrWhiteSpace(recherche))
        {
            // Sans casse ni accents (« eloise » trouve « Éloïse »).
            var motif = RechercheTexte.Contient(recherche);
            // Index trigrammes sur le nom (sans accents), l'email et le téléphone (migration IndexEtPerformances).
            requete = requete.Where(u => EF.Functions.ILike(AppDbContext.SansAccents(u.Nom), AppDbContext.SansAccents(motif))
                || EF.Functions.ILike(u.Email, motif) || EF.Functions.ILike(u.Telephone, motif));
        }

        var totalElements = await requete.CountAsync();

        var donnees = await requete
            .OrderBy(u => u.Id)
            .Skip((page - 1) * taille)
            .Take(taille)
            .Select(u => new UtilisateurResponseDto
            {
                Id = u.Id,
                Nom = u.Nom,
                Telephone = u.Telephone,
                Email = u.Email,
                Role = u.Role.ToString(),
                Statut = u.Statut.ToString(),
                MotifDesactivation = u.MotifDesactivation,
                DateDesactivation = u.DateDesactivation,
                NotificationsSms = u.NotificationsSms,
                IdentiteVerifiee = u.IdentiteVerifiee,
                EmailConfirme = u.EmailConfirme,
                DateConfirmationEmail = u.DateConfirmationEmail
            })
            .ToListAsync();

        return new PagedResultDto<UtilisateurResponseDto>
        {
            Donnees = donnees,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = totalElements,
            TotalPages = (int)Math.Ceiling(totalElements / (double)taille)
        };
    }

    // --- Profil de l'utilisateur connecté (l'id vient toujours du token) ---

    private async Task<Utilisateur> ChargerAsync(int id) =>
        await _context.Utilisateurs.FindAsync(id) ?? throw new NotFoundException("Cet utilisateur n'existe pas");

    private static UtilisateurResponseDto VersDto(Utilisateur u) => new()
    {
        Id = u.Id,
        Nom = u.Nom,
        Telephone = u.Telephone,
        Email = u.Email,
        Role = u.Role.ToString(),
        Statut = u.Statut.ToString(),
        MotifDesactivation = u.MotifDesactivation,
        DateDesactivation = u.DateDesactivation,
        NotificationsSms = u.NotificationsSms,
        IdentiteVerifiee = u.IdentiteVerifiee,
        EmailConfirme = u.EmailConfirme,
        DateConfirmationEmail = u.DateConfirmationEmail
    };

    public async Task<UtilisateurResponseDto> ObtenirProfilAsync(int id) => VersDto(await ChargerAsync(id));

    public async Task<UtilisateurResponseDto> ModifierProfilAsync(int id, ModifierProfilDto dto)
    {
        var (nom, telephone, email) = VerifierIdentite(dto.Nom, dto.Telephone, dto.Email);

        var utilisateur = await ChargerAsync(id);
        var cle = email.ToLower();
        if (email != utilisateur.Email && await _context.Utilisateurs.AnyAsync(u => u.Email.ToLower() == cle && u.Id != id))
        {
            throw new ConflictException("Un utilisateur avec cet email existe déjà");
        }

        // Seuls nom, téléphone et email apparaissent dans la liste des admins : la préférence SMS ne la rafraîchit pas.
        var identiteModifiee = nom != utilisateur.Nom || telephone != utilisateur.Telephone || email != utilisateur.Email;
        utilisateur.Nom = nom;
        utilisateur.Telephone = telephone;
        utilisateur.Email = email;
        // Absent du corps : préférence inchangée (anciens clients).
        if (dto.NotificationsSms is { } sms)
        {
            utilisateur.NotificationsSms = sms;
        }
        await EnregistrerCompteAsync();
        if (identiteModifiee)
        {
            await _diffusion.UtilisateurChangeAsync(utilisateur, "profilModifie");
        }
        return VersDto(utilisateur);
    }

    public async Task ChangerMotDePasseAsync(int id, ChangerMotDePasseDto dto)
    {
        var utilisateur = await ChargerAsync(id);
        if (string.IsNullOrEmpty(dto.MotDePasseActuel) || !BCrypt.Net.BCrypt.Verify(dto.MotDePasseActuel, utilisateur.MotDePasseHash))
        {
            throw new InvalidOperationException("Mot de passe actuel incorrect");
        }
        if (string.IsNullOrEmpty(dto.NouveauMotDePasse) || dto.NouveauMotDePasse.Length < LongueurMinMotDePasse)
        {
            throw new InvalidOperationException($"Le mot de passe doit contenir au moins {LongueurMinMotDePasse} caractères");
        }
        if (dto.NouveauMotDePasse == dto.MotDePasseActuel)
        {
            throw new InvalidOperationException("Le nouveau mot de passe doit être différent de l'actuel");
        }

        utilisateur.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(dto.NouveauMotDePasse);
        // Un éventuel lien « mot de passe oublié » encore valide ne doit plus servir.
        utilisateur.JetonReinitialisationHash = null;
        utilisateur.JetonReinitialisationExpiration = null;
        await _context.SaveChangesAsync();

        await _notificationServices.MotDePasseModifieAsync(utilisateur);
    }
}
