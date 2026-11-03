import Button from "@/components/ui/Button";
import { ClockIcon, GamepadIcon, UsersIcon, ChartIcon, ArrowRightIcon } from "@/components/ui/Icons";
import styles from "./CreateCampeonatoSection.module.css";

export default function CreateCampeonatoSection() {
  return (
    <section className={styles.section}>
      <div className={styles.content}>
        <div className={styles.textContent}>
          <h2 className={styles.title}>Crie seu próprio campeonato</h2>
          <p className={styles.description}>
            Organize um campeonato com seus amigos ou comunidade. Escolha o jogo, 
            defina as regras e comande toda a competição. É simples, rápido e totalmente 
            em suas mãos.
          </p>
          <ul className={styles.features}>
            <li>
              <span className={styles.iconWrapper}>
                <ClockIcon size={20} color="#0ea5e9" />
              </span>
              <span>Setup em minutos</span>
            </li>
            <li>
              <span className={styles.iconWrapper}>
                <GamepadIcon size={20} color="#0ea5e9" />
              </span>
              <span>Suporte a qualquer jogo</span>
            </li>
            <li>
              <span className={styles.iconWrapper}>
                <UsersIcon size={20} color="#0ea5e9" />
              </span>
              <span>Gerencie equipes e jogadores</span>
            </li>
            <li>
              <span className={styles.iconWrapper}>
                <ChartIcon size={20} color="#0ea5e9" />
              </span>
              <span>Acompanhe resultados em tempo real</span>
            </li>
          </ul>
        </div>
        
        <div className={styles.actions}>
          <Button variant="primary">
            Começar agora
          </Button>
          <p className={styles.subtext}>Ou explore campeonatos existentes</p>
          <a href="/campeonatos" className={styles.link}>
            <span>Ver todos os campeonatos</span>
            <ArrowRightIcon size={18} color="#0ea5e9" />
          </a>
        </div>
      </div>

      <div className={styles.decoration}>
        <div className={styles.circle1}></div>
        <div className={styles.circle2}></div>
      </div>
    </section>
  );
}
