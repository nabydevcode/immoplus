# Schéma du modèle de données — ImmoPlus

Toutes les entités existent dès la migration initiale (`InitialCreate`), même si certaines routes/pages associées arrivent dans des lots ultérieurs.

## Utilisateur
`Id, Nom, Telephone, Email, MotDePasseHash, Role (Admin|Bailleur|Locataire)`

## Appartement
`Id, BailleurId → Utilisateur, Adresse, Description?, Disponible, TypeOffre (LongueDuree|SejourCourt)`
- Longue durée : `LoyerMensuel, DureeAvanceExigee` (nullable, requis si `TypeOffre = LongueDuree`)
- Séjour court : `PrixParNuit, DureeMaxMois` (nullable, requis si `TypeOffre = SejourCourt`, défaut 1 mois)

## Location (longue durée)
`Id, LocataireId → Utilisateur, AppartementId → Appartement, CandidatureId? → Candidature, DateDebut, DateFin?, Statut (EnCours|Terminee|Resiliee)`
Créée en interne par le service au moment de l'acceptation d'une Candidature — pas de route publique de création directe.

## FinancementLocation
`Id, LocationId → Location (1-1), MontantTotal, DureeMois, ApportInitial, MontantARembourser, TauxFrais (0.02–0.05), NombreMensualites, MontantMensualite, Statut (EnAttenteValidation|EnCours|Solde)`

## Mensualite
`Id, FinancementLocationId → FinancementLocation, NumeroEcheance, DateEcheance, MontantDu, MontantPaye, Statut (AVenir|Partiellement|Payee)`

## Paiement (générique — longue durée ET séjour court)
`Id, MensualiteId? → Mensualite, ReservationId? → Reservation, Montant, DatePaiement, ModePaiement, Reference?`
Exactement un des deux FK (`MensualiteId` / `ReservationId`) doit être renseigné — validé en service, pas en contrainte SQL.

## Candidature
`Id, LocataireId → Utilisateur, AppartementId → Appartement, SituationProfessionnelle (CDI|CDD|Independant|DebutActiviteRevenuFixe|Autre), Statut (EnAttente|Acceptee|Refusee), DateSoumission, DateTraitement?, MotifRefus?`

## Garant
`Id, CandidatureId → Candidature (1-1, unique), Nom, Telephone, Email?, SituationProfessionnelle`

## Document
`Id, CandidatureId → Candidature, Proprietaire (Locataire|Garant), TypeDocument (PieceIdentite|JustificatifRevenu|JustificatifDomicile|Autre), NomFichierOriginal, CheminStockage, TailleOctets, DateUpload`
Stockage sur disque local (`Storage/documents/`), jamais exposé en fichier statique — téléchargement via route authentifiée qui vérifie les droits (locataire propriétaire, bailleur de l'annonce concernée, ou admin).

## Reservation (séjour court)
`Id, LocataireId → Utilisateur, AppartementId → Appartement, DateArrivee, DateDepart, MontantTotal, Statut (EnAttente|Confirmee|Annulee|Terminee), DateCreation`

---

## Flux métier

**Longue durée** : Candidature → (Bailleur/Admin accepte) → Location créée (+ FinancementLocation si apport insuffisant) → Mensualites générées → Paiements.

**Séjour court** : Reservation créée directement (dates + montant calculé) → Paiement direct (pas de dossier, pas de mensualités).
