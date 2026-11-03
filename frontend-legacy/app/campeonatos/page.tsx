"use client";

import { useState, useMemo } from "react";
import { Header, ChampionshipsGrid } from "@/components";
import { MOCKUP_CAMPEONATOS } from "@/constants";
import { Campeonato } from "@/types";
import styles from "./page.module.css";

export default function Campeonatos() {
  const [busca, setBusca] = useState("");
  const [jogo, setJogo] = useState("");

  // Pega lista única de jogos do mockup
  const jogos = useMemo(
    () => [...new Set(MOCKUP_CAMPEONATOS.map((c) => c.jogo))],
    []
  );

  // Filtra baseado em busca e jogo
  const filtrados: Campeonato[] = useMemo(() => {
    return MOCKUP_CAMPEONATOS.filter((c) => {
      const matchBusca =
        c.nome.toLowerCase().includes(busca.toLowerCase()) ||
        c.jogo.toLowerCase().includes(busca.toLowerCase());
      const matchJogo = !jogo || c.jogo === jogo;
      return matchBusca && matchJogo;
    });
  }, [busca, jogo]);

  return (
    <>
      <Header />
      <main className={styles.container}>
        <div className={styles.header}>
          <h1 className={styles.title}>Campeonatos</h1>
          <p className={styles.subtitle}>
            Explore e participe dos melhores campeonatos de esports
          </p>
        </div>

        <div className={styles.filtros}>
          <input
            type="text"
            placeholder="Buscar campeonato ou jogo..."
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            className={styles.input}
          />
          <select
            value={jogo}
            onChange={(e) => setJogo(e.target.value)}
            className={styles.select}
          >
            <option value="">Todos os jogos</option>
            {jogos.map((j) => (
              <option key={j} value={j}>
                {j}
              </option>
            ))}
          </select>
        </div>

        <div className={styles.resultados}>
          <p className={styles.count}>
            {filtrados.length} campeonato{filtrados.length !== 1 ? "s" : ""}{" "}
            encontrado{filtrados.length !== 1 ? "s" : ""}
          </p>
          <ChampionshipsGrid campeonatos={filtrados} />
        </div>
      </main>
    </>
  );
}
