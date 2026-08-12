using System.Security.Claims;
using Barbearia.Application.Acesso;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class AuthEndpoints
{
    public record LoginRequest(string Email, string Senha);
    public record TrocaSenhaRequest(string SenhaAtual, string NovaSenha);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (
            LoginRequest req, AppDbContext db, IHashDeSenha hash, IServicoDeToken tokens) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();

            var usuario = await db.Barbeiros
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.Ativo);

            // Mensagem unica: dizer "email nao existe" entrega quais contas existem.
            if (usuario is null || !hash.Conferir(req.Senha ?? "", usuario.SenhaHash))
                return Results.Json(new { erro = "E-mail ou senha invalidos" }, statusCode: 401);

            // Hash gravado com custo antigo continua caro para conferir em todo login.
            // Como a senha em texto so existe aqui, este e o unico ponto onde da para
            // reescrever o hash com o custo atual — acontece uma vez por conta.
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

        // Sem policy: e o unico caminho aberto para quem tem senha provisoria.
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

            // Token novo, ja sem a trava.
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
