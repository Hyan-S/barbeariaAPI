using System.Security.Claims;
using Barbearia.Api.Seguranca;
using Barbearia.Application.Acesso;
using Barbearia.Application.Agendamentos;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Barbearia.Api.Endpoints;

// Acesso do cliente: cadastro, login, os proprios horarios e o cancelamento.
//
// O nome de usuario e o telefone, nao um e-mail. Ele ja era a chave unica do
// cliente (indice unico em clientes.telefone) e ja e o dado que a pessoa informa
// para agendar — pedir um e-mail so para logar criaria um segundo identificador
// para a mesma pessoa e abriria caminho para cadastro duplicado.
public static class ClienteEndpoints
{
    public record CadastroRequest(string? Nome, string? Telefone, string? Senha);
    public record LoginRequest(string? Telefone, string? Senha);
    public record NovoAgendamento(Guid ServicoId, DateTime InicioUtc, Guid? BarbeiroId);
    public record TrocaSenhaRequest(string? SenhaAtual, string? NovaSenha);

    private const int MinSenha = 6;
    private const int MaxSenha = 72;   // limite do BCrypt: acima disso ele trunca calado
    private const int MaxNome = 120;

    // Mesmo freio por conta do login do painel: o limite por IP e a primeira
    // barreira, mas ele depende do X-Forwarded-For, que da para forjar. Este conta
    // por telefone, entao nao importa de quantos IPs venham as tentativas.
    //
    // Como no painel, o contador nao barra quem chega com a senha certa — barrando,
    // qualquer um trancaria o cliente fora da conta so errando a senha dele dez
    // vezes. Estourado, ele segura cada palpite errado por um segundo.
    private const int MaxFalhasPorConta = 10;
    private static readonly TimeSpan JanelaPorConta = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan EsperaAposLimite = TimeSpan.FromSeconds(1);

