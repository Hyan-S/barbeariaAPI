using System.Security.Claims;
using Barbearia.Application.Acesso;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Barbearia.Api.Endpoints;

public static class AuthEndpoints
{
    public record LoginRequest(string Email, string Senha);
    public record TrocaSenhaRequest(string SenhaAtual, string NovaSenha);

    // Limite de tentativas de login POR CONTA. O rate limiter por IP (politica
    // "login") e a primeira barreira, mas ele depende do X-Forwarded-For, que um
    // atacante pode forjar/rotacionar para zerar a contagem. Este limite por email
    // fecha esse furo: nao importa de quantos IPs venham, uma conta so aceita
    // MaxFalhasPorConta senhas erradas dentro da JanelaPorConta. So conta falhas;
    // um login bem-sucedido limpa o contador.
    private const int MaxFalhasPorConta = 10;
    private static readonly TimeSpan JanelaPorConta = TimeSpan.FromMinutes(15);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (
            LoginRequest req, AppDbContext db, IHashDeSenha hash, IServicoDeToken tokens,
            IMemoryCache cache) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();

            var chaveTentativas = "login-falhas:" + email;
            var falhas = cache.Get<int[]>(chaveTentativas);
            if (falhas is not null && falhas[0] >= MaxFalhasPorConta)
                return Results.Json(
                    new { erro = "Muitas tentativas para esta conta. Aguarde alguns minutos." },
                    statusCode: 429);

            var usuario = await db.Barbeiros
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.Ativo);

            if (usuario is null || !hash.Conferir(req.Senha ?? "", usuario.SenhaHash))
            {
                // Caixa mutavel: incrementar sem recriar a entrada preserva a
                // expiracao da janela (contada desde a primeira falha).
                var contador = cache.GetOrCreate(chaveTentativas, e =>
                {
                    e.AbsoluteExpirationRelativeToNow = JanelaPorConta;
                    return new int[1];
                })!;
                contador[0]++;

                return Results.Json(new { erro = "E-mail ou senha invalidos" }, statusCode: 401);
            }

            cache.Remove(chaveTentativas);

            if (hash.PrecisaRegerar(usuario.SenhaHash))
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
            IHashDeSenha hash, IServicoDeToken tokens) =>
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
