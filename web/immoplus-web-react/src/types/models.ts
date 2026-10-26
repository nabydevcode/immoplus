export interface PhotoDto {
  id: number;
  /** Chemin relatif à l'API : utiliser urlPhoto() pour l'afficher. */
  url: string;
  /** Miniature (480 px) : cartes, listes et vignettes. Chemin relatif à l'API, comme `url`. */
  urlMiniature?: string | null;
  ordre: number;
  /** Panorama 360° de la visite virtuelle (sinon photo classique). */
  est360?: boolean;
  /** Nom de la pièce, pour les panoramas 360°. */
  libelle?: string | null;
}

export interface AppartementDto {
  id: number;
  bailleurId: number;
  bailleurNom: string;
  adresse: string;
  description?: string | null;
  disponible: boolean;
  /** « Supprimé » : désactivé, masqué des annonces, historique conservé, réactivable (visible du seul hôte et des admins). */
  estDesactive?: boolean;
  dateDesactivation?: string | null;
  /** Une location est en cours : l'annonce ne peut pas être modifiée. */
  enLocation?: boolean;
  photos?: PhotoDto[];
  typeOffre: string;

  loyerMensuel?: number | null;
  dureeAvanceExigee?: number | null;

  prixParNuit?: number | null;
  dureeMaxMois?: number | null;

  /** Localisation et capacité (tous types d'offre). */
  ville?: string | null;
  quartier?: string | null;
  capacite?: number | null;
  chambres?: number | null;
  lits?: number | null;
  sallesDeBain?: number | null;
  /** Clés d'équipements (voir components/sejour/equipements.ts) ; [] par défaut. */
  equipements?: string[];

  /** Séjour court uniquement. */
  fraisMenage?: number | null;
  /** Remboursable, affichée à part : non incluse dans le montant total. */
  caution?: number | null;
  /** Réduction à partir de 7 nuits (0–50 %). */
  reductionSemainePct?: number | null;
  /** Réduction à partir de 28 nuits (0–70 %), remplace la réduction semaine. */
  reductionMoisPct?: number | null;
  nuitsMin?: number | null;
  /** Sans validation de l'hôte : la réservation passe directement en attente de paiement. */
  reservationInstantanee?: boolean;
  /** Séjour court : conditions de remboursement en cas d'annulation par le voyageur. */
  politiqueAnnulation?: PolitiqueAnnulation | null;

  /** Avis des voyageurs (séjour court) : note sur 5 à une décimale, null sans avis. */
  noteMoyenne?: number | null;
  nombreAvis?: number;
  /** Hôte et badges de confiance (calculés par l'API). */
  hote?: HoteDto | null;

  /** Séjour court : heures d'arrivée et de départ (« HH:mm », 14:00 et 11:00 par défaut). */
  heureArrivee?: string | null;
  heureDepart?: string | null;
  /** Accès, clés, wifi… : renvoyé seulement au propriétaire et à l'admin (null dans les listes publiques). */
  instructionsArrivee?: string | null;
  /** Position du logement : approximative (3 décimales) pour le public, exacte pour le propriétaire et l'admin. */
  latitude?: number | null;
  longitude?: number | null;
  /** Nombre de voyageurs ayant ajouté l'annonce en favori (propriétaire et admin, 0 sinon). */
  nombreFavoris?: number;
  /** Séjour court : « adresse » est un libellé approximatif (quartier, ville) tant que la réservation n'est pas confirmée. */
  adresseMasquee?: boolean;
  /** Logement désactivé ou hôte désactivé : photos servies seulement avec le jeton (propriétaire, admin). */
  photosProtegees?: boolean;
  /** L'hôte du logement est désactivé (visible du propriétaire et de l'admin). */
  bailleurDesactive?: boolean;
}

export type PolitiqueAnnulation = "Flexible" | "Moderee" | "Stricte";

/** Hôte d'une annonce et ses badges de confiance. */
export interface HoteDto {
  id: number;
  nom: string;
  identiteVerifiee: boolean;
  superhote: boolean;
  /** Pourcentage de demandes répondues sur 12 mois (null sans demande). */
  tauxReponse: number | null;
  delaiReponseHeures: number | null;
  /** « AAAA-MM » */
  membreDepuis: string | null;
  nombreAvis: number;
  noteMoyenne: number | null;
}

