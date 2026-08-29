import { Campeonato } from "@/types";
import Badge from "@/components/ui/Badge";
import styles from "./ChampionshipsGrid.module.css";

interface ChampionshipsGridProps {
  campeonatos: Campeonato[];
}

export default function ChampionshipsGrid({
  campeonatos,
}: ChampionshipsGridProps) {
  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>Campeonatos em destaque</h2>
      <div className={styles.grid}>
        {campeonatos.map((c) => (
          <div key={c.id} className={styles.card}>
            <div className={styles.cardHeader}>
              <h3 className={styles.cardTitle}>{c.nome}</h3>
              <Badge status={c.status} />
            </div>
            <p className={styles.cardInfo}>🎮 {c.jogo}</p>
            <p className={styles.cardInfo}>👥 {c.equipes} equipes</p>
          </div>
        ))}
      </div>
    </section>
  );
}
