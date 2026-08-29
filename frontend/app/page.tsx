import { Header, HeroSection, ChampionshipsGrid } from "@/components";
import { MOCKUP_CAMPEONATOS } from "@/constants";
import styles from "./page.module.css";

export default function Home() {
  return (
    <div className={styles.page}>
      <Header />
      <main className={styles.main}>
        <HeroSection />
        <ChampionshipsGrid campeonatos={MOCKUP_CAMPEONATOS} />
      </main>
    </div>
  );
}