/** Avis laissé après un séjour terminé, par le voyageur (sur le logement) ou par l'hôte (sur le voyageur). */
export interface AvisDto {
  id: number;
  /** Séjour court ; null pour un avis de location longue durée. */
  reservationId: number | null;
  /** Location longue durée ; null pour un avis de séjour. */
  locationId?: number | null;
  /** Prénom de l'auteur. */
  auteurNom: string;
  note: number;
  proprete: number | null;
  communication: number | null;
  emplacement: number | null;
  rapportQualitePrix: number | null;
  commentaire: string;
  dateCreation: string;
  reponseHote: string | null;
  dateReponse: string | null;
  sens: SensAvis | number;
  /** Masqué par un admin après signalement (exclu des moyennes et de l'affichage public). */
  masque?: boolean;
}

export type SensAvis = "VoyageurVersLogement" | "HoteVersVoyageur" | "LocataireVersLogement" | "BailleurVersLocataire";

export interface MoyennesAvisDto {
  proprete: number | null;
  communication: number | null;
  emplacement: number | null;
  rapportQualitePrix: number | null;
}

/** GET appartement/{id}/avis (et utilisateur/{id}/avis, sans moyennes par critère). */
export interface ListeAvisDto {
  moyenne: number | null;
  nombre: number;
  moyennes: MoyennesAvisDto | null;
  avis: AvisDto[];
}

/** Message échangé entre le voyageur et l'hôte au sujet d'une réservation. */
export interface MessageDto {
  id: number;
  reservationId: number;
  auteurId: number;
  auteurNom: string;
  contenu: string;
  dateEnvoi: string;
  dateLecture: string | null;
  deMoi: boolean;
}

/** GET messages/non-lus */
export interface MessagesNonLusDto {
  total: number;
  parReservation: { reservationId: number; nombre: number }[];
}

/** Remboursement prévu si l'appelant annule maintenant (GET reservation/{id}/annulation/apercu). */
export interface ApercuAnnulationDto {
  politique: PolitiqueAnnulation;
  montantPaye: number;
  montantRembourse: number;
  pourcentageNuitees: number;
  explication: string;
}

/** Villes et quartiers des annonces disponibles (GET appartement/villes). */
export interface VilleDto {
  ville: string;
  quartiers: string[];
}

/** Période du calendrier d'un logement (fin exclue). id et motif : propriétaire et admin uniquement. */
export interface PeriodeCalendrierDto {
  debut: string;
  fin: string;
  type: "Reservee" | "Bloquee";
  id: number | null;
  motif: string | null;
}

/** Prix détaillé d'un séjour avant réservation (GET reservation/devis). */
export interface DevisDto {
  nombreNuits: number;
  prixParNuit: number;
  sousTotal: number;
  reductionPct: number;
  reductionMontant: number;
  fraisMenage: number;
  montantTotal: number;
  caution: number;
  reservationInstantanee: boolean;
  disponible: boolean;
  raison: string | null;
}

export interface UtilisateurDto {
  id: number;
  nom: string;
  telephone: string;
  email: string;
  role: string;
  statut: "EnAttente" | "Valide" | "Refuse" | "Desactive";
  motifDesactivation?: string | null;
  dateDesactivation?: string | null;
  /** Réponse à la désactivation d'un hôte : réservations non confirmées annulées et réservations confirmées à venir. */
  reservationsAnnulees?: number | null;
  reservationsConfirmeesAVenir?: number | null;
  /** Recevoir aussi les notifications importantes par SMS/WhatsApp (profil, true par défaut). */
  notificationsSms?: boolean;
  /** Pièce d'identité validée par un admin (badge « Identité vérifiée »). */
  identiteVerifiee?: boolean;
  dateVerificationIdentite?: string | null;
  /** Adresse email confirmée par le lien reçu à l'inscription (condition de la validation par un admin). */
  emailConfirme: boolean;
  dateConfirmationEmail?: string | null;
}

export interface PagedResultDto<T> {
  donnees: T[];
  pageActuelle: number;
  taillePage: number;
  totalElements: number;
  totalPages: number;
}

export interface DocumentDto {
  id: number;
  candidatureId: number;
  proprietaire: string;
  typeDocument: string;
  nomFichierOriginal: string;
  tailleOctets: number;
  dateUpload: string;
}

export interface GarantDto {
  id: number;
  nom: string;
  telephone: string;
  email?: string | null;
  situationProfessionnelle: string;
}

