import { EnTete } from "../../components/admin/ui";
import ContenuProfil from "../../components/profil/ContenuProfil";

export default function ProfilAdmin() {
  return (
    <>
      <EnTete titre="Mon profil" description="Vos coordonnées et votre mot de passe." />
      <ContenuProfil />
    </>
  );
}
