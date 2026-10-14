import type { StatutPieceIdentite, TypePieceIdentite } from "../../types/models";
import { valeurEnum } from "../admin/outils";

/** Pièces acceptées, dans l'ordre de l'énumération de l'API (CNI = 0 … CarteConsulaire = 3). */
export const TYPES_PIECE: { valeur: TypePieceIdentite; libelle: string }[] = [
  { valeur: "CNI", libelle: "Carte nationale d'identité" },
  { valeur: "Passeport", libelle: "Passeport" },
  { valeur: "PermisConduire", libelle: "Permis de conduire" },
  { valeur: "CarteConsulaire", libelle: "Carte consulaire" },
];

export const STATUTS_PIECE: {
  valeur: StatutPieceIdentite;
  libelle: string;
  ton: "attente" | "succes" | "danger";
}[] = [
  { valeur: "EnAttente", libelle: "En attente", ton: "attente" },
  { valeur: "Validee", libelle: "Validée", ton: "succes" },
  { valeur: "Refusee", libelle: "Refusée", ton: "danger" },
];

export const libelleTypePiece = (type: TypePieceIdentite | number) =>
  TYPES_PIECE.find((t) => t.valeur === valeurEnum(type, TYPES_PIECE))?.libelle ?? String(type);

export const statutPiece = (statut: StatutPieceIdentite | number) => valeurEnum(statut, STATUTS_PIECE);

export const infoStatutPiece = (statut: StatutPieceIdentite | number) =>
  STATUTS_PIECE.find((s) => s.valeur === statutPiece(statut)) ?? {
    valeur: String(statut) as StatutPieceIdentite,
    libelle: String(statut),
    ton: "attente" as const,
  };

/** Taille maximale d'un fichier (recto ou verso) : 10 Mo. */
export const TAILLE_MAX_PIECE = 10 * 1024 * 1024;
export const FORMATS_PIECE = "image/jpeg,image/png,image/webp,application/pdf";

/** Contrôle côté navigateur (l'API vérifie aussi) : image ou PDF, 10 Mo au plus. */
export function erreurFichierPiece(fichier: File | null, libelle: string) {
  if (!fichier) return null;
  if (!/^image\/|^application\/pdf$/.test(fichier.type)) return `${libelle} : choisissez une image ou un PDF.`;
  if (fichier.size > TAILLE_MAX_PIECE) return `${libelle} : le fichier dépasse 10 Mo.`;
  return null;
}