export interface CandidatureDto {
  id: number;
  locataireId: number;
  locataireNom: string;
  locataireTelephone: string;
  locataireEmail: string;
  appartementId: number;
  appartementAdresse: string;
  apportInitial: number;
  situationProfessionnelle: string;
  statut: "EnAttente" | "Acceptee" | "Refusee" | "ListeAttente" | "Annulee" | "Preselectionnee";
  dateSoumission: string;
  dateTraitement?: string | null;
  motifRefus?: string | null;
  garant?: GarantDto | null;
  documents: DocumentDto[];
  demandesComplement?: DemandeComplementDto[];
  visites?: VisiteDto[];
  locationId?: number | null;
  financementLocationId?: number | null;
  /** Après l'attribution : réception de l'apport, puis remise des clés. */
  dateApportPaye?: string | null;
  dateRemiseCles?: string | null;
}

/** Visite du logement proposée par le bailleur à un candidat présélectionné. */
export interface VisiteDto {
  id: number;
  dateHeure: string;
  message?: string | null;
  statut: "Proposee" | "Confirmee" | "Indisponible" | "Effectuee" | "Absent" | "Annulee";
  reponseLocataire?: string | null;
  /** Visible uniquement par le bailleur et l'admin. */
  notesBailleur?: string | null;
}

export interface PieceDemandeeDto {
  proprietaire: "Locataire" | "Garant";
  typeDocument: string;
  recue: boolean;
}

/** Pièces manquantes demandées par le bailleur, avec une date limite. */
export interface DemandeComplementDto {
  id: number;
  pieces: PieceDemandeeDto[];
  message?: string | null;
  dateLimite: string;
  dateDemande: string;
  complete: boolean;
  depassee: boolean;
}

export interface ReservationDto {
  id: number;
  locataireId: number;
  locataireNom: string;
  appartementId: number;
  appartementAdresse: string;
  dateArrivee: string;
  dateDepart: string;
  montantTotal: number;
  montantPaye: number;
  /** DemandeEnvoyee : l'hôte doit accepter ; EnAttente : en attente de paiement. */
  statut: "EnAttente" | "Confirmee" | "Annulee" | "Terminee" | "DemandeEnvoyee" | "Refusee";
  dateCreation: string;
  appartementVille?: string | null;
  appartementQuartier?: string | null;
  bailleurNom?: string | null;
  nombreVoyageurs?: number;
  nombreNuits?: number;
  prixParNuit?: number;
  reductionMontant?: number;
  fraisMenage?: number;
  caution?: number;
  motifRefus?: string | null;
  dateReponseHote?: string | null;
  /** En attente de paiement : sans paiement avant cet horodatage, la réservation est annulée (null si non concerné). */
  payerAvant?: string | null;

  /** Avis : chaque partie peut évaluer l'autre jusqu'au jour indiqué (séjour terminé). */
  avisVoyageurDonne?: boolean;
  avisHoteDonne?: boolean;
  avisPossibleJusquau?: string | null;
  /** Messages non lus par l'appelant. */
  messagesNonLus?: number;

  politiqueAnnulation?: PolitiqueAnnulation | null;
  montantARembourser?: number | null;
  montantRembourse?: number | null;
  dateRemboursement?: string | null;
  annuleePar?: "Voyageur" | "Hote" | "Admin" | "Systeme" | null;
  motifAnnulation?: string | null;
  dateAnnulation?: string | null;

  /** Arrivée : heures du logement ; le reste n'est rempli que pour une réservation confirmée ou terminée. */
  heureArrivee?: string | null;
  heureDepart?: string | null;
  instructionsArrivee?: string | null;
  adresseComplete?: string | null;
  telephoneHote?: string | null;
  latitudeExacte?: number | null;
  longitudeExacte?: number | null;
}

export type OperateurMobile = "OrangeMoney" | "MtnMomo";
export type StatutTransactionMobile = "EnAttente" | "Reussie" | "Echouee" | "Expiree";

/** Paiement mobile money d'une réservation (POST paiement-mobile/initier, GET paiement-mobile/{id}). */
export interface TransactionMobileDto {
  id: number;
  reservationId: number;
  operateur: OperateurMobile | number;
  /** Masqué : « 6•• •• •• 12 ». */
  telephone: string;
  montant: number;
  statut: StatutTransactionMobile | number;
  referenceFournisseur: string | null;
  messageErreur: string | null;
  dateCreation: string;
  instructions: string;
  /** Fournisseur simulé (développement local) : boutons de simulation affichés. */
  simulation?: boolean;
}

