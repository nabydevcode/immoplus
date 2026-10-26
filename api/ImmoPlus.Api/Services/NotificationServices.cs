namespace ImmoPlus.Api.Services;

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Emails envoyés au locataire à chaque étape de son dossier de location longue durée.
/// Les emails sont mis dans la boîte d'envoi (MessagesSortants) et partent en arrière-plan, avec nouvelles tentatives :
/// l'action métier n'attend jamais le serveur SMTP, et un échec d'envoi ne la fait jamais échouer.
/// </summary>
public class NotificationServices
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly LiensFrontend _liens;
    private readonly SmsServices _sms;
    private readonly ILogger<NotificationServices> _logger;
    private readonly BoiteEnvoiServices _boite;

    public NotificationServices(LiensFrontend liens, SmsServices sms, ILogger<NotificationServices> logger,
        BoiteEnvoiServices boite)
    {
        _boite = boite;
        _liens = liens;
        _sms = sms;
        _logger = logger;
    }

    // --- SMS / WhatsApp courts, en plus de l'email (voir SmsServices) ---

    /// <summary>« ImmoPlus : … » suivi de l'adresse courte du site.</summary>
    private Task Sms(Utilisateur destinataire, string evenement, string texte) =>
        _sms.EnvoyerAsync(destinataire, evenement, $"ImmoPlus : {texte} {_sms.Site}");

    /// <summary>Montant sans espace insécable (caractère hors alphabet SMS) : « 1 500 000 GNF ».</summary>
    private static string MontantSms(decimal? montant) =>
        montant is null ? "-" : $"{montant.Value.ToString("N0", Fr).Replace('\u202F', ' ').Replace('\u00A0', ' ')} GNF";

    // --- Inscription : confirmation de l'adresse email, puis validation du compte par un administrateur ---

    /// <param name="cheminConfirmation">« /confirmer-email?jeton=… » (jeton en clair : seul son hash est en base).</param>
    public Task<bool> ConfirmationEmailAsync(Utilisateur utilisateur, string cheminConfirmation)
    {
        var lien = $"{_liens.Url}{cheminConfirmation}";
        return EnvoyerAsync(utilisateur, (ReferenceEmail?)null, "Confirmez votre adresse email",
            "Confirmez votre adresse email",
            $"""
            <p>Merci pour votre inscription sur ImmoPlus en tant que <strong>{H(LibelleRole(utilisateur.Role))}</strong>.</p>
            <p>Pour activer votre compte, confirmez d'abord que cette adresse ({H(utilisateur.Email)}) est bien la vôtre :</p>
            <p style="margin:20px 0"><a href="{lien}" style="display:inline-block;background:#d4a94a;color:#15161a;text-decoration:none;font-weight:bold;padding:12px 20px;border-radius:8px">Confirmer mon adresse email</a></p>
            <p style="font-size:13px;color:#6b7280">Si le bouton ne fonctionne pas, copiez ce lien dans votre navigateur :<br><span style="word-break:break-all">{lien}</span></p>
            <p>Ce lien est valable {DureeConfirmationHeures} heures. Si vous n'êtes pas à l'origine de cette inscription, ignorez cet email.</p>
            """,
            "Une fois votre adresse confirmée, un administrateur ImmoPlus validera votre compte. Vous recevrez un email dès qu'il sera validé, et pourrez alors vous connecter.",
            cheminConfirmation);
    }

    /// <summary>Durée de validité du lien de confirmation (affichée dans l'email).</summary>
    public const int DureeConfirmationHeures = 48;

    /// <summary>Adresse confirmée : chaque admin actif est prévenu qu'un compte attend sa validation.</summary>
    public async Task<bool> NouveauCompteAValiderAsync(Utilisateur admin, Utilisateur compte)
    {
        var role = LibelleRole(compte.Role);
        var envoye = await EnvoyerAsync(admin, (ReferenceEmail?)null, $"Nouveau compte à valider : {compte.Nom} ({role})",
            "Nouveau compte à valider",
            $"""
            <p>Un nouvel utilisateur a confirmé son adresse email et attend la validation de son compte.</p>
            {Tableau(("Nom", H(compte.Nom)), ("Rôle", H(role)), ("Email", H(compte.Email)), ("Téléphone", H(compte.Telephone)),
                ("Inscrit le", Date(compte.DateCreation)))}
            """,
            "Dans l'espace admin, rubrique « Utilisateurs », onglet « À valider » : vérifiez le compte puis validez-le ou refusez-le.",
            $"/admin/utilisateurs?q={Uri.EscapeDataString(compte.Email)}");
        await Sms(admin, "compte-a-valider", $"nouveau compte à valider : {compte.Nom} ({role}).");
        return envoye;
    }

    /// <summary>Compte validé par un administrateur : email (lien de connexion) et SMS.</summary>
    public async Task<bool> CompteValideAsync(Utilisateur utilisateur)
    {
        var envoye = await EnvoyerAsync(utilisateur, (ReferenceEmail?)null, "Votre compte ImmoPlus est validé",
            "Compte validé",
            $"""
            <p>Bonne nouvelle : votre compte ImmoPlus <strong>{H(LibelleRole(utilisateur.Role))}</strong> ({H(utilisateur.Email)}) a été validé par un administrateur.</p>
            """,
            "Vous pouvez dès maintenant vous connecter avec votre email et votre mot de passe.",
            "/login");
        await Sms(utilisateur, "compte-valide", "votre compte est validé, vous pouvez vous connecter.");
        return envoye;
    }

    public Task<bool> InscriptionRefuseeAsync(Utilisateur utilisateur) =>
        EnvoyerAsync(utilisateur, (ReferenceEmail?)null, "Votre inscription n'a pas été retenue",
            "Inscription non retenue",
            $"""
            <p>Après examen par un administrateur, votre inscription sur ImmoPlus ({H(utilisateur.Email)}) n'a pas été retenue.</p>
            <p>Vous ne pouvez pas vous connecter avec ce compte.</p>
            """,
            "Si vous pensez qu'il s'agit d'une erreur, contactez ImmoPlus en précisant l'adresse email utilisée à l'inscription.");

    private static string LibelleRole(RoleUtilisateur role) => role switch
    {
        RoleUtilisateur.Locataire => "Locataire",
        RoleUtilisateur.Bailleur => "Bailleur",
        _ => "Administrateur"
    };

    // 1. Dépôt du dossier
    public Task DossierDeposeAsync(Utilisateur locataire, Candidature candidature, Appartement appartement) =>
        EnvoyerAsync(locataire, candidature.Id, "Votre dossier de location a bien été reçu",
            "Dossier reçu",
            $"""
            <p>Nous avons bien reçu votre dossier de candidature pour le logement situé <strong>{H(appartement.Adresse)}</strong>.</p>
            {Tableau(("Dossier n°", Reference(candidature)), ("Apport initial", Montant(candidature.ApportInitial)), ("Déposé le", Date(candidature.DateSoumission)))}
            """,
            "Le bailleur va étudier votre dossier. Pensez à ajouter vos justificatifs (pièce d'identité, revenus, domicile) s'ils ne sont pas encore joints : vous serez informé par email de sa décision.");

    // 2. Dossier accepté
    public async Task DossierAccepteAsync(Utilisateur locataire, Candidature candidature, Appartement appartement, Location location, FinancementLocation? financement)
    {
        var details = financement is null
            ? Tableau(("Dossier n°", Reference(candidature)), ("Location n°", location.Id.ToString("D6")),
                ("Loyer mensuel", Montant(appartement.LoyerMensuel)), ("Apport initial", Montant(candidature.ApportInitial)))
            : Tableau(("Dossier n°", Reference(candidature)), ("Location n°", location.Id.ToString("D6")),
                ("Avance exigée", Montant(financement.MontantTotal)), ("Apport initial", Montant(financement.ApportInitial)),
                ("Montant financé", Montant(financement.MontantARembourser)),
                ("Remboursement", $"{financement.NombreMensualites} mensualités de {Montant(financement.MontantMensualite)}"));

        var prochaineEtape = $"Réglez votre apport de {Montant(candidature.ApportInitial)} auprès d'ImmoPlus. Dès sa réception, "
            + (financement is null
                ? "nous organiserons la remise des clés du logement."
                : "nous organiserons la remise des clés et votre échéancier de remboursement sera généré.");

        await EnvoyerAsync(locataire, candidature.Id, "Bonne nouvelle : votre dossier est accepté",
            "Dossier accepté 🎉",
            $"""
            <p>Votre dossier pour le logement situé <strong>{H(appartement.Adresse)}</strong> a été <strong>accepté</strong>.</p>
            {details}
            """,
            prochaineEtape);
        await Sms(locataire, "dossier-accepte",
            $"votre dossier n° {Reference(candidature)} est accepté ! Réglez votre apport de {MontantSms(candidature.ApportInitial)} pour la remise des clés.");
    }

    // 3. Dossier refusé (par le bailleur, ou automatiquement quand le logement est attribué à un autre candidat)
    public async Task DossierRefuseAsync(Utilisateur locataire, Candidature candidature, Appartement appartement)
    {
        await EnvoyerAsync(locataire, candidature.Id, "Réponse à votre dossier de location",
            "Dossier non retenu",
            $"""
            <p>Votre dossier pour le logement situé <strong>{H(appartement.Adresse)}</strong> n'a malheureusement pas été retenu.</p>
            {(string.IsNullOrWhiteSpace(candidature.MotifRefus) ? "" : $"<p><strong>Motif :</strong> {H(candidature.MotifRefus)}</p>")}
            """,
            "D'autres logements vous attendent : consultez les annonces disponibles et déposez un nouveau dossier.");
        await Sms(locataire, "dossier-refuse", $"votre dossier n° {Reference(candidature)} n'a pas été retenu. D'autres logements vous attendent.");
    }

    // 3 a. Le logement a été attribué à un autre candidat : dossier gardé en liste d'attente
    public Task DossierListeAttenteAsync(Utilisateur locataire, Candidature candidature, Appartement appartement) =>
        EnvoyerAsync(locataire, candidature.Id, "Votre dossier est sur liste d'attente",
            "Liste d'attente",
            $"""
            <p>Le logement situé <strong>{H(appartement.Adresse)}</strong> vient d'être attribué à un autre candidat.</p>
            <p>Votre dossier n° {Reference(candidature)} n'est pas refusé : le bailleur le garde <strong>en liste d'attente</strong>
            au cas où le candidat retenu se désisterait.</p>
            """,
            "Vous n'avez rien à faire : vous serez prévenu par email si le logement vous est attribué. Vous pouvez continuer à candidater sur d'autres logements, ou retirer cette candidature depuis « Mes candidatures ».");

    // 3 b. Le bailleur annule l'attribution d'un dossier accepté
    public Task AttributionAnnuleeAsync(Utilisateur locataire, Candidature candidature, Appartement appartement) =>
        EnvoyerAsync(locataire, candidature.Id, "Attribution du logement annulée",
            "Attribution annulée",
            $"""
            <p>L'attribution du logement situé <strong>{H(appartement.Adresse)}</strong> (dossier n° {Reference(candidature)}) a été annulée.</p>
            {(string.IsNullOrWhiteSpace(candidature.MotifRefus) ? "" : $"<p><strong>Motif :</strong> {H(candidature.MotifRefus)}</p>")}
            <p>La location et le financement associés sont clôturés ; aucun paiement n'avait été enregistré.</p>
            """,
            "Si vous pensez qu'il s'agit d'une erreur, contactez le bailleur. Sinon, d'autres logements vous attendent dans les annonces.");

    // 3 c. Le candidat retenu se désiste : le bailleur doit choisir un remplaçant
    public Task DesistementAsync(Utilisateur bailleur, Candidature candidature, Appartement appartement, Utilisateur locataire, int nombreEnAttente) =>
        EnvoyerAsync(bailleur, candidature.Id, $"Désistement de {locataire.Nom}",
            "Le candidat retenu se désiste",
            $"""
            <p><strong>{H(locataire.Nom)}</strong> s'est désisté pour le logement situé <strong>{H(appartement.Adresse)}</strong>
            (dossier n° {Reference(candidature)}). La location est annulée et le logement est de nouveau disponible.</p>
            """,
            nombreEnAttente > 0
                ? $"{nombreEnAttente} candidat(s) sont en liste d'attente : ouvrez « Candidatures reçues », onglet « Liste d'attente », pour choisir un remplaçant."
                : "Aucun candidat n'est en liste d'attente : le logement est de nouveau visible dans les annonces.");

    // 2 a'. Dossier accepté par un bailleur : l'admin prend le relais (apport, clés, financement)
    public Task DossierAttribueAdminAsync(Utilisateur admin, Candidature candidature, Appartement appartement, Utilisateur locataire,
        FinancementLocation? financement) =>
        EnvoyerAsync(admin, candidature.Id, $"Dossier accepté : {locataire.Nom} — à prendre en charge",
            "Nouveau dossier à suivre",
            $"""
            <p>Le dossier n° {Reference(candidature)} de <strong>{H(locataire.Nom)}</strong> a été accepté pour le logement situé
            <strong>{H(appartement.Adresse)}</strong>.</p>
            {Tableau(("Apport attendu", Montant(candidature.ApportInitial)),
                ("Financement", financement is null ? "Aucun" : $"{Montant(financement.MontantARembourser)} en {financement.NombreMensualites} mensualités"))}
            """,
            "Dans l'espace admin, onglet Candidatures : enregistrez le paiement de l'apport, puis la remise des clés et, s'il y a un financement, générez l'échéancier.");

    // 2 bis. ImmoPlus a reçu l'apport
    public async Task ApportRecuAsync(Utilisateur locataire, Candidature candidature, Appartement appartement, Location location)
    {
        await EnvoyerAsync(locataire, candidature.Id, "Apport reçu",
            "Paiement de l'apport enregistré",
            $"""
            <p>ImmoPlus confirme avoir reçu votre apport pour le logement situé <strong>{H(appartement.Adresse)}</strong>.</p>
            {Tableau(("Montant", Montant(candidature.ApportInitial)), ("Mode", H(location.ModePaiementApport ?? "-")),
                ("Référence", H(location.ReferenceApport ?? "-")), ("Date", Date(location.DateApportPaye)))}
            """,
            "Dernière étape : la remise des clés. Nous allons vous contacter pour convenir du rendez-vous.");
        await Sms(locataire, "paiement-recu", $"apport de {MontantSms(candidature.ApportInitial)} reçu (dossier n° {Reference(candidature)}). Merci !");
    }

    // 2 ter. Remise des clés : la location commence
    public Task ClesRemisesAsync(Utilisateur locataire, Candidature candidature, Appartement appartement, Location location) =>
        EnvoyerAsync(locataire, candidature.Id, "Bienvenue chez vous !",
            "Remise des clés 🔑",
            $"""
            <p>Les clés du logement situé <strong>{H(appartement.Adresse)}</strong> vous ont été remises le {Date(location.DateRemiseCles)}.</p>
            {Tableau(("Location n°", location.Id.ToString("D6")), ("Début de la location", Date(location.DateDebut)))}
            """,
            location.FinancementLocation is null
                ? "Votre installation est terminée. Bonne installation dans votre nouveau logement !"
                : "Votre échéancier de remboursement va être généré : vous recevrez un email avec les dates de chaque mensualité.");

    // 2 a. Le bailleur retient le candidat pour une visite (ou la reprogramme)
    public async Task VisiteProgrammeeAsync(Utilisateur locataire, Candidature candidature, Appartement appartement, Visite visite, bool reprogrammee)
    {
        await EnvoyerAsync(locataire, candidature.Id, reprogrammee ? $"Visite reprogrammée le {DateHeure(visite.DateHeure)}" : $"Vous êtes retenu pour une visite le {DateHeure(visite.DateHeure)}",
            reprogrammee ? "Nouvelle date de visite" : "Invitation à visiter le logement",
            $"""
            <p>{(reprogrammee ? "La visite du logement a été reprogrammée." : "Bonne nouvelle : le bailleur a retenu votre dossier et vous invite à visiter le logement.")}</p>
            {Tableau(("Logement", H(appartement.Adresse)), ("Date", DateHeure(visite.DateHeure)), ("Dossier n°", Reference(candidature)))}
            {(visite.Message is null ? "" : $"<p><strong>Message du bailleur :</strong> {H(visite.Message)}</p>")}
            """,
            "Confirmez votre présence (ou signalez que vous n'êtes pas disponible) dans « Mes candidatures ». Le bailleur prendra sa décision après les visites.");
        await Sms(locataire, "visite-programmee",
            $"visite {(reprogrammee ? "reprogrammée" : "programmée")} le {visite.DateHeure.ToString("dd/MM/yyyy 'à' HH'h'mm", Fr)} (dossier n° {Reference(candidature)}). Confirmez votre présence.");
    }

    // 2 b. Le candidat n'est pas disponible : le bailleur doit reprogrammer
    public Task VisiteIndisponibleAsync(Utilisateur bailleur, Candidature candidature, Appartement appartement, Utilisateur locataire, Visite visite) =>
        EnvoyerAsync(bailleur, candidature.Id, $"{locataire.Nom} n'est pas disponible pour la visite",
            "Visite à reprogrammer",
            $"""
            <p><strong>{H(locataire.Nom)}</strong> ne pourra pas venir à la visite du {DateHeure(visite.DateHeure)}
            (logement situé <strong>{H(appartement.Adresse)}</strong>, dossier n° {Reference(candidature)}).</p>
            {(visite.ReponseLocataire is null ? "" : $"<p><strong>Son message :</strong> {H(visite.ReponseLocataire)}</p>")}
            """,
            "Ouvrez « Candidatures reçues » pour proposer une nouvelle date, ou annuler la visite.");

    // 2 c. Le bailleur annule la visite prévue
    public Task VisiteAnnuleeAsync(Utilisateur locataire, Candidature candidature, Appartement appartement) =>
        EnvoyerAsync(locataire, candidature.Id, "Visite annulée",
            "Visite annulée",
            $"""
            <p>La visite prévue pour le logement situé <strong>{H(appartement.Adresse)}</strong> (dossier n° {Reference(candidature)}) a été annulée par le bailleur.</p>
            <p>Votre dossier reste à l'étude.</p>
            """,
            "Vous n'avez rien à faire : vous serez prévenu par email de la suite donnée à votre dossier.");

    // 3 bis. Le bailleur demande des pièces manquantes, avec une date limite
    public Task DemandeComplementAsync(Utilisateur locataire, Candidature candidature, Appartement appartement, DemandeComplement demande)
    {
        var liste = string.Concat(demande.ListePieces().Select(p => $"<li>{LibellePiece(p.Proprietaire, p.Type)}</li>"));
        return EnvoyerAsync(locataire, candidature.Id, $"Documents à fournir avant le {Date(demande.DateLimite)}",
            "Votre dossier est incomplet",
            $"""
            <p>Le bailleur étudie votre dossier n° {Reference(candidature)} pour le logement situé <strong>{H(appartement.Adresse)}</strong>
            et a besoin des documents suivants :</p>
            <ul style="margin:8px 0 12px;padding-left:20px">{liste}</ul>
            {(demande.Message is null ? "" : $"<p><strong>Message du bailleur :</strong> {H(demande.Message)}</p>")}
            <p style="color:#b45309"><strong>Date limite : {Date(demande.DateLimite)}</strong></p>
            """,
            "Connectez-vous à ImmoPlus, ouvrez « Mes candidatures », puis ajoutez ces documents depuis le détail de votre dossier. Sans ces pièces, votre dossier risque d'être refusé.");
    }

    // 3 ter. Toutes les pièces demandées sont arrivées : on prévient le bailleur
    public Task DossierCompleteAsync(Utilisateur bailleur, Candidature candidature, Appartement appartement, Utilisateur locataire) =>
        EnvoyerAsync(bailleur, candidature.Id, $"Dossier complété par {locataire.Nom}",
            "Documents reçus",
            $"""
            <p><strong>{H(locataire.Nom)}</strong> a ajouté tous les documents que vous aviez demandés pour son dossier n° {Reference(candidature)}
            (logement situé <strong>{H(appartement.Adresse)}</strong>).</p>
            """,
            "Ouvrez « Candidatures reçues » pour consulter les nouveaux documents et accepter ou refuser le dossier.");

    private static string LibellePiece(ProprietaireDocument proprietaire, TypeDocument type)
    {
        var libelle = type switch
        {
            TypeDocument.PieceIdentite => "Pièce d'identité",
            TypeDocument.JustificatifRevenu => "Justificatif de revenus",
            TypeDocument.JustificatifDomicile => "Justificatif de domicile",
            _ => "Autre document"
        };
        return proprietaire == ProprietaireDocument.Garant ? $"{libelle} du garant" : libelle;
    }

    // 4. Échéancier généré
    public Task EcheancierGenereAsync(Utilisateur locataire, FinancementLocation financement, IReadOnlyList<Mensualite> mensualites)
    {
        var lignes = new StringBuilder();
        foreach (var m in mensualites)
        {
            lignes.Append($"""<tr><td style="padding:6px 12px;border-bottom:1px solid #eee">n° {m.NumeroEcheance}</td><td style="padding:6px 12px;border-bottom:1px solid #eee">{Date(m.DateEcheance)}</td><td style="padding:6px 12px;border-bottom:1px solid #eee;text-align:right">{Montant(m.MontantDu)}</td></tr>""");
        }

        return EnvoyerAsync(locataire, financement.Location?.CandidatureId, "Votre échéancier de remboursement",
            "Échéancier disponible",
            $"""
            <p>L'échéancier de remboursement de votre financement n° {financement.Id} est prêt :
            <strong>{financement.NombreMensualites} mensualités de {Montant(financement.MontantMensualite)}</strong>{DerniereDifferente(financement, mensualites)}.</p>
            <table style="border-collapse:collapse;width:100%;font-size:14px;margin:12px 0">
              <tr style="color:#6b7280;text-align:left"><th style="padding:6px 12px">Échéance</th><th style="padding:6px 12px">Date</th><th style="padding:6px 12px;text-align:right">Montant</th></tr>
              {lignes}
            </table>
            """,
            $"Votre première mensualité de {Montant(mensualites.FirstOrDefault()?.MontantDu)} est attendue le {Date(mensualites.FirstOrDefault()?.DateEcheance)}.");
    }

    /// <summary>« (dernière : 343 336 GNF) » quand l'arrondi au franc ajuste la dernière mensualité.</summary>
    private static string DerniereDifferente(FinancementLocation financement, IReadOnlyList<Mensualite> mensualites) =>
        mensualites.Count > 1 && mensualites[^1].MontantDu != financement.MontantMensualite
            ? $" (dernière : {Montant(mensualites[^1].MontantDu)})"
            : "";

    // 5. Paiement d'une mensualité
    public async Task PaiementMensualiteAsync(Utilisateur locataire, Paiement paiement, Mensualite mensualite, Mensualite? prochaine)
    {
        var reste = mensualite.MontantDu - mensualite.MontantPaye;
        var prochaineEtape = reste > 0
            ? $"Il reste {Montant(reste)} à régler sur l'échéance n° {mensualite.NumeroEcheance}."
            : prochaine is not null
                ? $"Prochaine échéance : n° {prochaine.NumeroEcheance}, {Montant(prochaine.MontantDu - prochaine.MontantPaye)} à régler avant le {Date(prochaine.DateEcheance)}."
                : "Toutes vos échéances sont réglées.";

        await EnvoyerAsync(locataire, mensualite.FinancementLocation?.Location?.CandidatureId, $"Paiement reçu – échéance n° {mensualite.NumeroEcheance}",
            "Paiement enregistré",
            $"""
            <p>Nous avons bien enregistré votre paiement. Merci !</p>
            {Tableau(("Échéance", $"n° {mensualite.NumeroEcheance} du {Date(mensualite.DateEcheance)}"), ("Montant reçu", Montant(paiement.Montant)),
                ("Mode", H(paiement.ModePaiement)), ("Référence", H(paiement.Reference ?? "-")), ("Date", Date(paiement.DatePaiement)),
                ("Statut de l'échéance", reste > 0 ? $"Partiellement payée (reste {Montant(reste)})" : "Payée"))}
            """,
            prochaineEtape);
        await Sms(locataire, "paiement-recu",
            $"paiement de {MontantSms(paiement.Montant)} reçu pour l'échéance n° {mensualite.NumeroEcheance} (financement n° {mensualite.FinancementLocationId:D6}). Merci !");
    }

    // 8. Fin de location : préavis, annulation, clôture
    public Task PreavisEnregistreAsync(Utilisateur destinataire, Location location)
    {
        var parQui = location.PreavisDonnePar == "Bailleur" ? "le bailleur" : "le locataire";
        return EnvoyerAsync(destinataire, location.CandidatureId, $"Préavis de fin de location : fin le {Date(location.DateFinPrevue)}",
            "Préavis de fin de location",
            $"""
            <p>Un préavis de fin de location a été donné par <strong>{parQui}</strong> pour le logement situé
            <strong>{H(location.Appartement!.Adresse)}</strong> (locataire : {H(location.Locataire!.Nom)}).</p>
            {Tableau(("Location n°", location.Id.ToString("D6")), ("Préavis donné le", Date(location.DatePreavis)),
                ("Fin de la location", Date(location.DateFinPrevue)))}
            {(location.MotifPreavis is null ? "" : $"<p><strong>Motif :</strong> {H(location.MotifPreavis)}</p>")}
            """,
            $"La location se poursuit normalement pendant les 3 mois de préavis : les loyers restent dus jusqu'au {Date(location.DateFinPrevue)}. "
            + "Ce jour-là, ImmoPlus organisera la restitution des clés et clôturera la location.");
    }

    public Task PreavisAnnuleAsync(Utilisateur destinataire, Location location) =>
        EnvoyerAsync(destinataire, location.CandidatureId, "Préavis de fin de location annulé",
            "Préavis annulé",
            $"""
            <p>Le préavis de fin de location du logement situé <strong>{H(location.Appartement!.Adresse)}</strong>
            (location n° {location.Id:D6}) a été annulé.</p>
            """,
            "La location continue normalement, sans date de fin.");

    public Task LocationTermineeAsync(Utilisateur destinataire, Location location) =>
        EnvoyerAsync(destinataire, location.CandidatureId, "Fin de la location",
            "Location terminée",
            $"""
            <p>La location du logement situé <strong>{H(location.Appartement!.Adresse)}</strong> (location n° {location.Id:D6})
            s'est terminée le {Date(location.DateFin)}, au terme du préavis de 3 mois.</p>
            """,
            destinataire.Id == location.LocataireId
                ? "Merci d'avoir loué avec ImmoPlus. L'historique de vos paiements reste consultable dans « Mes candidatures »."
                : "Le logement est de nouveau disponible dans les annonces.");

    // 7. Paiement d'un loyer mensuel (après la période couverte par l'avance)
    public async Task LoyerPayeAsync(Utilisateur locataire, Paiement paiement, Loyer loyer, Appartement appartement)
    {
        var reste = loyer.MontantDu - loyer.MontantPaye;
        var mois = loyer.DateEcheance.ToString("MMMM yyyy", Fr);
        await EnvoyerAsync(locataire, loyer.Location?.CandidatureId, $"Loyer de {mois} : paiement reçu",
            "Loyer reçu",
            $"""
            <p>Nous avons bien reçu votre paiement pour le loyer de <strong>{mois}</strong> (logement situé {H(appartement.Adresse)}).</p>
            {Tableau(("Montant reçu", Montant(paiement.Montant)), ("Mode", H(paiement.ModePaiement)), ("Référence", H(paiement.Reference ?? "-")),
                ("Date", Date(paiement.DatePaiement)), ("Statut", reste > 0 ? $"Partiellement payé (reste {Montant(reste)})" : "Payé"))}
            """,
            reste > 0 ? $"Il reste {Montant(reste)} à régler pour ce mois." : "Prochain loyer : le même jour le mois prochain.");
        await Sms(locataire, "paiement-recu", $"loyer de {mois} : {MontantSms(paiement.Montant)} reçu (location n° {loyer.LocationId:D6}). Merci !");
    }

    // 6. Financement intégralement remboursé
    public Task FinancementSoldeAsync(Utilisateur locataire, FinancementLocation financement) =>
        EnvoyerAsync(locataire, financement.Location?.CandidatureId, "Votre financement est entièrement remboursé",
            "Financement soldé ✅",
            $"""
            <p>Félicitations ! Vous avez réglé l'intégralité des {financement.NombreMensualites} mensualités de votre financement n° {financement.Id}.</p>
            {Tableau(("Montant financé", Montant(financement.MontantARembourser)), ("Location n°", financement.LocationId.ToString("D6")))}
            """,
            "Votre financement est clôturé. Vous continuerez à régler chaque mois votre loyer, sans frais, dès la fin de la période couverte par votre avance.");

    // 8 bis. Compte : mot de passe changé depuis le profil (sans dossier de référence)
    public Task<bool> MotDePasseModifieAsync(Utilisateur utilisateur) =>
        EnvoyerAsync(utilisateur, (ReferenceEmail?)null, "Votre mot de passe a été modifié",
            "Mot de passe modifié",
            $"""
            <p>Le mot de passe de votre compte ImmoPlus ({H(utilisateur.Email)}) a été modifié le {DateHeure(DateTime.UtcNow)} (UTC).</p>
            """,
            "Si vous êtes à l'origine de ce changement, vous n'avez rien à faire. Sinon, utilisez « Mot de passe oublié » pour en choisir un nouveau et contactez ImmoPlus.");

    public async Task<bool> CompteDesactiveAsync(Utilisateur utilisateur, string? motif)
    {
        var envoye = await EnvoyerAsync(utilisateur, (ReferenceEmail?)null, "Votre compte a été désactivé",
            "Compte désactivé",
            $"""
            <p>Votre compte ImmoPlus ({H(utilisateur.Email)}) a été désactivé par un administrateur le {DateHeure(DateTime.UtcNow)} (UTC).</p>
            {(motif is null ? "" : $"<p><strong>Motif :</strong> {H(motif)}</p>")}
            <p>Vous ne pouvez plus vous connecter et vos annonces éventuelles ne sont plus visibles.</p>
            """,
            "Si vous pensez qu'il s'agit d'une erreur, contactez ImmoPlus en répondant à cet email.");
        await Sms(utilisateur, "compte-desactive", "votre compte a été désactivé par un administrateur. Contactez ImmoPlus pour plus d'informations.");
        return envoye;
    }

    public Task<bool> BienvenueAdminAsync(Utilisateur admin) =>
        EnvoyerAsync(admin, (ReferenceEmail?)null, "Votre compte administrateur ImmoPlus",
            "Bienvenue dans l'équipe ImmoPlus",
            $"""
            <p>Un administrateur vous a créé un compte <strong>administrateur</strong> ImmoPlus ({H(admin.Email)}).</p>
            <p>Il est actif dès maintenant : connectez-vous avec cet email et le mot de passe qui vous a été communiqué.</p>
            """,
            "Connectez-vous puis changez votre mot de passe depuis « Mon profil ».",
            "/login");

    public Task<bool> CompteReactiveAsync(Utilisateur utilisateur) =>
        EnvoyerAsync(utilisateur, (ReferenceEmail?)null, "Votre compte a été réactivé",
            "Compte réactivé",
            $"""
            <p>Bonne nouvelle : votre compte ImmoPlus ({H(utilisateur.Email)}) a été réactivé.</p>
            """,
            "Vous pouvez de nouveau vous connecter avec votre email et votre mot de passe habituels.",
            "/login");

    // 9. Rappels automatiques des échéances (mensualités du financement et loyers)
    /// <param name="echeance">Libellé de l'échéance, ex. « mensualité n° 3 » ou « loyer d'octobre 2026 ».</param>
    public async Task<bool> RappelEcheanceAsync(Utilisateur locataire, int? dossier, string echeance, DateTime dateEcheance, decimal resteDu, string adresse)
    {
        var envoye = await EnvoyerAsync(locataire, dossier, $"Rappel : {echeance} à régler le {Date(dateEcheance)}",
            "Échéance à venir",
            $"""
            <p>Votre {H(echeance)} (logement situé <strong>{H(adresse)}</strong>) arrive à échéance le <strong>{Date(dateEcheance)}</strong>.</p>
            {Tableau(("Échéance", H(echeance)), ("Date", Date(dateEcheance)), ("Montant à régler", Montant(resteDu)))}
            """,
            "Réglez ce montant auprès d'ImmoPlus avant la date d'échéance pour éviter tout retard. Si c'est déjà fait, ne tenez pas compte de ce message.");
        // Seulement si l'email est parti : sinon le rappel sera retenté au prochain passage, et le SMS ne doit pas être doublé.
        if (envoye)
        {
            await Sms(locataire, "rappel-echeance", $"rappel, {echeance} de {MontantSms(resteDu)} à régler le {Date(dateEcheance)}.");
        }
        return envoye;
    }

    public async Task<bool> RetardEcheanceAsync(Utilisateur locataire, int? dossier, string echeance, DateTime dateEcheance, int joursRetard, decimal resteDu, string adresse)
    {
        var envoye = await EnvoyerAsync(locataire, dossier, $"Retard de paiement : {echeance} ({joursRetard} jour{(joursRetard > 1 ? "s" : "")} de retard)",
            "Paiement en retard",
            $"""
            <p>Votre {H(echeance)} (logement situé <strong>{H(adresse)}</strong>) était due le <strong>{Date(dateEcheance)}</strong>
            et n'est pas encore entièrement réglée.</p>
            {Tableau(("Échéance", H(echeance)), ("Date d'échéance", Date(dateEcheance)),
                ("Retard", $"{joursRetard} jour{(joursRetard > 1 ? "s" : "")}"), ("Reste dû", Montant(resteDu)))}
            """,
            "Régularisez ce montant au plus vite auprès d'ImmoPlus. Un rappel vous sera envoyé chaque semaine tant que l'échéance n'est pas réglée. Si vous venez de payer, ne tenez pas compte de ce message.");
        if (envoye)
        {
            await Sms(locataire, "retard-echeance",
                $"{echeance} en retard de {joursRetard} jour{(joursRetard > 1 ? "s" : "")}, reste dû {MontantSms(resteDu)}. Régularisez au plus vite.");
        }
        return envoye;
    }

    public record LigneRetard(string Locataire, string Reference, string Echeance, DateTime DateEcheance, int JoursRetard, decimal ResteDu);

    public Task<bool> RecapitulatifRetardsAsync(Utilisateur admin, IReadOnlyList<LigneRetard> retards)
    {
        var lignes = new StringBuilder();
        foreach (var r in retards)
        {
            lignes.Append($"""<tr><td style="padding:6px 8px;border-bottom:1px solid #eee">{H(r.Locataire)}</td><td style="padding:6px 8px;border-bottom:1px solid #eee">{H(r.Reference)}</td><td style="padding:6px 8px;border-bottom:1px solid #eee">{H(r.Echeance)} du {Date(r.DateEcheance)}</td><td style="padding:6px 8px;border-bottom:1px solid #eee;text-align:right">{r.JoursRetard} j</td><td style="padding:6px 8px;border-bottom:1px solid #eee;text-align:right">{Montant(r.ResteDu)}</td></tr>""");
        }
        return EnvoyerAsync(admin, (ReferenceEmail?)null, $"Récapitulatif des retards : {retards.Count} échéance{(retards.Count > 1 ? "s" : "")} impayée{(retards.Count > 1 ? "s" : "")}",
            "Échéances en retard",
            $"""
            <p>Voici les échéances (mensualités et loyers) dépassées et pas encore entièrement réglées au {Date(DateTime.UtcNow)},
            pour un total de <strong>{Montant(retards.Sum(r => r.ResteDu))}</strong>.</p>
            <table style="border-collapse:collapse;width:100%;font-size:13px;margin:12px 0">
              <tr style="color:#6b7280;text-align:left"><th style="padding:6px 8px">Locataire</th><th style="padding:6px 8px">Référence</th><th style="padding:6px 8px">Échéance</th><th style="padding:6px 8px;text-align:right">Retard</th><th style="padding:6px 8px;text-align:right">Reste dû</th></tr>
              {lignes}
            </table>
            """,
            "Les locataires concernés reçoivent une alerte de retard chaque semaine. Enregistrez leurs paiements dans l'espace admin dès réception.");
    }

    // 10. Réservations (séjours courts)
    private static ReferenceEmail RefReservation(Reservation reservation) => new("Réservation", "de la réservation", reservation.Id);

    /// <summary>
    /// Adresse affichée dans un email de séjour : au voyageur, seulement « Quartier, Ville » tant que le séjour n'est pas confirmé
    /// (la rue est donnée par la confirmation de paiement et l'email de la veille) ; l'hôte voit toujours l'adresse exacte.
    /// </summary>
    private static string Logement(Reservation r, Appartement appartement, bool pourVoyageur) =>
        pourVoyageur && appartement.TypeOffre == TypeOffre.SejourCourt
            && r.Statut is not (StatutReservation.Confirmee or StatutReservation.Terminee)
            ? AppartementServices.AdresseApproximative(appartement)
            : appartement.Adresse;

    /// <summary>« mercredi 14/10/2026 à 09h30 (UTC) » : fin du délai de paiement de 24 h.</summary>
    private static string EcheancePaiement(Reservation r) =>
        $"{DateHeure(ReservationServices.PayerAvant(r, 0) ?? (r.DateReponseHote ?? r.DateCreation) + ReservationServices.DelaiPaiement)} (UTC)";

    private static string RecapSejour(Reservation r, string logement, params (string, string)[] autres)
    {
        var nuits = (r.DateDepart.Date - r.DateArrivee.Date).Days;
        var lignes = new List<(string, string)>
        {
            ("Réservation n°", r.Id.ToString("D6")), ("Logement", H(logement)),
            ("Arrivée", Date(r.DateArrivee)), ("Départ", Date(r.DateDepart)), ("Durée", $"{nuits} nuit{(nuits > 1 ? "s" : "")}"),
            ("Voyageurs", r.NombreVoyageurs.ToString())
        };
        if (r.PrixParNuit > 0)
        {
            lignes.Add(("Hébergement", $"{nuits} × {Montant(r.PrixParNuit)}"));
        }
        if (r.ReductionMontant > 0)
        {
            lignes.Add(("Réduction", $"− {Montant(r.ReductionMontant)}"));
        }
        if (r.FraisMenage > 0)
        {
            lignes.Add(("Frais de ménage", Montant(r.FraisMenage)));
        }
        lignes.Add(("Montant total", Montant(r.MontantTotal)));
        if (r.Caution > 0)
        {
            lignes.Add(("Caution (remboursable, à part)", Montant(r.Caution)));
        }
        return Tableau(lignes.Concat(autres).ToArray());
    }

    public Task<bool> DemandeReservationAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement) =>
        EnvoyerAsync(voyageur, RefReservation(reservation), "Votre demande de réservation est envoyée à l'hôte",
            "Demande de réservation envoyée",
            $"""
            <p>Votre demande de séjour pour le logement situé <strong>{H(Logement(reservation, appartement, true))}</strong> a été transmise à l'hôte.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, true), ("Statut", "En attente de l'hôte"))}
            """,
            "L'hôte a 48 h pour accepter votre demande. Vous recevrez un email dès sa réponse : il vous suffira alors de payer pour confirmer. "
            + "Ces dates vous sont réservées pendant ce délai.");

    public async Task<bool> DemandeReservationBailleurAsync(Utilisateur bailleur, Reservation reservation, Appartement appartement, Utilisateur voyageur)
    {
        var envoye = await EnvoyerAsync(bailleur, RefReservation(reservation), $"Demande de réservation de {voyageur.Nom}",
            "Nouvelle demande de réservation",
            $"""
            <p><strong>{H(voyageur.Nom)}</strong> souhaite séjourner dans votre logement situé <strong>{H(Logement(reservation, appartement, false))}</strong>.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, false), ("Statut", "À accepter ou refuser"))}
            """,
            "Acceptez ou refusez cette demande dans votre espace, rubrique « Réservations reçues », sous 48 h : "
            + "sans réponse de votre part, elle sera refusée automatiquement.");
        await Sms(bailleur, "demande-reservation",
            $"nouvelle demande de réservation n° {reservation.Id:D6} du {Date(reservation.DateArrivee)} au {Date(reservation.DateDepart)}. Répondez sous 48 h.");
        return envoye;
    }

    public async Task<bool> DemandeAccepteeAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement)
    {
        var envoye = await EnvoyerAsync(voyageur, RefReservation(reservation), "L'hôte a accepté votre demande : payez pour confirmer",
            "Demande acceptée ✅",
            $"""
            <p>Bonne nouvelle : l'hôte a accepté votre demande de séjour au logement situé <strong>{H(Logement(reservation, appartement, true))}</strong>.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, true), ("Statut", "En attente de paiement"))}
            """,
            $"Réglez le montant de {Montant(reservation.MontantTotal)} avant le {EcheancePaiement(reservation)} : votre réservation sera confirmée dès réception du paiement complet. "
            + "Sans paiement d'ici là, elle sera annulée automatiquement et les dates libérées.");
        await Sms(voyageur, "demande-acceptee",
            $"l'hôte accepte votre demande n° {reservation.Id:D6}. Payez {MontantSms(reservation.MontantTotal)} avant le {EcheancePaiement(reservation)} pour confirmer.");
        return envoye;
    }

    public async Task<bool> DemandeRefuseeAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement)
    {
        var envoye = await EnvoyerAsync(voyageur, RefReservation(reservation), "Votre demande de réservation n'a pas été acceptée",
            "Demande non acceptée",
            $"""
            <p>Votre demande de séjour au logement situé <strong>{H(Logement(reservation, appartement, true))}</strong>
            du {Date(reservation.DateArrivee)} au {Date(reservation.DateDepart)} n'a pas été acceptée.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, true), ("Motif", H(reservation.MotifRefus ?? "Non précisé")))}
            """,
            "Aucun paiement n'a été demandé : vous n'avez rien à faire. D'autres logements vous attendent dans les annonces.");
        await Sms(voyageur, "reservation-refusee",
            $"votre demande de réservation n° {reservation.Id:D6} du {Date(reservation.DateArrivee)} n'a pas été acceptée.");
        return envoye;
    }

    public Task<bool> ReservationCreeeAsync(Utilisateur locataire, Reservation reservation, Appartement appartement) =>
        EnvoyerAsync(locataire, RefReservation(reservation), "Votre réservation est enregistrée",
            "Réservation enregistrée",
            $"""
            <p>Votre demande de séjour pour le logement situé <strong>{H(Logement(reservation, appartement, true))}</strong> est bien enregistrée.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, true), ("Statut", "En attente de paiement"))}
            """,
            $"Réglez le montant de {Montant(reservation.MontantTotal)} avant le {EcheancePaiement(reservation)} : votre réservation sera confirmée dès réception du paiement complet. "
            + "Sans paiement d'ici là, elle sera annulée automatiquement et les dates libérées.");

    public async Task<bool> ReservationCreeeBailleurAsync(Utilisateur bailleur, Reservation reservation, Appartement appartement, Utilisateur locataire)
    {
        var envoye = await EnvoyerAsync(bailleur, RefReservation(reservation), $"Nouvelle réservation de {locataire.Nom}",
            "Nouvelle réservation",
            $"""
            <p><strong>{H(locataire.Nom)}</strong> a réservé votre logement situé <strong>{H(Logement(reservation, appartement, false))}</strong>.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, false), ("Statut", "En attente de paiement"))}
            """,
            $"La réservation sera confirmée dès que le paiement complet sera enregistré ; sans paiement avant le {EcheancePaiement(reservation)}, elle sera annulée automatiquement. "
            + "Vous la retrouvez dans votre espace, rubrique « Réservations ».");
        await Sms(bailleur, "nouvelle-reservation",
            $"nouvelle réservation n° {reservation.Id:D6} du {Date(reservation.DateArrivee)} au {Date(reservation.DateDepart)}, en attente de paiement.");
        return envoye;
    }

    public async Task<bool> ReservationPaiementAsync(Utilisateur locataire, Reservation reservation, Appartement appartement, Paiement paiement, decimal totalPaye)
    {
        var reste = Math.Max(0, reservation.MontantTotal - totalPaye);
        var confirmee = reste == 0;
        var envoye = await EnvoyerAsync(locataire, RefReservation(reservation),
            confirmee ? "Paiement reçu : réservation confirmée" : "Paiement reçu pour votre réservation",
            confirmee ? "Réservation confirmée ✅" : "Paiement enregistré",
            $"""
            <p>Nous avons bien reçu votre paiement pour le séjour au logement situé <strong>{H(Logement(reservation, appartement, true))}</strong>. Merci !</p>
            {RecapSejour(reservation, Logement(reservation, appartement, true), ("Montant reçu", Montant(paiement.Montant)), ("Mode", H(paiement.ModePaiement)),
                ("Référence du paiement", H(paiement.Reference ?? "-")), ("Total réglé", Montant(totalPaye)), ("Reste dû", Montant(reste)),
                ("Statut", confirmee ? "Confirmée" : "En attente du solde"))}
            """,
            confirmee
                ? $"Votre réservation est entièrement payée et confirmée. Rendez-vous le {Date(reservation.DateArrivee)} : bon séjour !"
                : $"Il reste {Montant(reste)} à régler pour confirmer votre réservation.");
        await (confirmee
            ? Sms(locataire, "reservation-confirmee",
                $"réservation n° {reservation.Id:D6} confirmée ! Arrivée le {Date(reservation.DateArrivee)}. Adresse et instructions sur le site.")
            : Sms(locataire, "paiement-recu",
                $"{MontantSms(paiement.Montant)} reçu pour la réservation n° {reservation.Id:D6}. Reste à payer : {MontantSms(reste)}."));
        return envoye;
    }

    /// <param name="annuleePar">« vous-même », « le voyageur (…) », « l'hôte » ou « l'administrateur » : qui a annulé, vu du destinataire.</param>
    /// <param name="montantARembourser">Remboursement dû au voyageur selon la politique d'annulation (hors caution).</param>
    public async Task<bool> ReservationAnnuleeAsync(Utilisateur destinataire, Reservation reservation, Appartement appartement, string annuleePar,
        decimal totalPaye, decimal montantARembourser, bool pourBailleur)
    {
        var lignes = new List<(string, string)> { ("Déjà réglé", Montant(totalPaye)), ("À rembourser au voyageur", Montant(montantARembourser)) };
        if (!string.IsNullOrWhiteSpace(reservation.MotifAnnulation))
        {
            lignes.Add(("Motif", H(reservation.MotifAnnulation)));
        }
        var envoye = await EnvoyerAsync(destinataire, RefReservation(reservation), "Réservation annulée",
            "Réservation annulée",
            $"""
            <p>La réservation du logement situé <strong>{H(Logement(reservation, appartement, !pourBailleur))}</strong> du {Date(reservation.DateArrivee)} au {Date(reservation.DateDepart)}
            a été annulée par {H(annuleePar)}.</p>
            {RecapSejour(reservation, Logement(reservation, appartement, !pourBailleur), lignes.ToArray())}
            """,
            pourBailleur
                ? "Ces dates sont de nouveau disponibles à la réservation."
                : montantARembourser > 0
                    ? $"ImmoPlus va vous rembourser {Montant(montantARembourser)} ; vous recevrez un email dès que le remboursement sera effectué."
                    : totalPaye > 0
                        ? "Selon la politique d'annulation du logement, aucun remboursement n'est dû pour cette annulation."
                        : "Aucun paiement n'avait été enregistré : vous n'avez rien à faire. D'autres logements vous attendent dans les annonces.",
            pourBailleur ? CheminReservationsHote : CheminReservationsVoyageur);
        await Sms(destinataire, "reservation-annulee",
            $"la réservation n° {reservation.Id:D6} du {Date(reservation.DateArrivee)} est annulée."
            + (!pourBailleur && montantARembourser > 0 ? $" Remboursement prévu : {MontantSms(montantARembourser)}." : ""));
        return envoye;
    }

    public Task<bool> RemboursementEffectueAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement,
        decimal montant, string mode, string? referencePaiement, DateTime date) =>
        EnvoyerAsync(voyageur, RefReservation(reservation), "Votre remboursement a été effectué",
            "Remboursement effectué",
            $"""
            <p>ImmoPlus vous a remboursé suite à l'annulation de votre séjour au logement situé <strong>{H(Logement(reservation, appartement, true))}</strong>.</p>
            {Tableau(("Réservation n°", reservation.Id.ToString("D6")), ("Montant remboursé", Montant(montant)), ("Mode", H(mode)),
                ("Référence", H(referencePaiement ?? "-")), ("Date", Date(date)),
                ("Total remboursé", Montant(reservation.MontantRembourse)), ("Total dû", Montant(reservation.MontantARembourser)))}
            """,
            (reservation.MontantRembourse ?? 0) >= (reservation.MontantARembourser ?? 0)
                ? "Votre remboursement est complet : vous n'avez rien d'autre à faire."
                : $"Il reste {Montant((reservation.MontantARembourser ?? 0) - (reservation.MontantRembourse ?? 0))} à vous rembourser.",
            CheminReservationsVoyageur);

    // 11. Fin de séjour, avis et messagerie
    public Task<bool> SejourTermineAsync(Utilisateur destinataire, Reservation reservation, Appartement appartement, bool pourHote) =>
        EnvoyerAsync(destinataire, RefReservation(reservation), "Séjour terminé — laissez un avis",
            "Séjour terminé — laissez un avis ⭐",
            pourHote
                ? $"""
                  <p>Le séjour de <strong>{H(reservation.Locataire?.Nom ?? "votre voyageur")}</strong> dans votre logement situé <strong>{H(Logement(reservation, appartement, !pourHote))}</strong> est terminé.</p>
                  {RecapSejour(reservation, Logement(reservation, appartement, !pourHote))}
                  """
                : $"""
                  <p>Votre séjour au logement situé <strong>{H(Logement(reservation, appartement, !pourHote))}</strong> est terminé. Nous espérons qu'il s'est bien passé !</p>
                  {RecapSejour(reservation, Logement(reservation, appartement, !pourHote))}
                  """,
            pourHote
                ? $"Évaluez votre voyageur dans votre espace, rubrique « Réservations reçues », d'ici le {Date(reservation.DateDepart.Date.AddDays(14))}."
                : $"Laissez un avis sur le logement dans « Mes réservations » d'ici le {Date(reservation.DateDepart.Date.AddDays(14))} : il aidera les prochains voyageurs.",
            pourHote ? CheminReservationsHote : CheminReservationsVoyageur);

    public Task<bool> AvisRecuAsync(Utilisateur cible, Avis avis, Reservation reservation, Appartement appartement, string auteur, bool pourHote) =>
        EnvoyerAsync(cible, RefReservation(reservation), pourHote ? "Nouvel avis sur votre logement" : "Votre hôte vous a évalué",
            "Nouvel avis reçu",
            $"""
            <p><strong>{H(auteur)}</strong> a laissé un avis {(pourHote ? "sur votre logement" : "sur votre séjour au logement")} situé <strong>{H(Logement(reservation, appartement, !pourHote))}</strong>.</p>
            {Tableau(("Réservation n°", reservation.Id.ToString("D6")), ("Note", $"{avis.Note} / 5"), ("Commentaire", H(avis.Commentaire)))}
            """,
            pourHote
                ? "Vous pouvez répondre publiquement à cet avis depuis votre espace, rubrique « Réservations reçues »."
                : "Cet avis est visible des hôtes auprès de qui vous réserverez.",
            pourHote ? CheminReservationsHote : CheminReservationsVoyageur);

    /// <summary>Avis laissé sur une location longue durée : au bailleur (avis sur son logement) ou au locataire (avis de son bailleur).</summary>
    public Task<bool> AvisLocationRecuAsync(Utilisateur cible, Avis avis, Location location, Appartement appartement, string auteur, bool pourBailleur) =>
        EnvoyerAsync(cible, new ReferenceEmail("Location", "de la location", location.Id),
            pourBailleur ? "Nouvel avis sur votre logement" : "Votre bailleur vous a évalué",
            "Nouvel avis reçu",
            $"""
            <p><strong>{H(auteur)}</strong> a laissé un avis {(pourBailleur ? "sur votre logement" : "sur votre location du logement")} situé <strong>{H(appartement.Adresse)}</strong>.</p>
            {Tableau(("Location n°", location.Id.ToString("D6")), ("Note", $"{avis.Note} / 5"), ("Commentaire", H(avis.Commentaire)))}
            """,
            pourBailleur
                ? "Cet avis est visible sur la fiche de votre logement : vous pouvez y répondre publiquement depuis votre espace."
                : "Cet avis est visible des bailleurs et des hôtes auprès de qui vous candidaterez ou réserverez.",
            pourBailleur ? "/bailleur/candidatures" : "/mes-candidatures");

    // 13. Vérification d'identité et signalements
    public async Task<bool> IdentiteValideeAsync(Utilisateur utilisateur, PieceIdentite piece)
    {
        var envoye = await EnvoyerAsync(utilisateur, new ReferenceEmail("Vérification", "de la demande", piece.Id), "Votre identité est vérifiée",
            "Identité vérifiée ✅",
            $"""
            <p>Votre pièce d'identité a été contrôlée par un administrateur : votre identité est désormais <strong>vérifiée</strong>.</p>
            <p>Le badge « Identité vérifiée » apparaît sur votre profil public.</p>
            """,
            "Vous n'avez rien à faire : le badge renforce la confiance des voyageurs, locataires et bailleurs.",
            "/profil");
        await Sms(utilisateur, "identite-verifiee", "votre identité est vérifiée, le badge apparaît sur votre profil.");
        return envoye;
    }

    public async Task<bool> IdentiteRefuseeAsync(Utilisateur utilisateur, PieceIdentite piece)
    {
        var envoye = await EnvoyerAsync(utilisateur, new ReferenceEmail("Vérification", "de la demande", piece.Id), "Vérification d'identité refusée",
            "Vérification d'identité refusée",
            $"""
            <p>Votre demande de vérification d'identité n'a pas pu être acceptée.</p>
            {(string.IsNullOrWhiteSpace(piece.MotifRefus) ? "" : $"<p><strong>Motif :</strong> {H(piece.MotifRefus)}</p>")}
            """,
            "Vous pouvez envoyer une nouvelle demande depuis « Mon profil », rubrique « Vérifier mon identité » (document lisible, en cours de validité).",
            "/profil");
        await Sms(utilisateur, "identite-refusee", "votre vérification d'identité est refusée. Consultez le motif dans votre profil.");
        return envoye;
    }

    public async Task<bool> SignalementRecuAsync(Utilisateur admin, Signalement signalement, string resumeCible, string auteur)
    {
        var envoye = await EnvoyerAsync(admin, new ReferenceEmail("Signalement", "du signalement", signalement.Id), "Nouveau signalement à traiter",
            "Nouveau signalement",
            $"""
            <p>Un signalement vient d'être envoyé par <strong>{H(auteur)}</strong>.</p>
            {Tableau(("Cible", H($"{signalement.Cible} n° {signalement.CibleId}")), ("Concerne", H(resumeCible)),
                ("Motif", H(signalement.Motif.ToString())), ("Texte", string.IsNullOrWhiteSpace(signalement.Texte) ? "-" : H(signalement.Texte)))}
            """,
            "Dans l'espace admin, rubrique « Signalements » : examinez la cible, puis traitez ou rejetez le signalement.",
            "/admin/signalements");
        await Sms(admin, "signalement-recu", $"nouveau signalement n° {signalement.Id:D6} ({signalement.Cible}) à traiter.");
        return envoye;
    }

    public Task<bool> NouveauMessageAsync(Utilisateur destinataire, Reservation reservation, Appartement appartement, string auteur, string contenu, bool pourHote)
    {
        var extrait = contenu.Length > 200 ? contenu[..200] + "…" : contenu;
        return EnvoyerAsync(destinataire, RefReservation(reservation), $"Nouveau message de {auteur}",
            "Nouveau message",
            $"""
            <p><strong>{H(auteur)}</strong> vous a écrit à propos du séjour au logement situé <strong>{H(Logement(reservation, appartement, !pourHote))}</strong>
            du {Date(reservation.DateArrivee)} au {Date(reservation.DateDepart)} :</p>
            <blockquote style="border-left:3px solid #e8dcc0;margin:12px 0;padding:8px 12px;color:#374151;white-space:pre-line">{H(extrait)}</blockquote>
            """,
            pourHote
                ? "Répondez depuis votre espace, rubrique « Réservations reçues », bouton « Messages »."
                : "Répondez depuis « Mes réservations », bouton « Messages ».",
            pourHote ? CheminReservationsHote : CheminReservationsVoyageur);
    }

    // 12. Paiement mobile et arrivée
    public Task<bool> PaiementMobileEchoueAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement, string operateur,
        decimal montant, string motif) =>
        EnvoyerAsync(voyageur, RefReservation(reservation), $"Paiement {operateur} non abouti",
            "Paiement non abouti",
            $"""
            <p>Votre paiement {H(operateur)} de <strong>{Montant(montant)}</strong> pour le séjour au logement situé <strong>{H(Logement(reservation, appartement, true))}</strong>
            n'a pas abouti. Aucun montant n'a été débité.</p>
            {Tableau(("Réservation n°", reservation.Id.ToString("D6")), ("Montant", Montant(montant)), ("Motif", H(motif)))}
            """,
            "Vous pouvez relancer le paiement depuis « Mes réservations » (vérifiez votre solde et votre numéro), ou choisir un autre opérateur.",
            CheminReservationsVoyageur);

    /// <summary>Paiement mobile validé par l'opérateur alors que la réservation venait d'être annulée : il est intégralement remboursé.</summary>
    public Task<bool> PaiementRecuApresAnnulationAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement, string operateur,
        decimal montant) =>
        EnvoyerAsync(voyageur, RefReservation(reservation), "Paiement reçu après l'annulation : remboursement prévu",
            "Paiement à rembourser",
            $"""
            <p>Votre paiement {H(operateur)} de <strong>{Montant(montant)}</strong> pour le séjour au logement situé <strong>{H(Logement(reservation, appartement, true))}</strong>
            a été validé par l'opérateur alors que la réservation était déjà annulée. Ce montant vous sera intégralement remboursé.</p>
            {Tableau(("Réservation n°", reservation.Id.ToString("D6")), ("Montant reçu", Montant(montant)),
                ("Total à rembourser", Montant(reservation.MontantARembourser)))}
            """,
            "ImmoPlus effectue le remboursement et vous prévient par email dès qu'il est versé.",
            CheminReservationsVoyageur);

    /// <param name="adresse">Adresse complète (le séjour est confirmé : on peut la communiquer).</param>
    public async Task<bool> ArriveeDemainAsync(Utilisateur voyageur, Reservation reservation, Appartement appartement, string adresse,
        string? heureArrivee, string? heureDepart, string? telephoneHote)
    {
        var instructions = string.IsNullOrWhiteSpace(appartement.InstructionsArrivee)
            ? "<p>L'hôte n'a pas laissé d'instructions particulières : contactez-le par la messagerie de votre réservation si besoin.</p>"
            : $"""
              <p><strong>Instructions de l'hôte</strong></p>
              <blockquote style="border-left:3px solid #e8dcc0;margin:12px 0;padding:8px 12px;color:#374151;white-space:pre-line">{H(appartement.InstructionsArrivee)}</blockquote>
              """;
        var envoye = await EnvoyerAsync(voyageur, RefReservation(reservation), "Votre arrivée demain",
            "Votre arrivée demain 🧳",
            $"""
            <p>Votre séjour commence demain. Voici tout ce qu'il faut pour arriver sereinement.</p>
            {Tableau(("Réservation n°", reservation.Id.ToString("D6")), ("Adresse", H(adresse)),
                ("Arrivée", $"{Date(reservation.DateArrivee)} à partir de {H(heureArrivee ?? "-")}"),
                ("Départ", $"{Date(reservation.DateDepart)} avant {H(heureDepart ?? "-")}"),
                ("Voyageurs", reservation.NombreVoyageurs.ToString()),
                ("Hôte", H(appartement.Bailleur?.Nom ?? "-")), ("Téléphone de l'hôte", H(telephoneHote ?? "-")))}
            {instructions}
            """,
            "Retrouvez ces informations, la carte et l'état des lieux photo dans « Mes réservations ». Bon séjour !",
            CheminReservationsVoyageur);
        // Mémorisé par l'appelant seulement si l'email est parti : pas de SMS en double au passage suivant.
        if (envoye)
        {
            await Sms(voyageur, "arrivee-demain",
                $"arrivée demain {Date(reservation.DateArrivee)} dès {heureArrivee ?? "-"} (réservation n° {reservation.Id:D6}). Adresse et instructions sur le site.");
        }
        return envoye;
    }

    public Task<bool> ArriveeDemainHoteAsync(Utilisateur hote, Reservation reservation, Appartement appartement, Utilisateur voyageur) =>
        EnvoyerAsync(hote, RefReservation(reservation), $"Arrivée demain de {voyageur.Nom}",
            "Arrivée demain",
            $"""
            <p><strong>{H(voyageur.Nom)}</strong> arrive demain dans votre logement situé <strong>{H(Logement(reservation, appartement, false))}</strong>.</p>
            {Tableau(("Réservation n°", reservation.Id.ToString("D6")), ("Arrivée", Date(reservation.DateArrivee)), ("Départ", Date(reservation.DateDepart)),
                ("Voyageurs", reservation.NombreVoyageurs.ToString()))}
            """,
            "Le voyageur a reçu vos instructions d'arrivée. Pensez à l'état des lieux photo à son arrivée, depuis « Réservations reçues ».",
            CheminReservationsHote);

    private const string CheminReservationsVoyageur = "/mes-reservations";
    private const string CheminReservationsHote = "/bailleur/reservations";

    /// <summary>Référence affichée dans l'objet (« [Dossier n° 000048] ») et dans l'encadré en tête de l'email.</summary>
    private readonly record struct ReferenceEmail(string Libelle, string Complement, int Numero);

    /// <param name="dossier">N° du dossier de candidature concerné : affiché dans l'objet et en tête de l'email.</param>
    /// <param name="evenement">Méthode appelante (ex. « DossierAccepteAsync »), conservée comme événement dans la boîte d'envoi.</param>
    private Task<bool> EnvoyerAsync(Utilisateur destinataire, int? dossier, string sujet, string titre, string contenu, string prochaineEtape,
        [CallerMemberName] string evenement = "") =>
        EnvoyerAsync(destinataire, dossier is { } n ? new ReferenceEmail("Dossier", "du dossier", n) : null, sujet, titre, contenu, prochaineEtape,
            dossier is null ? null : PageDossier(destinataire, dossier.Value), evenement);

    /// <summary>Page du site qui suit le dossier, selon le destinataire : le lien de l'email y mène directement.</summary>
    private static string PageDossier(Utilisateur destinataire, int dossier) => destinataire.Role switch
    {
        RoleUtilisateur.Locataire => "/mes-candidatures",
        RoleUtilisateur.Bailleur => "/bailleur/candidatures",
        _ => $"/admin/candidatures?q={dossier:D6}"
    };

    /// <returns>
    /// true si l'email est mis en file (les rappels le mémorisent alors comme traité : la boîte d'envoi se charge des nouvelles
    /// tentatives, sans double envoi ; SMTP non configuré : l'email y attend sa configuration) ; false si la mise en file a échoué.
    /// </returns>
    /// <param name="chemin">Page du site ouverte par le lien de l'email (accueil par défaut).</param>
    private async Task<bool> EnvoyerAsync(Utilisateur destinataire, ReferenceEmail? reference, string sujet, string titre, string contenu, string prochaineEtape,
        string? chemin = null, [CallerMemberName] string evenement = "")
    {
        if (reference is { } r)
        {
            sujet = $"[{r.Libelle} n° {r.Numero:D6}] {sujet}";
        }
        var encadreReference = reference is { } re
            ? $"""<p style="display:inline-block;background:#f5f2ea;border:1px solid #e8dcc0;border-radius:6px;padding:6px 12px;margin:0 0 16px;font-size:14px">Référence {re.Complement} : <strong>n° {re.Numero:D6}</strong></p>"""
            : "";

        var urlFrontend = _liens.Url;
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;color:#1f1f1f">
              <div style="background:#15161a;padding:20px 24px;border-radius:12px 12px 0 0">
                <span style="color:#d4a94a;font-size:20px;font-weight:bold">ImmoPlus</span>
              </div>
              <div style="background:#ffffff;padding:24px;border:1px solid #eee;border-top:none;border-radius:0 0 12px 12px">
                <h2 style="margin:0 0 16px">{titre}</h2>
                {encadreReference}
                <p>Bonjour {H(destinataire.Nom)},</p>
                {contenu}
                <div style="background:#f5f2ea;border-left:4px solid #d4a94a;padding:12px 16px;margin:20px 0;border-radius:4px">
                  <strong>Prochaine étape</strong><br>{prochaineEtape}
                </div>
                <p><a href="{urlFrontend}{chemin}" style="color:#b8892f">Accéder à ImmoPlus</a></p>
                <p style="color:#9ca3af;font-size:12px">Cet email vous est envoyé automatiquement, merci de ne pas y répondre.</p>
              </div>
            </div>
            """;

        try
        {
            await _boite.AjouterEmailAsync(destinataire.Email, destinataire.Id, sujet, html,
                evenement.EndsWith("Async", StringComparison.Ordinal) ? evenement[..^5] : evenement,
                reference is { } rf ? $"{rf.Libelle} n° {rf.Numero:D6}" : null);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de mettre en file l'email « {Sujet} » pour {Email}", sujet, destinataire.Email);
            return false;
        }
    }

    private static string Tableau(params (string Libelle, string Valeur)[] lignes)
    {
        var html = new StringBuilder("""<table style="border-collapse:collapse;font-size:14px;margin:12px 0">""");
        foreach (var (libelle, valeur) in lignes)
        {
            html.Append($"""<tr><td style="padding:4px 16px 4px 0;color:#6b7280">{libelle}</td><td style="padding:4px 0;font-weight:bold">{valeur}</td></tr>""");
        }
        return html.Append("</table>").ToString();
    }

    /// <summary>Référence d'un dossier : son numéro sur 6 chiffres (48 → « 000048 »).</summary>
    private static string Reference(Candidature candidature) => candidature.Id.ToString("D6");

    private static string H(string valeur) => WebUtility.HtmlEncode(valeur);
    private static string Montant(decimal? montant) => montant is null ? "-" : $"{montant.Value.ToString("N0", Fr)} GNF";
    private static string Date(DateTime? date) => date?.ToString("dd/MM/yyyy", Fr) ?? "-";
    private static string DateHeure(DateTime date) => date.ToString("dddd dd/MM/yyyy 'à' HH'h'mm", Fr);
}
