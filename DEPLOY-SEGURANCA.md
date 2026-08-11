# Deploy no Render (front + back juntos)

O front e servido pela propria API (mesma origem), entao e **um unico Web Service** no
Render. Banco de dados fica numa nuvem separada. A app **recusa subir em producao** com
config de exemplo — isso e proposital.

---

## 1. Banco de dados na nuvem

> **Requisito que quebra o deploy se ignorado:** o banco PRECISA suportar a extensao
> `btree_gist`. A trava que impede dois clientes no mesmo horario e uma constraint
> EXCLUDE que depende dela. Sem a extensao, a primeira migration falha e a app nao sobe.

Provedores gratuitos que suportam:

| Provedor | Free | Observacao |
|---|---|---|
| **[Neon](https://neon.tech)** (recomendado) | 0,5 GB, **nao expira** | suporta btree_gist |
| Supabase | 0,5 GB | suporta btree_gist |
| Postgres do proprio Render | expira em ~30 dias | evitar |

Passos no Neon:
1. Criar projeto -> copiar a connection string.
2. Converter para o formato Npgsql (adicionar `SSL Mode=Require`):
   ```
   Host=ep-xxx.neon.tech;Database=neondb;Username=xxx;Password=xxx;SSL Mode=Require;Trust Server Certificate=true
   ```

As migrations rodam sozinhas na primeira subida (inclui `CREATE EXTENSION btree_gist`,
a constraint anti-duplicacao, e cria o admin do seed).

---

## 2. Subir no Render

1. `git push` do repositorio para o GitHub.
2. No Render: **New -> Blueprint**, aponte para o repo. Ele le o `render.yaml` e ja
   cria o Web Service Docker com health check em `/health`.
   (Alternativa: **New -> Web Service -> Docker**, sem o blueprint.)

---

## 3. Variaveis de ambiente (no painel do Render)

O `render.yaml` ja gera o `Jwt__Secret` e fixa `ASPNETCORE_ENVIRONMENT=Production`.
As marcadas `sync:false` voce preenche no painel:

| Variavel | Valor |
|---|---|
| `ConnectionStrings__Postgres` | string do Neon, formato Npgsql, com SSL |
| `ADMIN_SENHA` | senha forte do primeiro admin (sem ela a app nao sobe) |
| `ADMIN_EMAIL` | e-mail do primeiro admin |
| `App__UrlPublica` | `https://SEU-DOMINIO` (ou a URL `.onrender.com`) |
| `App__OrigensPermitidas` | mesma URL — so importa se um dia separar o front |

`PORT` o Render injeta sozinho.

---

## 4. Dominio proprio (opcional)

1. Registrar em [registro.br](https://registro.br) (`.com.br`) ou Namecheap/Cloudflare (`.com`).
2. Render -> Settings -> **Custom Domains** -> Add. Ele mostra os registros DNS.
3. No painel do registrador, criar os registros CNAME/A que o Render pediu.
4. Voltar no Render e **Verify**. O SSL (Let's Encrypt) e automatico.
5. **Atualizar `App__UrlPublica` e `App__OrigensPermitidas`** para o dominio, e redeploy.
   (Sem isso, o link do WhatsApp continua apontando pro `.onrender.com`.)

---

## 5. Depois do primeiro deploy

1. Abra a URL, entre com `ADMIN_EMAIL` / `ADMIN_SENHA`.
2. Em **Sistema**, confira que o banco conectou (senha aparece mascarada).
3. **Keep-alive**: o plano free hiberna em 15 min (cold start ~50s, que estoura o
   timeout do webhook do WhatsApp). Aponte um cron gratuito (cron-job.org) para
   `/health` a cada 10 min. 720h/mes cabe nas 750h do plano free.

---

## O que a app faz sozinha em producao

- HTTPS + HSTS + redirect (TLS termina no proxy do Render; `ForwardedHeaders` cuida disso).
- Swagger desligado (nao expoe o mapa da API).
- Rate limit: login 8/min por IP, agendamento anonimo 12/min por IP.
- Headers: CSP restrita, X-Frame-Options DENY, nosniff, no-referrer.
- JWT: valida emissor/audiencia/validade/algoritmo (`alg=none` recusado).
- **Fail-fast**: recusa subir se `Jwt__Secret`, `ConnectionStrings__Postgres` ou
  `ADMIN_SENHA` estiverem com valor de exemplo/ausentes.

## Nunca versione
`.env`, `_conversas/`, `.txt` em `wwwroot`, ou `appsettings.json` com segredo real.
Ja cobertos pelo `.gitignore` e `.dockerignore`.