/** Photo d'état des lieux d'une réservation (GET reservation/{id}/etat-des-lieux). */
export interface PhotoEtatDesLieuxDto {
  id: number;
  moment: "Arrivee" | "Depart" | number;
  auteurId: number;
  auteurNom: string;
  /** Route protégée de l'API (etat-des-lieux/photo/{id}) : à charger avec le jeton. */
  url: string;
  commentaire: string | null;
  dateCreation: string;
}

/** GET bailleur/tableau-de-bord?annee= */
export interface TableauDeBordHoteDto {
  annee: number;
  /** Revenus nets : encaissé − remboursé. */
  revenusTotal: number;
  remboursementsTotal?: number;
  /** remboursements : montants remboursés aux voyageurs (valeur positive, déduite). */
  revenusParMois: { mois: number; sejours: number; loyers: number; remboursements?: number }[];
  tauxOccupation: number | null;
  occupationParLogement: {
    appartementId: number;
    adresse: string;
    nuitsReservees: number;
    nuitsPossibles: number;
    /** null si aucune nuit disponible. */
    taux: number | null;
  }[];
  prochainesArrivees: {
    reservationId: number;
    voyageurNom: string;
    appartementAdresse: string;
    dateArrivee: string;
    dateDepart: string;
    nombreVoyageurs: number;
  }[];
  demandesEnAttente: number;
  messagesNonLus: number;
  noteMoyenne: number | null;
  nombreAvis: number;
  superhote: boolean;
  tauxReponse: number | null;
}

export interface LocationDto {
  id: number;
  locataireId: number;
  locataireNom: string;
  appartementId: number;
  appartementAdresse: string;
  candidatureId?: number | null;
  dateDebut: string;
  dateFin?: string | null;
  statut: "EnCours" | "Terminee" | "Resiliee";
  dateApportPaye?: string | null;
  dateRemiseCles?: string | null;
  /** Fin de location : préavis de 3 mois. */
  datePreavis?: string | null;
  dateFinPrevue?: string | null;
  preavisDonnePar?: "Locataire" | "Bailleur" | null;
  motifPreavis?: string | null;
  /** Avis : après la fin de la location (ou à partir de 6 mois de location en cours), jusqu'à la date indiquée. */
  avisLocataireDonne?: boolean;
  avisBailleurDonne?: boolean;
  /** La fenêtre d'avis est ouverte aujourd'hui (calculé par l'API). */
  avisPossible?: boolean;
  avisPossibleDepuis?: string | null;
  avisPossibleJusquau?: string | null;
}

export interface FinancementDto {
  id: number;
  locationId: number;
  candidatureId?: number | null;
  locataireNom: string;
  appartementAdresse: string;
  montantTotal: number;
  dureeMois: number;
  apportInitial: number;
  montantARembourser: number;
  tauxFrais: number;
  nombreMensualites: number;
  montantMensualite: number;
  statut: "EnAttenteValidation" | "EnCours" | "Solde";
  // Bilan de ponctualité
  mensualitesPayeesATemps: number;
  mensualitesPayeesEnRetard: number;
  mensualitesEnRetard: number;
  joursRetardCumules: number;
  montantEnRetard: number;
}

export interface MensualiteDto {
  id: number;
  financementLocationId: number;
  numeroEcheance: number;
  dateEcheance: string;
  montantDu: number;
  montantPaye: number;
  statut: "AVenir" | "Partiellement" | "Payee";
  /** Ponctualité calculée par l'API à partir de l'historique des paiements. */
  ponctualite: "AVenir" | "ATemps" | "PayeeEnRetard" | "EnRetard";
  joursRetard: number;
  /** Date du paiement qui a soldé la mensualité. */
  dateReglement?: string | null;
  paiements: PaiementEnregistreDto[];
}

/** Loyer mensuel sans frais, dû après la période couverte par l'avance. */
export interface LoyerDto {
  id: number;
  locationId: number;
  numero: number;
  dateEcheance: string;
  montantDu: number;
  montantPaye: number;
  statut: "AVenir" | "Partiellement" | "Payee";
  ponctualite: "AVenir" | "ATemps" | "PayeeEnRetard" | "EnRetard";
  joursRetard: number;
  dateReglement?: string | null;
  paiements: PaiementEnregistreDto[];
}

