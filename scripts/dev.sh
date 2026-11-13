#!/usr/bin/env bash
# Sobe o ambiente de desenvolvimento completo:
# PostgreSQL + scheduler (Docker), API .NET e frontend Angular.
# Uso: ./scripts/dev.sh   (Ctrl+C encerra API e frontend; o Docker continua)
set -euo pipefail

cd "$(dirname "$0")/.."

if [ ! -f .env ]; then
  echo "Arquivo .env não encontrado. Rode: cp .env.example .env" >&2
  exit 1
fi

set -a
. ./.env
set +a

# Evita o prompt interativo de analytics do Angular CLI (o frontend roda em background).
export NG_CLI_ANALYTICS=false

export ConnectionStrings__Tournaments="Host=localhost;Database=tournaments;Username=tournaments;Password=${POSTGRES_PASSWORD}"

echo "==> Subindo PostgreSQL e scheduler (Docker)..."
docker compose up -d --build --wait db scheduler

echo "==> Aplicando migrations..."
dotnet run --project backend/Projeto-Campeonato.Api --no-launch-profile -- --migrate

frontend_lock_hash="$(sha256sum frontend/package-lock.json | cut -d ' ' -f 1)"
frontend_install_stamp="frontend/node_modules/.package-lock.sha256"

if [ ! -d frontend/node_modules ] || [ ! -f "$frontend_install_stamp" ] || [ "$(cat "$frontend_install_stamp")" != "$frontend_lock_hash" ]; then
  echo "==> Instalando dependências do frontend..."
  (cd frontend && npm ci)
  printf '%s\n' "$frontend_lock_hash" > "$frontend_install_stamp"
fi

pids=()
cleanup() {
  echo
  echo "==> Encerrando API e frontend..."
  for pid in "${pids[@]}"; do
    kill "$pid" 2>/dev/null || true
  done
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "==> Iniciando API em http://localhost:5094 (Swagger em /swagger)"
dotnet run --project backend/Projeto-Campeonato.Api --launch-profile http &
pids+=($!)

echo "==> Iniciando frontend em http://localhost:4200"
(cd frontend && exec npm start) &
pids+=($!)

# Encerra tudo se qualquer um dos dois cair.
wait -n
