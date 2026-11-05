# Scheduling Engine

Serviço interno stateless: recebe dados pelo HTTP, resolve hard constraints com
Google OR-Tools CP-SAT e retorna uma proposta. Não acessa PostgreSQL.

Python 3.12, na raiz do repositório:

```bash
python3 -m venv scheduler/.venv
scheduler/.venv/bin/pip install -r scheduler/requirements.txt
cd scheduler
.venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8000
```

No Windows, utilize `scheduler\.venv\Scripts\python.exe -m pip`,
`python.exe -m uvicorn` e `python.exe -m pytest` no lugar dos executáveis de `bin`.

Em outro terminal, dentro de `scheduler`:

```bash
.venv/bin/python -m pytest -q
.venv/bin/ruff check app tests
```

- Health: GET http://localhost:8000/health
- Contrato OpenAPI: http://localhost:8000/docs
- Cálculo: POST http://localhost:8000/schedule/generate

O request é documentado em `app/models.py` e no OpenAPI. O prazo CP-SAT é de cinco
segundos, um worker e seed zero. Status UNKNOWN vira timeout, nunca infeasible.
`optimal` significa viabilidade provada neste modelo sem função objetivo; não
promete equilíbrio ou qualidade esportiva. Não existe algoritmo guloso substituindo
o solver. IDs do contrato são UUIDs, timestamps exigem offset e todos os recursos
informam suas próprias janelas de disponibilidade.

Veja [contratos e decisões](../docs/tournament-autopilot.md).

## Docker

`docker compose up --build` na raiz inclui este serviço e seu healthcheck. A API
usa `http://scheduler:8000`; o Angular continua local. Não há dependência de
startup da API no scheduler, para manter operações manuais disponíveis.

Em ambientes cloud com proxy TLS, o Dockerfile aceita a CA como segredo temporário:

```bash
docker build --secret id=proxy_ca,src=/etc/ssl/certs/ca-certificates.crt \
  -t projeto-torneio-scheduler scheduler
```

Execute esse build na raiz. Em redes comuns, a CA extra não é necessária. A CA não
é gravada na imagem e a verificação TLS permanece habilitada.