export interface LoyersLocationDto {
  locationId: number;
  loyerMensuel: number;
  /** Premier loyer : fin de la période couverte par l'avance (null tant que les clés ne sont pas remises). */
  debutLoyers?: string | null;
  loyers: LoyerDto[];
  payesATemps: number;
  payesEnRetard: number;
  enRetard: number;
  joursRetardCumules: number;
  montantEnRetard: number;
}

export interface PaiementEnregistreDto {
  id: number;
  montant: number;
  datePaiement: string;
  modePaiement: string;
  reference?: string | null;
}

/** Profil de l'utilisateur connecté (GET/PUT utilisateur/moi). */
export type ProfilDto = UtilisateurDto;

export interface DossierAPrendreEnChargeDto {
  candidatureId: number;
  locationId: number;
  locataireNom: string;
  appartementAdresse: string;
  apportInitial: number;
  dateAttribution: string;
}

export interface CleARemettreDto {
  /** Null si la location ne provient pas d'une candidature. */
  candidatureId: number | null;
  locationId: number;
  locataireNom: string;
  appartementAdresse: string;
  dateApportPaye: string;
}

export interface EcheancierAGenererDto {
  financementId: number;
  /** Null si la location ne provient pas d'une candidature. */
  candidatureId: number | null;
  locationId: number;
  locataireNom: string;
  montantARembourser: number;
}

export interface RetardDto {
  type: "Mensualite" | "Loyer";
  /** Null si la location ne provient pas d'une candidature. */
  candidatureId: number | null;
  locationId: number;
  locataireNom: string;
  dateEcheance: string;
  joursRetard: number;
  resteDu: number;
}

export interface PreavisEnCoursDto {
  locationId: number;
  /** Null si la location ne provient pas d'une candidature. */
  candidatureId: number | null;
  locataireNom: string;
  appartementAdresse: string;
  dateFinPrevue: string;
  finAtteinte: boolean;
}

/** Tableau de bord « à faire » de l'admin (GET admin/tableau-de-bord). */
export interface TableauDeBordDto {
  utilisateursAValider: number;
  candidaturesAEtudier: number;
  visitesAVenir: number;
  logements: number;
  locationsEnCours: number;
  dossiersAPrendreEnCharge: DossierAPrendreEnChargeDto[];
  clesARemettre: CleARemettreDto[];
  echeanciersAGenerer: EcheancierAGenererDto[];
  retards: { nombre: number; montant: number; liste: RetardDto[] };
  preavisEnCours: PreavisEnCoursDto[];
  reservationsEnAttentePaiement: number;
  reservationsAVenir7Jours: number;
  remboursementsAEffectuer?: RemboursementAEffectuerDto[];
}

export interface RemboursementAEffectuerDto {
  reservationId: number;
  voyageurNom: string;
  montantARembourser: number;
  montantRembourse: number | null;
  dateAnnulation: string | null;
}

/** Sauvegarde de la base (GET admin/sauvegardes). */
export interface SauvegardeDto {
  /** Nom du fichier (immoplus-AAAAMMJJ-HHMM.dump, format pg_dump), à utiliser pour le téléchargement. */
  nom: string;
  /** Taille en octets. */
  taille: number;
  /** Horodatage UTC de la sauvegarde. */
  date: string;
}

export type CanalMessage = "Sms" | "WhatsApp";
export type StatutNotificationSms = "Envoye" | "Echec" | "Simule";

/** SMS / WhatsApp envoyé (GET admin/sms, GET utilisateur/moi/sms). */
export interface NotificationSmsDto {
  id: number;
  utilisateurId: number;
  /** Nom du destinataire (historique admin). */
  utilisateurNom?: string | null;
  telephone: string;
  canal: CanalMessage | number;
  texte: string;
  statut: StatutNotificationSms | number;
  referenceFournisseur?: string | null;
  erreur?: string | null;
  dateEnvoi: string;
  /** Événement déclencheur, ex. « reservation-confirmee ». */
  evenement: string;
}

export type TypeMessageSortant = "Email" | "Sms";
export type StatutMessageSortant = "EnAttente" | "Envoye" | "Echec" | "Abandonne";

