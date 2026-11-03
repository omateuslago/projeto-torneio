export interface Campeonato {
  id: number;
  nome: string;
  jogo: string;
  modalidade?: string;
  equipes: number;
  status: "Aberto" | "Em andamento" | "Encerrado";
  descricao?: string;
  dataInicio?: string;
  dataFim?: string;
}

export interface StatusConfig {
  classe: string;
  label: string;
}
