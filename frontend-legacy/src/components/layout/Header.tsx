import styles from "./Header.module.css";

export default function Header() {
  return (
    <header className={styles.header}>
      <div className={styles.container}>
        <span className={styles.logo}>higis denaif</span>

        <nav className={styles.nav}>
          <a href="/" className={styles.navLink}>
            Início
          </a>
          <a href="/campeonatos" className={styles.navLink}>
            Campeonatos
          </a>
          <a href="/ranking" className={styles.navLink}>
            Ranking
          </a>
        </nav>

        <button className={styles.button}>Entrar</button>
      </div>
    </header>
  );
}
