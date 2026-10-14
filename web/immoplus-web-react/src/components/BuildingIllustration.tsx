interface Immeuble {
  x: number;
  y: number;
  largeur: number;
  hauteur: number;
  colonnes: number;
  lignes: number;
}

const immeubles: Immeuble[] = [
  { x: 40, y: 240, largeur: 70, hauteur: 260, colonnes: 3, lignes: 8 },
  { x: 130, y: 120, largeur: 95, hauteur: 380, colonnes: 4, lignes: 11 },
  { x: 245, y: 170, largeur: 80, hauteur: 330, colonnes: 3, lignes: 10 },
  { x: 340, y: 280, largeur: 60, hauteur: 220, colonnes: 2, lignes: 7 },
];

function Fenetres({ immeuble, seed }: { immeuble: Immeuble; seed: number }) {
  const { x, y, largeur, hauteur, colonnes, lignes } = immeuble;
  const marge = 10;
  const gapX = (largeur - marge * 2) / colonnes;
  const gapY = (hauteur - marge * 2) / lignes;
  const taille = Math.min(gapX, gapY) * 0.5;

  const fenetres = [];
  for (let l = 0; l < lignes; l++) {
    for (let c = 0; c < colonnes; c++) {
      const allume = (c + l * colonnes + seed) % 5 === 0;
      fenetres.push(
        <rect
          key={`${c}-${l}`}
          x={x + marge + c * gapX + (gapX - taille) / 2}
          y={y + marge + l * gapY + (gapY - taille) / 2}
          width={taille}
          height={taille}
          rx={1}
          fill={allume ? "var(--color-immo-gold)" : "currentColor"}
          opacity={allume ? 0.55 : 0.12}
        />,
      );
    }
  }
  return <>{fenetres}</>;
}

export default function BuildingIllustration({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 440 520" className={className} fill="none" xmlns="http://www.w3.org/2000/svg">
      <line x1="0" y1="500" x2="440" y2="500" stroke="currentColor" strokeOpacity={0.15} />
      {immeubles.map((immeuble, i) => (
        <g key={i} className="text-immo-cream">
          <rect
            x={immeuble.x}
            y={immeuble.y}
            width={immeuble.largeur}
            height={immeuble.hauteur}
            fill="var(--color-immo-dark-light)"
            fillOpacity={0.6}
            stroke="var(--color-immo-gold)"
            strokeOpacity={0.25}
          />
          <Fenetres immeuble={immeuble} seed={i} />
        </g>
      ))}
    </svg>
  );
}
