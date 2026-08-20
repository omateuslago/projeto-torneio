export default function Header() {
  return (
    <header className="w-full border-b border-white/10 bg-[#0d0d1a]/90 backdrop-blur-sm sticky top-0 z-50">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 h-16 flex items-center justify-between">
        <span className="text-white font-bold text-lg tracking-widest uppercase">
          higis denaif
        </span>

        <nav className="hidden md:flex items-center gap-8 text-sm text-zinc-400">
          <a href="#" className="hover:text-white transition-colors">Início</a>
          <a href="#" className="hover:text-white transition-colors">Campeonatos</a>
          <a href="#" className="hover:text-white transition-colors">Ranking</a>
        </nav>

        <button className="bg-violet-600 hover:bg-violet-500 text-white text-sm font-medium px-5 py-2 rounded-full transition-colors">
          Entrar
        </button>
      </div>
    </header>
  );
}
