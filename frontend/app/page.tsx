import Header from "./components/Header";
import styles from "./page.module.css";

const campeonatos = [
  { id: 1, nome: "Valorant Championship", jogo: "Valorant", equipes: 16, status: "Aberto" },
  { id: 2, nome: "CS2 Community Cup", jogo: "CS2", equipes: 8, status: "Em andamento" },
  { id: 3, nome: "EA FC League", jogo: "EA FC 27", equipes: 32, status: "Aberto" },
];

const statusClass: Record<string, string> = {
  "Aberto": styles.statusAberto,
  "Em andamento": styles.statusEmAndamento,
};

export default function Home() {
  return (
    <div className={styles.page}>
      <Header />

      <section className={styles.hero}>
        <p className={styles.heroLabel}>Plataforma de Campeonatos Online</p>
        <h1 className={styles.heroTitle}>
          Compita.{" "}
          <span className={styles.heroHighlight}>Supere.</span>{" "}
          Vença.
        </h1>
        <p className={styles.heroSubtitle}>
          Descubra, participe e organize campeonatos de jogos online. Mostre seu talento e conquiste seu lugar no topo.
        </p>
        <div className={styles.heroActions}>
          <button className={styles.btnPrimary}>Explorar campeonatos</button>
          <button className={styles.btnSecondary}>Criar campeonato</button>
        </div>
      </section>

      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>Campeonatos em destaque</h2>
        <div className={styles.grid}>
          {campeonatos.map((c) => (
            <div key={c.id} className={styles.card}>
              <div className={styles.cardHeader}>
                <h3 className={styles.cardTitle}>{c.nome}</h3>
                <span className={statusClass[c.status]}>{c.status}</span>
              </div>
              <p className={styles.cardInfo}>🎮 {c.jogo}</p>
              <p className={styles.cardInfo}>👥 {c.equipes} equipes</p>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
