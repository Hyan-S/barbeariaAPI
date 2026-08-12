using Barbearia.Domain;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class PermissoesEndpoints
{
    public record AlteracaoPermissao(Guid BarbeiroId, string Chave, bool Concedida);

    private static readonly (string Chave, string Nome, string Descricao, bool GestorTemSempre)[] Catalogo =
    [
        ("servicos", "Servicos", "Cadastrar servicos, duracao, preco e quem executa", true),
        ("produtos", "Produtos", "Cadastrar produtos e controlar estoque", true),
        ("clientes", "Clientes", "Ver, editar e bloquear clientes", true),
        ("dashboard", "Dashboard", "Faturamento, ranking de profissionais e relatorios", false)
    ];

    public static void MapPermissoes(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        g.MapGet("/permissoes", async (AppDbContext db) =>
        {
            var pessoas = await db.Barbeiros.AsNoTracking()
                .Where(x => x.Ativo)
                .OrderBy(x => x.Perfil).ThenBy(x => x.Nome)
                .Select(x => new
                {
                    x.Id, x.Nome, x.Email,
                    perfil = x.Perfil.ToString(),
                    concedidas = new
                    {
                        servicos = x.PodeGerenciarServicos,
                        produtos = x.PodeGerenciarProdutos,
                        clientes = x.PodeGerenciarClientes,
                        dashboard = x.PodeVerDashboard
                    }
                })
                .ToListAsync();

            return Results.Ok(new
            {
                permissoes = Catalogo.Select(p => new
                {
                    chave = p.Chave, nome = p.Nome, descricao = p.Descricao, gestorTemSempre = p.GestorTemSempre
                }),
                pessoas
            });
        });

        g.MapPost("/permissoes", async (AlteracaoPermissao req, AppDbContext db) =>
        {
            if (!Catalogo.Any(p => p.Chave == req.Chave))
                return Results.BadRequest(new { erro = "Permissao desconhecida" });

            var pessoa = await db.Barbeiros.FirstOrDefaultAsync(x => x.Id == req.BarbeiroId);
            if (pessoa is null) return Results.NotFound();

            if (pessoa.Perfil == Perfil.Admin)
                return Results.BadRequest(new { erro = "Admin ja tem acesso total; nao ha o que conceder" });

            switch (req.Chave)
            {
                case "servicos": pessoa.PodeGerenciarServicos = req.Concedida; break;
                case "produtos": pessoa.PodeGerenciarProdutos = req.Concedida; break;
                case "clientes": pessoa.PodeGerenciarClientes = req.Concedida; break;
                case "dashboard": pessoa.PodeVerDashboard = req.Concedida; break;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new { aplicadoNoProximoLogin = true });
        });
    }
}
