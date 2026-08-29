import Button from "@/components/ui/Button";
import styles from "./HeroSection.module.css";

export default function HeroSection() {
  return (
    <section className={styles.hero}>
      <p className={styles.heroLabel}>Plataforma de Campeonatos Online</p>
      <h1 className={styles.heroTitle}>
        Compita.{" "}
        <span className={styles.heroHighlight}>Supere.</span> Vença.
      </h1>
      <p className={styles.heroSubtitle}>
        Descubra, participe e organize campeonatos de jogos online. Mostre seu
        talento e conquiste seu lugar no topo.
      </p>
      <div className={styles.heroActions}>
        <Button variant="primary">Explorar campeonatos</Button>
        <Button variant="secondary">Criar campeonato</Button>
      </div>
    </section>
  );
}
