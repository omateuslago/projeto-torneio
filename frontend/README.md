# Frontend Angular

Aplicação oficial em Angular 22, standalone, com signals, Reactive Forms e HttpClient.
O Next.js foi preservado sem alterações em `../frontend-legacy/`.

```bash
npm ci
npm start
```

Abra http://localhost:4200. A API deve estar em http://127.0.0.1:5094.
A URL base fica em `src/environments/environment.ts`. O proxy de desenvolvimento
em `proxy.conf.json` encaminha `/api/**` ao backend. Em produção, configure o
servidor para encaminhar `/api` à API e servir `index.html` nas rotas da aplicação.

```bash
npm run build
npm test -- --watch=false
```

Veja o README da raiz para iniciar PostgreSQL e backend.
