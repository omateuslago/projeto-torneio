import { Campeonato } from "@/types";
import { API_BASE_URL } from "@/constants";

export async function getCampeonatos(): Promise<Campeonato[]> {
  try {
    const res = await fetch(`${API_BASE_URL}/campeonatos`, {
      cache: "no-store",
    });

    if (!res.ok) {
      throw new Error(`Erro ao buscar campeonatos: ${res.statusText}`);
    }

    return res.json();
  } catch (error) {
    console.error("Erro na requisição de campeonatos:", error);
    throw error;
  }
}

export async function getCampeonatoById(id: number): Promise<Campeonato> {
  try {
    const res = await fetch(`${API_BASE_URL}/campeonatos/${id}`, {
      cache: "no-store",
    });

    if (!res.ok) {
      throw new Error(`Campeonato não encontrado: ${res.statusText}`);
    }

    return res.json();
  } catch (error) {
    console.error(`Erro ao buscar campeonato ${id}:`, error);
    throw error;
  }
}