    public static void MapCliente(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cliente");

        g.MapPost("/cadastro", async (
            CadastroRequest req, AppDbContext db, IHashDeSenha hash, IServicoDeToken tokens) =>
        {
            var telefone = TelefoneBr.Normalizar(req.Telefone);
            if (telefone is null)
                return Results.BadRequest(new { erro = "Informe um telefone valido com DDD" });

            var nome = (req.Nome ?? "").Trim();
            if (nome.Length < 2)
                return Results.BadRequest(new { erro = "Informe seu nome" });
            if (nome.Length > MaxNome)
                return Results.BadRequest(new { erro = "O nome pode ter no maximo " + MaxNome + " caracteres" });

            var senha = req.Senha ?? "";
            if (senha.Length < MinSenha)
                return Results.BadRequest(new { erro = "A senha precisa de no minimo " + MinSenha + " caracteres" });
            if (senha.Length > MaxSenha)
                return Results.BadRequest(new { erro = "A senha pode ter no maximo " + MaxSenha + " caracteres" });

            var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Telefone == telefone);

            if (cliente is null)
            {
                cliente = new Cliente { Telefone = telefone, Nome = nome };
                db.Clientes.Add(cliente);
            }
            else if (cliente.SenhaHash is not null)
            {
                // Ja existe acesso para este telefone. Nao da para "recadastrar" por
                // cima, senao qualquer um trocaria a senha de qualquer cliente so
                // sabendo o numero dele.
                return Results.Conflict(new { erro = "Ja existe um acesso com este telefone. Entre com a sua senha." });
            }
            else
            {
                // Cliente que nasceu no balcao ou no WhatsApp e nunca teve acesso:
                // este e o primeiro acesso dele, e o nome informado agora prevalece.
                cliente.Nome = nome;
            }

            if (cliente.Bloqueado)
                return Results.Json(new { erro = "Este telefone esta bloqueado. Fale com a barbearia." }, statusCode: 403);

            cliente.SenhaHash = hash.Gerar(senha);
            cliente.SenhaDefinidaEmUtc = DateTime.UtcNow;

            // So vale token emitido daqui para frente. Importa no acesso que nasce
            // depois de um "zerar acesso" no painel: nada de antes volta a valer.
            cliente.TokensValidosDesdeUtc = GuardaDeSessao.Segundo(DateTime.UtcNow);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Dois cadastros no mesmo telefone ao mesmo tempo: o indice unico
                // decide, e quem perdeu recebe a mesma resposta do caminho normal.
                return Results.Conflict(new { erro = "Ja existe um acesso com este telefone. Entre com a sua senha." });
            }

            return Results.Ok(Sessao(cliente, tokens));
        }).RequireRateLimiting("login");

        g.MapPost("/login", async (
            LoginRequest req, AppDbContext db, IHashDeSenha hash, IServicoDeToken tokens,
            IMemoryCache cache) =>
        {
            var telefone = TelefoneBr.Normalizar(req.Telefone);
            if (telefone is null)
                return Results.Json(new { erro = "Telefone ou senha invalidos" }, statusCode: 401);

            var chave = "cliente-falhas:" + telefone;

            var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Telefone == telefone);

            // Telefone sem conta gasta o mesmo tempo de um BCrypt de verdade: sem
            // isso a mensagem seria a mesma, mas o relogio entregaria quem e cliente
            // da casa — a resposta voltaria na hora em vez de esperar o hash.
            var senhaCerta = cliente?.SenhaHash is null
                ? hash.ConferirEmVao(req.Senha ?? "")
                : hash.Conferir(req.Senha ?? "", cliente.SenhaHash);

            if (!senhaCerta)
            {
                var contador = cache.GetOrCreate(chave, e =>
                {
                    e.AbsoluteExpirationRelativeToNow = JanelaPorConta;
                    return new int[1];
                })!;
                contador[0]++;

                // Mesma resposta para telefone inexistente, telefone sem acesso e
                // senha errada: distinguir contaria a um curioso quem e cliente da casa.
                if (contador[0] < MaxFalhasPorConta)
                    return Results.Json(new { erro = "Telefone ou senha invalidos" }, statusCode: 401);

                await Task.Delay(EsperaAposLimite);

                return Results.Json(
                    new { erro = "Muitas tentativas para este telefone. Aguarde alguns minutos." },
                    statusCode: 429);
            }

            if (cliente!.Bloqueado)
                return Results.Json(new { erro = "Este telefone esta bloqueado. Fale com a barbearia." }, statusCode: 403);

            // Senha certa limpa o contador, inclusive quando ele estava estourado.
            cache.Remove(chave);

            if (hash.PrecisaRegerar(cliente.SenhaHash!))
            {
                cliente.SenhaHash = hash.Gerar(req.Senha!);
                await db.SaveChangesAsync();
            }

            return Results.Ok(Sessao(cliente, tokens));
        }).RequireRateLimiting("login");

        // Daqui para baixo, so com token de cliente.
        var s = app.MapGroup("/api/cliente").RequireAuthorization("Cliente");

        s.MapGet("/me", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var cliente = await Atual(user, db);
            if (cliente is null) return Results.Unauthorized();

            var agora = DateTime.UtcNow;

            var agendamentos = await db.Agendamentos.AsNoTracking()
                .Include(a => a.Servico).Include(a => a.Barbeiro)
                .Where(a => a.ClienteId == cliente.Id
                            && a.InicioUtc > agora
                            && a.Status != StatusAgendamento.Cancelado)
                .OrderBy(a => a.InicioUtc)
                .ToListAsync();

            return Results.Ok(new
            {
                nome = cliente.Nome,
                telefone = TelefoneBr.Formatar(cliente.Telefone),
                agendamentos = agendamentos.Select(a => new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    servico = a.Servico!.Nome,
                    barbeiro = a.Barbeiro!.Nome,
                    precoCentavos = a.Servico.PrecoCentavos
                })
            });
        });

        // Sem isto o cliente nao tem como trocar a propria senha, e o unico caminho
        // seria a barbearia zerar o acesso dele.
        s.MapPost("/senha", async (
            TrocaSenhaRequest req, ClaimsPrincipal user, AppDbContext db,
            IHashDeSenha hash, IServicoDeToken tokens) =>
        {
            var cliente = await Atual(user, db);
            if (cliente is null) return Results.Unauthorized();

            if (!hash.Conferir(req.SenhaAtual ?? "", cliente.SenhaHash!))
                return Results.BadRequest(new { erro = "Senha atual incorreta" });

            var nova = req.NovaSenha ?? "";
            if (nova.Length < MinSenha)
                return Results.BadRequest(new { erro = "A senha precisa de no minimo " + MinSenha + " caracteres" });
            if (nova.Length > MaxSenha)
                return Results.BadRequest(new { erro = "A senha pode ter no maximo " + MaxSenha + " caracteres" });
            if (hash.Conferir(nova, cliente.SenhaHash!))
                return Results.BadRequest(new { erro = "A nova senha precisa ser diferente da atual" });

            cliente.SenhaHash = hash.Gerar(nova);
            cliente.SenhaDefinidaEmUtc = DateTime.UtcNow;

            // Derruba quem estava logado nesta conta em outro aparelho. O token
            // devolvido abaixo nasce no mesmo segundo do selo, entao quem trocou a
            // senha segue dentro.
            cliente.TokensValidosDesdeUtc = GuardaDeSessao.Segundo(DateTime.UtcNow);

            await db.SaveChangesAsync();

            return Results.Ok(Sessao(cliente, tokens));
        }).RequireRateLimiting("login");

        s.MapPost("/agendamentos", async (
            NovoAgendamento req, ClaimsPrincipal user, AppDbContext db, AgendamentoService servico) =>
        {
            var cliente = await Atual(user, db);
            if (cliente is null) return Results.Unauthorized();

            var resultado = await servico.CriarAsync(
                cliente.Id, req.ServicoId, req.InicioUtc, req.BarbeiroId, OrigemAgendamento.Web);

            if (!resultado.Sucesso)
                return Results.Json(new
                {
                    erro = Mensagem(resultado.Tipo),
                    sugestoes = resultado.Sugestoes?.Select(x => new
                    {
                        x.InicioUtc, hora = x.HoraFormatada, x.BarbeiroNome
                    })
                }, statusCode: 409);

            var a = resultado.Agendamento!;

            var barbeiro = await db.Barbeiros.AsNoTracking()
                .Where(b => b.Id == a.BarbeiroId)
                .Select(b => b.Nome)
                .FirstOrDefaultAsync();

            return Results.Ok(new { a.Id, inicio = Fuso.ParaLocal(a.InicioUtc), a.BarbeiroId, barbeiro });
        }).RequireRateLimiting("agendar");

        s.MapPost("/agendamentos/{id:guid}/cancelar", async (
            Guid id, ClaimsPrincipal user, AppDbContext db, AgendamentoService servico) =>
        {
            var cliente = await Atual(user, db);
            if (cliente is null) return Results.Unauthorized();

            // O id do cliente vem do token, nao do corpo: nao da para cancelar o
            // horario de outra pessoa passando o Guid dela.
            return await servico.CancelarAsync(id, cliente.Id, true)
                ? Results.Ok()
                : Results.BadRequest(new { erro = "Nao foi possivel cancelar (muito proximo do horario)" });
        });
    }

    // Le o cliente do banco a cada chamada, e nao so do token: se a barbearia
    // bloquear alguem, o token que ja esta na mao da pessoa para de valer na hora.
    private static async Task<Cliente?> Atual(ClaimsPrincipal user, AppDbContext db)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(id, out var clienteId)) return null;

        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == clienteId);

        if (cliente is null || cliente.Bloqueado || cliente.SenhaHash is null) return null;

        // Token emitido antes do selo esta morto: e o que faz trocar a senha
        // desconectar os outros aparelhos na hora.
        return GuardaDeSessao.Emissao(user) < cliente.TokensValidosDesdeUtc ? null : cliente;
    }

    private static object Sessao(Cliente cliente, IServicoDeToken tokens) => new
    {
        token = tokens.GerarParaCliente(cliente),
        id = cliente.Id,
        nome = cliente.Nome,
        telefone = TelefoneBr.Formatar(cliente.Telefone)
    };

    private static string Mensagem(ResultadoTipo tipo) => tipo switch
    {
        ResultadoTipo.HorarioIndisponivel => "Esse horario acabou de ser ocupado",
        ResultadoTipo.ForaDaAntecedencia => "Esse horario ja esta muito em cima",
        ResultadoTipo.ForaDaJanelaDeAgenda => "A agenda ainda nao abriu para essa data",
        ResultadoTipo.LimiteDeAgendamentosAtingido => "Voce ja tem agendamentos abertos demais",
        ResultadoTipo.ServicoInvalido => "Servico indisponivel",
        _ => "Nao foi possivel agendar"
    };
}