/** Message de la boîte d'envoi des emails / SMS (GET admin/messages-sortants). Horodatages UTC. */
export interface MessageSortantDto {
  id: number;
  type: TypeMessageSortant;
  /** Adresse email ou téléphone. */
  destinataire: string;
  utilisateurId: number | null;
  /** Objet de l'email (null pour un SMS). */
  sujet: string | null;
  /** Texte du SMS (null pour un email). */
  texte: string | null;
  /** Ex. « Dossier n° 000048 ». */
  reference: string | null;
  evenement: string;
  statut: StatutMessageSortant;
  tentatives: number;
  /** Prochain essai (EnAttente / Echec uniquement). */
  prochaineTentative: string | null;
  derniereErreur: string | null;
  dateCreation: string;
  dateDerniereTentative: string | null;
  dateEnvoi: string | null;
}

/** Action enregistrée dans le journal d'audit (GET admin/journal). */
export interface JournalActionDto {
  id: number;
  date: string;
  utilisateurId: number | null;
  utilisateurNom: string;
  role: string;
  /** Code court, ex. « utilisateur.desactiver ». */
  action: string;
  /** Type d'objet concerné, ex. « Utilisateur », « Reservation ». */
  cible: string;
  cibleId: number | null;
  details: string;
  adresseIp: string | null;
}

export type CibleSignalement = "Appartement" | "Utilisateur" | "Avis" | "Message";
export type MotifSignalement =
  | "Arnaque"
  | "AnnonceTrompeuse"
  | "ContenuInapproprie"
  | "Doublon"
  | "LogementIndisponible"
  | "ComportementAbusif"
  | "Autre";
export type StatutSignalement = "Nouveau" | "EnCours" | "Traite" | "Rejete";
export type ActionSignalement = "aucune" | "desactiverAnnonce" | "desactiverUtilisateur" | "masquerAvis";

/** Signalement d'un contenu ou d'un utilisateur (GET admin/signalements). */
export interface SignalementDto {
  id: number;
  auteurId: number | null;
  auteurNom?: string | null;
  /** Email de l'auteur (saisi s'il n'était pas connecté). */
  auteurEmail: string | null;
  cible: CibleSignalement;
  cibleId: number;
  /** Référence affichée (n° sur 6 chiffres). */
  reference?: string;
  /** Résumé lisible de la cible (adresse de l'annonce, nom, extrait de l'avis ou du message). */
  cibleResume?: string | null;
  /** Liens utiles : annonce (annonce, avis), réservation (message), location (avis longue durée). */
  appartementId?: number | null;
  reservationId?: number | null;
  locationId?: number | null;
  /** Utilisateur visé, auteur de l'avis / du message, ou bailleur de l'annonce. */
  utilisateurCibleId?: number | null;
  actionAppliquee?: string | null;
  motif: MotifSignalement | number;
  texte: string;
  statut: StatutSignalement | number;
  decisionAdmin: string | null;
  dateCreation: string;
  dateTraitement: string | null;
  traiteParId: number | null;
  traiteParNom?: string | null;
}

export type TypePieceIdentite = "CNI" | "Passeport" | "PermisConduire" | "CarteConsulaire";
export type StatutPieceIdentite = "EnAttente" | "Validee" | "Refusee";

/** Demande de vérification d'identité (GET utilisateur/moi/identite, GET admin/identites). */
export interface PieceIdentiteDto {
  id: number;
  utilisateurId: number;
  utilisateurNom?: string | null;
  utilisateurEmail?: string | null;
  utilisateurRole?: string | null;
  type: TypePieceIdentite | number;
  statut: StatutPieceIdentite | number;
  motifRefus: string | null;
  dateEnvoi: string;
  dateDecision: string | null;
  decideParNom?: string | null;
  /** « image/jpeg » ou « application/pdf ». */
  typeRecto?: string;
  /** Un verso a été envoyé. */
  aVerso?: boolean;
  typeVerso?: string | null;
}

/** GET utilisateur/moi/identite : état du badge et dernière demande (null s'il n'y en a jamais eu). */
export interface StatutIdentiteDto {
  identiteVerifiee: boolean;
  dateVerificationIdentite: string | null;
  demande: PieceIdentiteDto | null;
}

/** Pastilles du menu admin (GET admin/compteurs). */
export interface CompteursAdminDto {
  signalementsNouveaux: number;
  identitesEnAttente: number;
}
