import Header from "./components/Header";

const campeonatos = [
  { id: 1, nome: "Valorant Championship", jogo: "Valorant", equipes: 16, status: "Aberto" },
  { id: 2, nome: "CS2 Community Cup", jogo: "CS2", equipes: 8, status: "Em andamento" },
  { id: 3, nome: "EA FC League", jogo: "EA FC 25", equipes: 32, status: "Aberto" },
];

const statusColor: Record<string, string> = {
  "Aberto": "text-emerald-400 bg-emerald-400/10",
  "Em andamento": "text-amber-400 bg-amber-400/10",
};

export default function Home() {
  return (
    <div className="min-h-screen bg-[#0d0d1a] text-white">
      <Header />

   <section className="max-w-6xl mx-auto px-4 sm:px-6 py-24 sm:py-32 text-center">
        <p className="text-violet-400 text-sm font-semibold tracking-widest uppercase mb-4">
          Plataforma de Campeonatos Online
        </p>
        <h1 className="text-4xl sm:text-6xl font-extrabold tracking-tight leading-tight mb-6">
          Compita.{" "}
          <span className="text-violet-500">Supere.</span>{" "}
          Vença.
        </h1>
        <p className="text-zinc-400 text-lg sm:text-xl max-w-2xl mx-auto mb-10">
          Descubra, participe e organize campeonatos de jogos online. Mostre seu talento e conquiste seu lugar no topo.
        </p>
        <div className="flex flex-col sm:flex-row gap-4 justify-center">
          <button className="bg-violet-600 hover:bg-violet-500 text-white font-semibold px-8 py-3 rounded-full transition-colors">
            Explorar campeonatos
          </button>
          <button className="border border-white/20 hover:border-white/40 text-white font-semibold px-8 py-3 rounded-full transition-colors">
            Criar campeonato
          </button>
        </div>
      </section>

      
      <section className="max-w-6xl mx-auto px-4 sm:px-6 pb-24">
        <h2 className="text-xl font-bold mb-6 text-zinc-200">Campeonatos em destaque</h2>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {campeonatos.map((c) => (
            <div
              key={c.id}
              className="bg-white/5 border border-white/10 rounded-2xl p-6 hover:border-violet-500/50 transition-colors"
            >
              <div className="flex items-start justify-between mb-4">
                <h3 className="font-semibold text-white">{c.nome}</h3>
                <span className={`text-xs font-medium px-2 py-1 rounded-full ${statusColor[c.status]}`}>
                  {c.status}
                </span>
              </div>
              <p className="text-zinc-400 text-sm mb-1">🎮 {c.jogo}</p>
              <p className="text-zinc-400 text-sm">👥 {c.equipes} equipes</p>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
