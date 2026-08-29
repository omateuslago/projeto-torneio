export const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5094";

export const MOCKUP_CAMPEONATOS = [
  {
    id: 1,
    nome: "Valorant Championship",
    jogo: "Valorant",
    equipes: 16,
    status: "Aberto" as const,
  },
  {
    id: 2,
    nome: "CS2 Community Cup",
    jogo: "CS2",
    equipes: 8,
    status: "Em andamento" as const,
  },
  {
    id: 3,
    nome: "EA FC League",
    jogo: "EA FC 27",
    equipes: 32,
    status: "Aberto" as const,
  },
];

export const STATUS_CONFIG = {
  "Aberto": { classe: "statusAberto", label: "Aberto" },
  "Em andamento": { classe: "statusEmAndamento", label: "Em andamento" },
  "Encerrado": { classe: "statusEncerrado", label: "Encerrado" },
} as const;
