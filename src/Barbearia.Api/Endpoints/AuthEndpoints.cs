using System.Security.Claims;
using Barbearia.Api.Seguranca;
using Barbearia.Application.Acesso;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Barbearia.Api.Endpoints;

public static class AuthEndpoints
{
    public record LoginRequest(string Email, string Senha);
    public record TrocaSenhaRequest(string SenhaAtual, string NovaSenha);

    // Limite de senhas erradas POR CONTA. O rate limiter por IP (politica
    // "login") e a primeira barreira, mas ele depende do X-Forwarded-For, que um
    // atacante pode forjar/rotacionar para zerar a contagem. Este conta por e-mail,
    // entao nao importa de quantos IPs venham as tentativas.
    //
    // O contador NAO barra quem chega com a senha certa. Barrando, qualquer pessoa
    // que soubesse o e-mail do dono o manteria fora do painel de proposito: dez
    // senhas erradas a cada quinze minutos e ele nunca mais entra. O que o
    // contador faz depois de estourado e segurar cada palpite errado por um
    // segundo, o que derruba a velocidade de quem esta adivinhando sem nunca
    // trancar a porta de quem tem a chave.
    private const int MaxFalhasPorConta = 10;
    private static readonly TimeSpan JanelaPorConta = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan EsperaAposLimite = TimeSpan.FromSeconds(1);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (
            LoginRequest req, AppDbContext db, IHashDeSenha hash, IServicoDeToken tokens,
            IMemoryCache cache) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();

            // Recusado antes de encostar no banco. Conta sem e-mail existe de verdade —
            // o barbeiro de exemplo do seed nasce assim, e o painel permite deixar uma
            // conta antiga nesse estado ate alguem arrumar — e sem esta guarda um POST
            // com e-mail vazio casaria justamente com essa linha. A senha aleatoria dela
            // nao confere com nada, entao nunca foi um jeito de entrar; o que se evita
            // aqui e a consulta inutil e o contador de falhas na chave vazia.
            if (string.IsNullOrWhiteSpace(email))
                return Results.Json(new { erro = "E-mail ou senha invalidos" }, statusCode: 401);

            var chaveTentativas = "login-falhas:" + email;

            var usuario = await db.Barbeiros
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.Ativo);

            // Conferir mesmo sem conta: o ConferirEmVao gasta o mesmo tempo de um
            // BCrypt de verdade. Sem ele, e-mail que nao existe responde na hora e
            // e-mail que existe demora o custo do hash — o relogio contaria quem e
            // da casa, mesmo com a mensagem sendo a mesma.
            var senhaCerta = usuario is null
                ? hash.ConferirEmVao(req.Senha ?? "")
                : hash.Conferir(req.Senha ?? "", usuario.SenhaHash);

            if (!senhaCerta)
            {
                // Caixa mutavel: incrementar sem recriar a entrada preserva a
                // expiracao da janela (contada desde a primeira falha).
                var contador = cache.GetOrCreate(chaveTentativas, e =>
                {
                    e.AbsoluteExpirationRelativeToNow = JanelaPorConta;
                    return new int[1];
                })!;
                contador[0]++;

                if (contador[0] < MaxFalhasPorConta)
                    return Results.Json(new { erro = "E-mail ou senha invalidos" }, statusCode: 401);

                await Task.Delay(EsperaAposLimite);

                return Results.Json(
                    new { erro = "Muitas tentativas para esta conta. Aguarde alguns minutos." },
                    statusCode: 429);
            }

            // Senha certa limpa o contador, inclusive quando ele estava estourado.
            cache.Remove(chaveTentativas);

            // senhaCerta so e true com usuario carregado; o compilador nao ve isso.
            if (hash.PrecisaRegerar(usuario!.SenhaHash))
            {
                usuario.SenhaHash = hash.Gerar(req.Senha!);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new
            {
                token = tokens.GerarParaBarbeiro(usuario),
                id = usuario.Id,
                nome = usuario.Nome,
                perfil = usuario.Perfil.ToString(),
                precisaTrocarSenha = usuario.PrecisaTrocarSenha,
                permissoes = new
                {
                    servicos = usuario.PodeGerenciarServicos,
                    produtos = usuario.PodeGerenciarProdutos,
                    clientes = usuario.PodeGerenciarClientes,
                    dashboard = usuario.PodeVerDashboard
                }
            });
        }).RequireRateLimiting("login");

        g.MapPost("/trocar-senha", async (
            TrocaSenhaRequest req, ClaimsPrincipal user, AppDbContext db,
            IHashDeSenha hash, IServicoDeToken tokens, IMemoryCache cache) =>
        {
            var id = user.FindFirstValue("sub");
            if (!Guid.TryParse(id, out var usuarioId)) return Results.Unauthorized();

            var usuario = await db.Barbeiros.FirstOrDefaultAsync(x => x.Id == usuarioId && x.Ativo);
            if (usuario is null) return Results.Unauthorized();

            if (!hash.Conferir(req.SenhaAtual ?? "", usuario.SenhaHash))
                return Results.BadRequest(new { erro = "Senha atual incorreta" });

            var nova = req.NovaSenha ?? "";
            if (nova.Length < 8)
                return Results.BadRequest(new { erro = "A nova senha precisa de no minimo 8 caracteres" });

            if (hash.Conferir(nova, usuario.SenhaHash))
                return Results.BadRequest(new { erro = "A nova senha precisa ser diferente da atual" });

            usuario.SenhaHash = hash.Gerar(nova);
            usuario.PrecisaTrocarSenha = false;

            // Trocar a senha derruba as sessoes antigas — inclusive a de quem
            // estivesse com a senha velha em outro aparelho. O token devolvido logo
            // abaixo nasce no mesmo segundo do selo, entao a propria pessoa segue
            // dentro sem precisar entrar de novo.
            GuardaDeSessao.CortarSessoes(usuario, cache);

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                token = tokens.GerarParaBarbeiro(usuario),
                id = usuario.Id,
                nome = usuario.Nome,
                perfil = usuario.Perfil.ToString(),
                precisaTrocarSenha = false,
                permissoes = new
                {
                    servicos = usuario.PodeGerenciarServicos,
                    produtos = usuario.PodeGerenciarProdutos,
                    clientes = usuario.PodeGerenciarClientes,
                    dashboard = usuario.PodeVerDashboard
                }
            });
        }).RequireAuthorization();

        g.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.FindFirstValue("sub"),
            nome = user.FindFirstValue("name"),
            perfil = user.FindFirstValue("role"),
            precisaTrocarSenha = user.HasClaim("trocar_senha", "1"),
            permissoes = user.FindAll("perm").Select(c => c.Value)
        })).RequireAuthorization();
    }
}
