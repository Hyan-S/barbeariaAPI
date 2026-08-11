# Barbearia API

API de agendamento em .NET 10 + PostgreSQL.

## Rodar local

```bash
# 1. Banco (a 5432 ja esta ocupada por um Postgres nativo nesta maquina)
docker start barbearia-pg
# ou, na primeira vez:
# docker run -d --name barbearia-pg -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=barbearia -p 5433:5432 postgres:16-alpine

# 2. API
dotnet run --project src/Barbearia.Api
```

Swagger: http://localhost:5080/swagger

As migrations e o seed (1 barbeiro, 2 servicos, expediente seg-sab 09-12 e 13-19)
rodam sozinhos na subida.

## Endpoints

| Metodo | Rota | O que faz |
|---|---|---|
| GET | `/health` | ping |
| GET | `/api/servicos` | servicos ativos |
| GET | `/api/barbeiros` | barbeiros ativos |
| GET | `/api/disponibilidade?data=&servicoId=&barbeiroId=` | horarios livres do dia |
| POST | `/api/agendamentos` | agenda; 409 com sugestoes se ocupado |
| GET | `/api/agendamentos?telefone=` | agendamentos do cliente |
| POST | `/api/agendamentos/{id}/cancelar` | cancela |

## Decisoes que importam

**Anti-agendamento-duplicado.** Nao esta no C#, esta no Postgres:

```sql
EXCLUDE USING gist ("BarbeiroId" WITH =, tstzrange("InicioUtc","FimUtc") WITH &&)
WHERE ("Status" <> 2)
```

Checagem em codigo perde a corrida entre duas requisicoes simultaneas. Testado com
10 requisicoes paralelas no mesmo horario: 1 gravou, 9 levaram 409.

**Horarios nao ficam no banco.** Sao calculados sob demanda:
expediente − agendamentos − bloqueios − passado.

**Telefone e a identidade do cliente.** `TelefoneBr.Normalizar` resolve o nono digito
(o `wa_id` do WhatsApp as vezes vem sem ele). `5511987654321`, `551187654321` e
`(11) 98765-4321` caem no mesmo cadastro.

**Tudo em UTC no banco**, convertido para America/Sao_Paulo so na borda.

## Deploy no Render

Dockerfile validado localmente (build + run + banco + fuso -03:00, zero erro no log).

1. **Banco** — criar um Postgres free em [neon.tech](https://neon.tech) (0.5 GB, nao expira).
   Copiar a connection string e converter para o formato Npgsql:
   `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require`
2. **Web Service** no Render → conectar o repo → Runtime **Docker** → plano Free.
3. Variaveis de ambiente:

| Variavel | Valor |
|---|---|
| `ConnectionStrings__Postgres` | string do Neon |
| `App__UrlPublica` | URL do front |
| `App__OrigensPermitidas` | URL do front |
| `Jwt__Secret` | 32+ caracteres aleatorios |

`PORT` o Render injeta sozinho. As migrations rodam na subida.

4. **Keep-alive**: o free hiberna em 15 min (~50s de cold start, o que estoura o
   timeout do webhook da Meta). Apontar um cron gratuito (cron-job.org) para
   `/health` a cada 10 min. 720h/mes cabe nas 750h do plano.

## Ainda nao feito

- WhatsApp: o codigo existe (`InterpretadorMensagem`, `ConversaService`,
  `WhatsAppClient`, `ValidadorAssinatura`, fila em background) mas **nao esta
  ligado no Program.cs nem testado**.
- Login do painel (JWT) e magic link: services prontos, sem endpoint.
- Dockerfile e deploy no Render.
