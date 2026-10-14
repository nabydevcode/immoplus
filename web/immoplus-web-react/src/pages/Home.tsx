import { Link } from "react-router-dom";
import { ArrowRight, FileCheck, KeyRound, ShieldCheck, Wallet } from "lucide-react";
import BuildingIllustration from "../components/BuildingIllustration";

export default function Home() {
  return (
    <>
      <section className="relative overflow-hidden bg-immo-dark px-6 pt-20 pb-28 md:pt-24 md:pb-36">
        <div
          className="pointer-events-none absolute inset-0 opacity-40"
          style={{
            backgroundImage:
              "radial-gradient(circle at 15% 20%, rgba(212,169,74,0.18), transparent 45%), radial-gradient(circle at 85% 0%, rgba(212,169,74,0.12), transparent 40%)",
          }}
        />
        <div
          className="pointer-events-none absolute inset-y-0 right-0 w-3/5 hidden md:block"
          style={{
            maskImage: "linear-gradient(to left, black 35%, transparent 85%)",
            WebkitMaskImage: "linear-gradient(to left, black 35%, transparent 85%)",
          }}
        >
          <BuildingIllustration className="h-full w-full object-cover opacity-60" />
        </div>
        <div
          className="pointer-events-none absolute inset-x-0 bottom-0 h-24"
          style={{ backgroundImage: "linear-gradient(to bottom, transparent, var(--color-immo-cream))" }}
        />
        <div className="relative max-w-6xl mx-auto grid md:grid-cols-2 gap-12 items-center">
          <div>
            <span className="inline-block bg-immo-gold/15 text-immo-gold text-xs font-semibold px-3 py-1.5 rounded-full mb-5 tracking-wide">
              Plateforme immobilière de confiance
            </span>
            <h1 className="text-immo-cream text-4xl md:text-5xl font-extrabold leading-tight mb-5">
              Trouvez votre logement idéal, longue durée ou séjour court
            </h1>
            <p className="text-gray-400 text-lg mb-9 max-w-xl">
              Location longue durée avec dossier de candidature et financement d'avance de loyer, ou séjour court meublé
              façon réservation directe — deux parcours pensés pour vos besoins.
            </p>

            <div className="flex flex-wrap gap-4">
              <Link
                to="/annonces/longue-duree"
                className="group inline-flex items-center gap-2 bg-immo-gold text-immo-dark font-semibold px-6 py-3.5 rounded-xl hover:bg-immo-gold-light transition-colors"
              >
                Location longue durée
                <ArrowRight size={18} className="transition-transform group-hover:translate-x-1" />
              </Link>
              <Link
                to="/annonces/sejour-court"
                className="inline-flex items-center gap-2 border border-white/20 text-immo-cream font-semibold px-6 py-3.5 rounded-xl hover:border-immo-gold hover:text-immo-gold transition-colors"
              >
                Séjours courts
              </Link>
            </div>
          </div>

          <div className="hidden md:grid grid-cols-2 gap-4">
            <div className="col-span-2 rounded-2xl bg-white/5 border border-white/10 p-6 backdrop-blur">
              <p className="text-immo-gold text-3xl font-extrabold">2</p>
              <p className="text-gray-400 text-sm mt-1">parcours de location adaptés à chaque besoin</p>
            </div>
            <div className="rounded-2xl bg-white/5 border border-white/10 p-6 backdrop-blur">
              <ShieldCheck className="text-immo-gold mb-3" size={22} />
              <p className="text-immo-cream text-sm font-semibold">Dossiers vérifiés</p>
            </div>
            <div className="rounded-2xl bg-white/5 border border-white/10 p-6 backdrop-blur">
              <Wallet className="text-immo-gold mb-3" size={22} />
              <p className="text-immo-cream text-sm font-semibold">Financement d'avance</p>
            </div>
          </div>
        </div>
      </section>

      <section className="bg-white px-6 py-20">
        <div className="max-w-6xl mx-auto">
          <div className="max-w-xl mb-12">
            <h2 className="text-2xl md:text-3xl font-extrabold mb-3">Deux parcours, un seul objectif</h2>
            <p className="text-gray-600">Choisissez la formule adaptée à votre projet de logement.</p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div className="group bg-immo-cream rounded-2xl p-8 border border-black/5 hover:shadow-lg hover:-translate-y-1 transition-all">
              <div className="w-12 h-12 rounded-xl bg-immo-dark/5 flex items-center justify-center mb-5">
                <FileCheck className="text-immo-dark" size={22} />
              </div>
              <h3 className="font-bold text-lg mb-2">Location longue durée</h3>
              <p className="text-gray-600 text-sm mb-6 leading-relaxed">
                Bail classique. Vous candidatez avec votre dossier (situation professionnelle, garant si besoin,
                documents), le bailleur examine votre candidature, puis un financement d'avance de loyer est proposé si
                votre apport est insuffisant.
              </p>
              <Link
                to="/annonces/longue-duree"
                className="inline-flex items-center gap-1.5 text-immo-gold font-semibold text-sm group-hover:gap-2.5 transition-all"
              >
                Voir les annonces <ArrowRight size={16} />
              </Link>
            </div>

            <div className="group bg-immo-cream rounded-2xl p-8 border border-black/5 hover:shadow-lg hover:-translate-y-1 transition-all">
              <div className="w-12 h-12 rounded-xl bg-immo-dark/5 flex items-center justify-center mb-5">
                <KeyRound className="text-immo-dark" size={22} />
              </div>
              <h3 className="font-bold text-lg mb-2">Séjour court</h3>
              <p className="text-gray-600 text-sm mb-6 leading-relaxed">
                Logement meublé pour un mois maximum, renouvelable. Réservation directe et rapide, sans dossier de
                candidature — vous choisissez vos dates et réglez à la réservation.
              </p>
              <Link
                to="/annonces/sejour-court"
                className="inline-flex items-center gap-1.5 text-immo-gold font-semibold text-sm group-hover:gap-2.5 transition-all"
              >
                Voir les annonces <ArrowRight size={16} />
              </Link>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
