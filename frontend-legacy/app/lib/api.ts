const API_URL =  "http://localhost:5094/";


export interface Campeonato {
id: number;
nome: string;
modalidade: string;
}



export async function getCampeonatos(): Promise<Campeonato[]> {
const res = await fetch(`${API_URL}/campeonatos`, { cache: "no-store"});
if (!res.ok) throw new Error("ERRO AO BUSCAR CAMEPONATOS");
return res.json();
}