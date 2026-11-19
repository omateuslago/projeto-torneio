#!/usr/bin/env bash
# Sobe o ambiente de desenvolvimento completo:
# PostgreSQL + scheduler + migrations + API (Docker) e frontend Angular local.
# Uso: ./scripts/dev.sh   (Ctrl+C encerra o frontend; o Docker continua)
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

echo "==> Subindo PostgreSQL, scheduler, migrations e API..."
docker compose up -d --build --wait db scheduler api
echo "==> API disponível em http://localhost:5094"

frontend_lock_hash="$(sha256sum frontend/package-lock.json | cut -d ' ' -f 1)"
frontend_install_stamp="frontend/node_modules/.package-lock.sha256"

if [ ! -d frontend/node_modules ] || [ ! -f "$frontend_install_stamp" ] || [ "$(cat "$frontend_install_stamp")" != "$frontend_lock_hash" ]; then
  echo "==> Instalando dependências do frontend..."
  (cd frontend && npm ci)
  printf '%s\n' "$frontend_lock_hash" > "$frontend_install_stamp"
fi

frontend_pid=""
cleanup() {
  echo
  echo "==> Encerrando frontend; os containers Docker continuam rodando."
  if [ -n "$frontend_pid" ]; then
    kill "$frontend_pid" 2>/dev/null || true
  fi
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "==> Iniciando frontend em http://localhost:4200"
(cd frontend && exec npm start) &
frontend_pid=$!

wait "$frontend_pid"
