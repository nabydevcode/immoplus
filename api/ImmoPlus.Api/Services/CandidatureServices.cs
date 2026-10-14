namespace ImmoPlus.Api.Services;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class CandidatureServices
{
    private readonly AppDbContext _context;
    private readonly LocationServices _locationServices;
    private readonly FinancementServices _financementServices;
    private readonly NotificationServices _notificationServices;

    public CandidatureServices(AppDbContext context, LocationServices locationServices, FinancementServices financementServices,
        NotificationServices notificationServices)
    {
        _context = context;
        _locationServices = locationServices;
        _financementServices = financementServices;
        _notificationServices = notificationServices;
    }

    public async Task<CandidatureResponseDto> CreerAsync(int locataireId, CandidatureDto dto)
    {
        var appartement = await _context.Appartements.Include(a => a.Bailleur).FirstOrDefaultAsync(a => a.Id == dto.AppartementId);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (appartement.TypeOffre != TypeOffre.LongueDuree)
        {
            throw new ConflictException("Cette annonce est un séjour court : réservez directement, pas besoin de candidature");
        }
        if (!appartement.Disponible || appartement.EstDesactive || appartement.Bailleur?.Statut == StatutUtilisateur.Desactive)
        {
            throw new ConflictException("Cet appartement n'est plus disponible");
        }
        if (!Enum.TryParse<SituationProfessionnelle>(dto.SituationProfessionnelle, true, out var situation))
        {
            throw new InvalidOperationException("SituationProfessionnelle invalide");
        }

        // Même règle que FinancementServices : sous l'avance totale, l'apport doit couvrir au moins 2 mois de loyer,
        // sinon le bailleur ne pourrait jamais accepter le dossier.
        var avanceExigee = appartement.LoyerMensuel!.Value * appartement.DureeAvanceExigee!.Value;
        var apportMinimum = appartement.LoyerMensuel.Value * 2;
        if (dto.ApportInitial < avanceExigee && dto.ApportInitial < apportMinimum)
        {
            throw new InvalidOperationException($"L'apport initial doit être d'au moins {apportMinimum.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} GNF (2 mois de loyer)");
        }

        var dejaCandidat = await _context.Candidatures.AnyAsync(c =>
            c.LocataireId == locataireId && c.AppartementId == dto.AppartementId
            && (c.Statut == StatutCandidature.EnAttente || c.Statut == StatutCandidature.ListeAttente
                || c.Statut == StatutCandidature.Preselectionnee));
        if (dejaCandidat)
        {
            throw new ConflictException("Vous avez déjà une candidature en attente pour cet appartement");
        }

        var candidature = new Candidature
        {
            LocataireId = locataireId,
            AppartementId = dto.AppartementId,
            ApportInitial = dto.ApportInitial,
            SituationProfessionnelle = situation,
            Statut = StatutCandidature.EnAttente,
            DateSoumission = DateTime.UtcNow
        };

        if (dto.Garant is not null)
        {
            if (!Enum.TryParse<SituationProfessionnelle>(dto.Garant.SituationProfessionnelle, true, out var situationGarant))
            {
                throw new InvalidOperationException("SituationProfessionnelle du garant invalide");
            }
            candidature.Garant = new Garant
            {
                Nom = dto.Garant.Nom,
                Telephone = dto.Garant.Telephone,
                Email = dto.Garant.Email,
                SituationProfessionnelle = situationGarant
            };
        }

        await _context.Candidatures.AddAsync(candidature);
        await _context.SaveChangesAsync();

        var locataire = await _context.Utilisateurs.FindAsync(locataireId);
        await _notificationServices.DossierDeposeAsync(locataire!, candidature, appartement);

        return await RechercherAsync(candidature.Id, locataireId, RoleUtilisateur.Locataire);
    }

    public async Task<PagedResultDto<CandidatureResponseDto>> ListerAsync(int utilisateurId, RoleUtilisateur role, int page, int taille)
    {
        if (page < 1) page = 1;
        if (taille < 1 || taille > 100) taille = 20;

        var requete = _context.Candidatures.AsNoTracking().AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(c => c.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(c => c.Appartement!.BailleurId == utilisateurId);
        }

        var totalElements = await requete.CountAsync();

        var candidatures = await requete
            .OrderByDescending(c => c.DateSoumission)
            .Skip((page - 1) * taille)
            .Take(taille)
            .Include(c => c.Locataire)
            .Include(c => c.Appartement)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .Include(c => c.DemandesComplement)
            .Include(c => c.Visites)
            .ToListAsync();

        var locationsParCandidature = await _context.Locations
            .Where(l => l.CandidatureId != null && candidatures.Select(c => c.Id).Contains(l.CandidatureId!.Value))
            .Select(l => new { l.CandidatureId, l.Id, FinancementId = l.FinancementLocation != null ? l.FinancementLocation.Id : (int?)null,
                l.DateApportPaye, l.DateRemiseCles })
            .ToListAsync();

        var donnees = candidatures.Select(c =>
        {
            var l = locationsParCandidature.FirstOrDefault(x => x.CandidatureId == c.Id);
            return VersDto(c, l?.Id, l?.FinancementId, masquerNotes: role == RoleUtilisateur.Locataire,
                dateApportPaye: l?.DateApportPaye, dateRemiseCles: l?.DateRemiseCles);
        }).ToList();

        return new PagedResultDto<CandidatureResponseDto>
        {
            Donnees = donnees,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = totalElements,
            TotalPages = (int)Math.Ceiling(totalElements / (double)taille)
        };
    }

    public async Task<CandidatureResponseDto> RechercherAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Locataire)
            .Include(c => c.Appartement)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .Include(c => c.DemandesComplement)
            .Include(c => c.Visites)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }

        VerifierAutorisation(candidature, utilisateurId, role);

        var location = await _context.Locations
            .Where(l => l.CandidatureId == id)
            .Select(l => new { l.Id, FinancementId = l.FinancementLocation != null ? l.FinancementLocation.Id : (int?)null,
                l.DateApportPaye, l.DateRemiseCles })
            .FirstOrDefaultAsync();

        return VersDto(candidature, location?.Id, location?.FinancementId, masquerNotes: role == RoleUtilisateur.Locataire,
            dateApportPaye: location?.DateApportPaye, dateRemiseCles: location?.DateRemiseCles);
    }

    public async Task<CandidatureResponseDto> AccepterAsync(int id, int utilisateurId, RoleUtilisateur role, AccepterCandidatureDto dto)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .Include(c => c.Locataire)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .Include(c => c.DemandesComplement)
            .Include(c => c.Visites)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (!EstDecidable(candidature.Statut))
        {
            throw new ConflictException("Cette candidature a déjà été traitée");
        }
        if (!candidature.Visites.Any(v => v.Statut == StatutVisite.Effectuee))
        {
            throw new ConflictException("Organisez d'abord une visite du logement avec ce candidat : la décision se prend après la visite");
        }
        var appartement = candidature.Appartement!;
        var montantTotal = appartement.LoyerMensuel!.Value * appartement.DureeAvanceExigee!.Value;

        // Transaction : si le financement échoue (ex. apport insuffisant), rien ne doit être persisté
        // (ni la Location, ni l'Appartement marqué indisponible, ni le statut de la candidature).
        Location location;
        FinancementLocation? financement = null;
        List<Candidature> autresCandidatures;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Vérifié dans la transaction (verrou d'écriture SQLite) : deux acceptations simultanées ne peuvent pas attribuer le logement deux fois.
            if (await _context.Locations.AnyAsync(l => l.AppartementId == candidature.AppartementId && l.Statut == StatutLocation.EnCours))
            {
                throw new ConflictException("Ce logement est déjà attribué à un autre candidat");
            }
            appartement.Disponible = false;
            location = await _locationServices.CreerDepuisCandidatureAsync(candidature);

            if (candidature.ApportInitial < montantTotal)
            {
                // Le taux de frais relève de l'admin : un bailleur qui accepte applique le taux par défaut.
                var tauxFrais = role == RoleUtilisateur.Admin ? dto.TauxFrais ?? 0.03m : 0.03m;
                financement = await _financementServices.CreerPourLocationAsync(location, appartement, candidature.ApportInitial, tauxFrais);
            }

            candidature.Statut = StatutCandidature.Acceptee;
            candidature.DateTraitement = DateTime.UtcNow;

            // Les autres candidatures en attente passent en liste d'attente : le bailleur pourra en choisir une
            // si l'attribution tombe (désistement). Celles déjà en liste d'attente y restent.
            autresCandidatures = await _context.Candidatures
                .Include(c => c.Locataire)
                .Include(c => c.Visites)
                .Where(c => c.AppartementId == appartement.Id && c.Id != id
                    && (c.Statut == StatutCandidature.EnAttente || c.Statut == StatutCandidature.Preselectionnee))
                .ToListAsync();
            foreach (var autre in autresCandidatures)
            {
                autre.Statut = StatutCandidature.ListeAttente;
                autre.DateTraitement = DateTime.UtcNow;
                AnnulerVisitesActives(autre);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        // Emails envoyés seulement une fois la transaction validée.
        await _notificationServices.DossierAccepteAsync(candidature.Locataire!, candidature, appartement, location, financement);
        // L'admin prend le relais : apport, remise des clés et financement.
        var admins = await _context.Utilisateurs
            .Where(u => u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide)
            .ToListAsync();
        foreach (var admin in admins)
        {
            await _notificationServices.DossierAttribueAdminAsync(admin, candidature, appartement, candidature.Locataire!, financement);
        }
        foreach (var autre in autresCandidatures)
        {
            await _notificationServices.DossierListeAttenteAsync(autre.Locataire!, autre, appartement);
        }

        return VersDto(candidature, location.Id, financement?.Id);
    }

    public async Task<CandidatureResponseDto> RefuserAsync(int id, int utilisateurId, RoleUtilisateur role, RefuserCandidatureDto dto)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .Include(c => c.Locataire)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .Include(c => c.DemandesComplement)
            .Include(c => c.Visites)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (!EstDecidable(candidature.Statut))
        {
            throw new ConflictException("Cette candidature a déjà été traitée");
        }

        AnnulerVisitesActives(candidature);
        candidature.Statut = StatutCandidature.Refusee;
        candidature.DateTraitement = DateTime.UtcNow;
        candidature.MotifRefus = dto.MotifRefus;

        await _context.SaveChangesAsync();
        await _notificationServices.DossierRefuseAsync(candidature.Locataire!, candidature, candidature.Appartement!);

        return VersDto(candidature, null, null);
    }

    /// <summary>Le bailleur (ou l'admin) annule l'attribution d'un dossier accepté, par exemple après un désistement.</summary>
    public async Task<CandidatureResponseDto> AnnulerAttributionAsync(int id, string? motif, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await ChargerPourChangementAsync(id);
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (candidature.Statut != StatutCandidature.Acceptee)
        {
            throw new ConflictException("Seul un dossier accepté peut voir son attribution annulée");
        }

        await LibererAttributionAsync(candidature,
            string.IsNullOrWhiteSpace(motif) ? "Attribution annulée par le bailleur" : $"Attribution annulée : {motif.Trim()}");
        await _notificationServices.AttributionAnnuleeAsync(candidature.Locataire!, candidature, candidature.Appartement!);
        return VersDto(candidature, null, null);
    }

    /// <summary>
    /// Le candidat retire sa candidature. S'il avait été retenu, l'attribution est annulée et le bailleur prévenu
    /// pour qu'il choisisse un remplaçant dans la liste d'attente.
    /// </summary>
    public async Task<CandidatureResponseDto> DesisterAsync(int id, int locataireId)
    {
        var candidature = await ChargerPourChangementAsync(id);
        if (candidature.LocataireId != locataireId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }

        switch (candidature.Statut)
        {
            case StatutCandidature.Acceptee:
                await LibererAttributionAsync(candidature, "Désistement du candidat");
                var enAttente = await _context.Candidatures.CountAsync(c =>
                    c.AppartementId == candidature.AppartementId && c.Statut == StatutCandidature.ListeAttente);
                var bailleur = await _context.Utilisateurs.FindAsync(candidature.Appartement!.BailleurId);
                await _notificationServices.DesistementAsync(bailleur!, candidature, candidature.Appartement, candidature.Locataire!, enAttente);
                break;
            case StatutCandidature.EnAttente:
            case StatutCandidature.ListeAttente:
            case StatutCandidature.Preselectionnee:
                AnnulerVisitesActives(candidature);
                candidature.Statut = StatutCandidature.Annulee;
                candidature.DateTraitement = DateTime.UtcNow;
                candidature.MotifRefus = "Candidature retirée par le candidat";
                await _context.SaveChangesAsync();
                break;
            default:
                throw new ConflictException("Cette candidature est déjà close");
        }
        return VersDto(candidature, null, null, masquerNotes: true);
    }

    /// <summary>Le bailleur clôt la liste d'attente d'un logement : les candidats restants sont refusés et prévenus.</summary>
    public async Task<int> CloreListeAttenteAsync(int appartementId, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await _context.Appartements.FindAsync(appartementId);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && appartement.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à ce logement");
        }

        var enAttente = await _context.Candidatures
            .Include(c => c.Locataire)
            .Where(c => c.AppartementId == appartementId && c.Statut == StatutCandidature.ListeAttente)
            .ToListAsync();
        foreach (var c in enAttente)
        {
            c.Statut = StatutCandidature.Refusee;
            c.DateTraitement = DateTime.UtcNow;
            c.MotifRefus = "Appartement attribué à un autre candidat";
        }
        await _context.SaveChangesAsync();

        foreach (var c in enAttente)
        {
            await _notificationServices.DossierRefuseAsync(c.Locataire!, c, appartement);
        }
        return enAttente.Count;
    }

    private async Task<Candidature> ChargerPourChangementAsync(int id) =>
        await _context.Candidatures
            .Include(c => c.Appartement)
            .Include(c => c.Locataire)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .Include(c => c.DemandesComplement)
            .Include(c => c.Visites)
            .FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new NotFoundException("Cette candidature n'existe pas");

    /// <summary>
    /// Défait une attribution : location résiliée, financement supprimé, logement de nouveau disponible.
    /// Refusé dès qu'un paiement a été enregistré, car il faudrait alors gérer un remboursement.
    /// </summary>
    private async Task LibererAttributionAsync(Candidature candidature, string motif)
    {
        var location = await _context.Locations
            .Include(l => l.FinancementLocation).ThenInclude(f => f!.Mensualites)
            .FirstOrDefaultAsync(l => l.CandidatureId == candidature.Id && l.Statut == StatutLocation.EnCours);

        if (location?.DateApportPaye is not null)
        {
            throw new ConflictException("L'apport a déjà été payé : l'attribution ne peut plus être annulée");
        }
        if (location?.FinancementLocation is { } financement)
        {
            var idsMensualites = financement.Mensualites.Select(m => m.Id).ToList();
            if (await _context.Paiements.AnyAsync(p => p.MensualiteId != null && idsMensualites.Contains(p.MensualiteId.Value)))
            {
                throw new ConflictException("Des paiements ont déjà été enregistrés sur ce dossier : l'attribution ne peut pas être annulée");
            }
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        if (location is not null)
        {
            location.Statut = StatutLocation.Resiliee;
            location.DateFin = DateTime.UtcNow.Date;
            if (location.FinancementLocation is { } financementASupprimer)
            {
                _context.Mensualites.RemoveRange(financementASupprimer.Mensualites);
                _context.FinancementLocations.Remove(financementASupprimer);
            }
        }
        candidature.Statut = StatutCandidature.Annulee;
        candidature.DateTraitement = DateTime.UtcNow;
        candidature.MotifRefus = motif;
        candidature.Appartement!.Disponible = true;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<CandidatureResponseDto> DemanderComplementAsync(int id, DemanderComplementDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .Include(c => c.Locataire)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .Include(c => c.DemandesComplement)
            .Include(c => c.Visites)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (candidature.Statut != StatutCandidature.EnAttente && candidature.Statut != StatutCandidature.Preselectionnee)
        {
            throw new ConflictException("Cette candidature a déjà été traitée");
        }

        var pieces = new List<(ProprietaireDocument Proprietaire, TypeDocument Type)>();
        foreach (var piece in dto.Pieces)
        {
            if (!Enum.TryParse<ProprietaireDocument>(piece.Proprietaire, true, out var proprietaire)
                || !Enum.TryParse<TypeDocument>(piece.TypeDocument, true, out var type))
            {
                throw new InvalidOperationException("Pièce demandée invalide");
            }
            if (proprietaire == ProprietaireDocument.Garant && candidature.Garant is null)
            {
                throw new InvalidOperationException("Ce dossier n'a pas de garant : impossible de demander ses pièces");
            }
            if (!pieces.Contains((proprietaire, type)))
            {
                pieces.Add((proprietaire, type));
            }
        }
        if (pieces.Count == 0)
        {
            throw new InvalidOperationException("Sélectionnez au moins une pièce à demander");
        }

        var aujourdhui = DateTime.UtcNow.Date;
        if (dto.DateLimite.Date <= aujourdhui || dto.DateLimite.Date > aujourdhui.AddDays(60))
        {
            throw new InvalidOperationException("La date limite doit être comprise entre demain et dans 60 jours");
        }

        var demande = new DemandeComplement
        {
            CandidatureId = id,
            Pieces = string.Join(";", pieces.Select(p => $"{p.Proprietaire}:{p.Type}")),
            Message = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message.Trim(),
            DateLimite = dto.DateLimite.Date,
            DateDemande = DateTime.UtcNow
        };
        candidature.DemandesComplement.Add(demande);
        await _context.SaveChangesAsync();

        await _notificationServices.DemandeComplementAsync(candidature.Locataire!, candidature, candidature.Appartement!, demande);
        return VersDto(candidature, null, null);
    }

    // --- Après l'attribution : apport puis remise des clés ------------------------------------------------

    private async Task<(Candidature Candidature, Location Location)> ChargerAttributionAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await ChargerPourChangementAsync(id);
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        var location = candidature.Statut == StatutCandidature.Acceptee
            ? await _context.Locations.Include(l => l.FinancementLocation)
                .FirstOrDefaultAsync(l => l.CandidatureId == id && l.Statut == StatutLocation.EnCours)
            : null;
        return (candidature, location ?? throw new ConflictException("Ce dossier n'a pas de location en cours"));
    }

    private CandidatureResponseDto VersDtoAttribution(Candidature c, Location l) =>
        VersDto(c, l.Id, l.FinancementLocation?.Id, dateApportPaye: l.DateApportPaye, dateRemiseCles: l.DateRemiseCles);

    /// <summary>Le bailleur enregistre la réception de l'apport prévu au dossier.</summary>
    public async Task<CandidatureResponseDto> EnregistrerApportAsync(int id, EnregistrerApportDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var (candidature, location) = await ChargerAttributionAsync(id, utilisateurId, role);
        if (location.DateApportPaye is not null)
        {
            throw new ConflictException("Le paiement de l'apport est déjà enregistré");
        }
        if (string.IsNullOrWhiteSpace(dto.ModePaiement))
        {
            throw new InvalidOperationException("Indiquez le mode de paiement");
        }
        // Date absente du JSON : DateTime par défaut (01/01/0001), qui passerait le contrôle « pas dans le futur ».
        if (dto.DatePaiement.Year < 2000)
        {
            throw new InvalidOperationException("Indiquez la date du paiement");
        }
        if (dto.DatePaiement.Date > DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("La date de paiement ne peut pas être dans le futur");
        }

        location.DateApportPaye = dto.DatePaiement.Date;
        location.ModePaiementApport = dto.ModePaiement.Trim();
        location.ReferenceApport = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim();
        await _context.SaveChangesAsync();

        await _notificationServices.ApportRecuAsync(candidature.Locataire!, candidature, candidature.Appartement!, location);
        return VersDtoAttribution(candidature, location);
    }

    /// <summary>Le bailleur remet les clés : possible seulement une fois l'apport payé. La location démarre ce jour-là.</summary>
    public async Task<CandidatureResponseDto> RemettreClesAsync(int id, RemiseClesDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var (candidature, location) = await ChargerAttributionAsync(id, utilisateurId, role);
        if (location.DateApportPaye is null)
        {
            throw new ConflictException("Les clés ne peuvent être remises qu'après le paiement de l'apport");
        }
        if (location.DateRemiseCles is not null)
        {
            throw new ConflictException("La remise des clés est déjà enregistrée");
        }
        if (dto.DateRemise.Date < location.DateApportPaye.Value.Date || dto.DateRemise.Date > DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("La date de remise doit être comprise entre le paiement de l'apport et aujourd'hui");
        }

        location.DateRemiseCles = dto.DateRemise.Date;
        location.DateDebut = dto.DateRemise.Date;
        await _context.SaveChangesAsync();

        await _notificationServices.ClesRemisesAsync(candidature.Locataire!, candidature, candidature.Appartement!, location);
        return VersDtoAttribution(candidature, location);
    }

    // --- Visites -------------------------------------------------------------------------------------------

    private const int PreselectionsMax = 3;

    /// <summary>Statuts sur lesquels le bailleur peut encore décider (accepter, refuser, organiser une visite).</summary>
    private static bool EstDecidable(StatutCandidature statut) =>
        statut is StatutCandidature.EnAttente or StatutCandidature.ListeAttente or StatutCandidature.Preselectionnee;

    private static void AnnulerVisitesActives(Candidature candidature)
    {
        foreach (var visite in candidature.Visites.Where(v => v.EstActive))
        {
            visite.Statut = StatutVisite.Annulee;
        }
    }

    /// <summary>
    /// Le bailleur retient le candidat pour une visite (3 présélectionnés maximum par logement), ou reprogramme
    /// la visite déjà prévue. Le candidat reçoit la date par email.
    /// </summary>
    public async Task<CandidatureResponseDto> ProgrammerVisiteAsync(int id, ProgrammerVisiteDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await ChargerPourChangementAsync(id);
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (!EstDecidable(candidature.Statut))
        {
            throw new ConflictException("Cette candidature a déjà été traitée");
        }
        if (await _context.Locations.AnyAsync(l => l.AppartementId == candidature.AppartementId && l.Statut == StatutLocation.EnCours))
        {
            throw new ConflictException("Ce logement est déjà attribué : une visite n'est possible qu'après un désistement");
        }
        if (dto.DateHeure <= DateTime.UtcNow || dto.DateHeure > DateTime.UtcNow.AddDays(60))
        {
            throw new InvalidOperationException("La visite doit avoir lieu dans le futur, dans les 60 prochains jours");
        }

        if (candidature.Statut != StatutCandidature.Preselectionnee)
        {
            var preselectionnes = await _context.Candidatures.CountAsync(c =>
                c.AppartementId == candidature.AppartementId && c.Statut == StatutCandidature.Preselectionnee);
            if (preselectionnes >= PreselectionsMax)
            {
                throw new ConflictException($"{PreselectionsMax} candidats sont déjà retenus pour une visite : refusez-en un ou annulez sa visite pour libérer une place");
            }
        }

        var visite = candidature.Visites.FirstOrDefault(v => v.EstActive);
        var reprogrammee = visite is not null;
        if (visite is null)
        {
            visite = new Visite { DateCreation = DateTime.UtcNow };
            candidature.Visites.Add(visite);
        }
        visite.DateHeure = dto.DateHeure;
        visite.Message = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message.Trim();
        visite.Statut = StatutVisite.Proposee;
        visite.ReponseLocataire = null;
        candidature.Statut = StatutCandidature.Preselectionnee;
        await _context.SaveChangesAsync();

        await _notificationServices.VisiteProgrammeeAsync(candidature.Locataire!, candidature, candidature.Appartement!, visite, reprogrammee);
        return VersDto(candidature, null, null);
    }

    /// <summary>Le candidat confirme sa présence ou signale qu'il n'est pas disponible (le bailleur est alors prévenu).</summary>
    public async Task<CandidatureResponseDto> RepondreVisiteAsync(int visiteId, RepondreVisiteDto dto, int locataireId)
    {
        var visite = await _context.Visites.FindAsync(visiteId) ?? throw new NotFoundException("Cette visite n'existe pas");
        var candidature = await ChargerPourChangementAsync(visite.CandidatureId);
        visite = candidature.Visites.First(v => v.Id == visiteId);
        if (candidature.LocataireId != locataireId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette visite");
        }
        if (!visite.EstActive || visite.DateHeure <= DateTime.UtcNow)
        {
            throw new ConflictException("Cette visite n'attend plus de réponse");
        }

        visite.Statut = dto.Disponible ? StatutVisite.Confirmee : StatutVisite.Indisponible;
        visite.ReponseLocataire = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message.Trim();
        await _context.SaveChangesAsync();

        if (!dto.Disponible)
        {
            var bailleur = await _context.Utilisateurs.FindAsync(candidature.Appartement!.BailleurId);
            await _notificationServices.VisiteIndisponibleAsync(bailleur!, candidature, candidature.Appartement, candidature.Locataire!, visite);
        }
        return VersDto(candidature, null, null, masquerNotes: true);
    }

    /// <summary>Après la visite, le bailleur indique si elle a eu lieu, avec ses notes personnelles.</summary>
    public async Task<CandidatureResponseDto> CloturerVisiteAsync(int visiteId, CloturerVisiteDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var visite = await _context.Visites.FindAsync(visiteId) ?? throw new NotFoundException("Cette visite n'existe pas");
        var candidature = await ChargerPourChangementAsync(visite.CandidatureId);
        visite = candidature.Visites.First(v => v.Id == visiteId);
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette visite");
        }
        if (visite.Statut is not (StatutVisite.Proposee or StatutVisite.Confirmee))
        {
            throw new ConflictException("Cette visite ne peut pas être clôturée");
        }
        if (visite.DateHeure.Date > DateTime.UtcNow.Date)
        {
            throw new ConflictException("La visite n'a pas encore eu lieu");
        }

        visite.Statut = dto.Effectuee ? StatutVisite.Effectuee : StatutVisite.Absent;
        visite.NotesBailleur = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        await _context.SaveChangesAsync();
        return VersDto(candidature, null, null);
    }

    /// <summary>Le bailleur annule la visite prévue : le dossier quitte la présélection et libère une place.</summary>
    public async Task<CandidatureResponseDto> AnnulerVisiteAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await ChargerPourChangementAsync(id);
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (candidature.Statut != StatutCandidature.Preselectionnee)
        {
            throw new ConflictException("Ce candidat n'est pas retenu pour une visite");
        }

        var avaitVisitePrevue = candidature.Visites.Any(v => v.EstActive);
        AnnulerVisitesActives(candidature);
        candidature.Statut = StatutCandidature.EnAttente;
        await _context.SaveChangesAsync();

        if (avaitVisitePrevue)
        {
            await _notificationServices.VisiteAnnuleeAsync(candidature.Locataire!, candidature, candidature.Appartement!);
        }
        return VersDto(candidature, null, null);
    }

    private static void VerifierAutorisation(Candidature candidature, int utilisateurId, RoleUtilisateur role)
    {
        var autorise = role == RoleUtilisateur.Admin
            || (role == RoleUtilisateur.Locataire && candidature.LocataireId == utilisateurId)
            || (role == RoleUtilisateur.Bailleur && candidature.Appartement!.BailleurId == utilisateurId);
        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
    }

    private static CandidatureResponseDto VersDto(Candidature c, int? locationId, int? financementId, bool masquerNotes = false,
        DateTime? dateApportPaye = null, DateTime? dateRemiseCles = null) => new()
    {
        Id = c.Id,
        LocataireId = c.LocataireId,
        LocataireNom = c.Locataire?.Nom ?? "",
        LocataireTelephone = c.Locataire?.Telephone ?? "",
        LocataireEmail = c.Locataire?.Email ?? "",
        AppartementId = c.AppartementId,
        AppartementAdresse = c.Appartement?.Adresse ?? "",
        ApportInitial = c.ApportInitial,
        SituationProfessionnelle = c.SituationProfessionnelle.ToString(),
        Statut = c.Statut.ToString(),
        DateSoumission = c.DateSoumission,
        DateTraitement = c.DateTraitement,
        MotifRefus = c.MotifRefus,
        Garant = c.Garant is null ? null : new GarantResponseDto
        {
            Id = c.Garant.Id,
            Nom = c.Garant.Nom,
            Telephone = c.Garant.Telephone,
            Email = c.Garant.Email,
            SituationProfessionnelle = c.Garant.SituationProfessionnelle.ToString()
        },
        Documents = c.Documents.Select(d => new DocumentResponseDto
        {
            Id = d.Id,
            CandidatureId = d.CandidatureId,
            Proprietaire = d.Proprietaire.ToString(),
            TypeDocument = d.TypeDocument.ToString(),
            NomFichierOriginal = d.NomFichierOriginal,
            TailleOctets = d.TailleOctets,
            DateUpload = d.DateUpload
        }).ToList(),
        LocationId = locationId,
        DateApportPaye = dateApportPaye,
        DateRemiseCles = dateRemiseCles,
        FinancementLocationId = financementId,
        Visites = c.Visites
            .OrderByDescending(v => v.DateCreation)
            .Select(v => new VisiteResponseDto
            {
                Id = v.Id,
                DateHeure = v.DateHeure,
                Message = v.Message,
                Statut = v.Statut.ToString(),
                ReponseLocataire = v.ReponseLocataire,
                NotesBailleur = masquerNotes ? null : v.NotesBailleur
            }).ToList(),
        DemandesComplement = c.DemandesComplement
            .OrderByDescending(d => d.DateDemande)
            .Select(d =>
            {
                var complete = d.EstComplete(c.Documents);
                return new DemandeComplementResponseDto
                {
                    Id = d.Id,
                    Pieces = d.ListePieces().Select(p => new PieceDemandeeResponseDto
                    {
                        Proprietaire = p.Proprietaire.ToString(),
                        TypeDocument = p.Type.ToString(),
                        Recue = d.EstRecue(p, c.Documents)
                    }).ToList(),
                    Message = d.Message,
                    DateLimite = d.DateLimite,
                    DateDemande = d.DateDemande,
                    Complete = complete,
                    Depassee = !complete && d.DateLimite.Date < DateTime.UtcNow.Date
                };
            }).ToList()
    };
}
