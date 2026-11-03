import { Header, HeroSection, CreateCampeonatoSection } from "@/components";
import styles from "./page.module.css";

export default function Home() {
  return (
    <div className={styles.page}>
      <Header />
      <main className={styles.main}>
        <HeroSection />
        <CreateCampeonatoSection />
      </main>
    </div>
  );
}
