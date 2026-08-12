using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class CatalogoEndpoints
{
    public record ProdutoRequest(string Nome, string? Descricao, int PrecoCentavos, int Estoque, bool Ativo);
    public record ClienteRequest(string Nome, bool Bloqueado);

    public static void MapCatalogo(this IEndpointRouteBuilder app)
    {
        var p = app.MapGroup("/api/produtos").RequireAuthorization("Produtos");

        p.MapGet("/", async (AppDbContext db) =>
            await db.Produtos.AsNoTracking().OrderBy(x => x.Nome).ToListAsync());

        p.MapPost("/", async (ProdutoRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Nome))
                return Results.BadRequest(new { erro = "Informe o nome do produto" });

            if (req.PrecoCentavos < 0 || req.Estoque < 0)
                return Results.BadRequest(new { erro = "Preco e estoque nao podem ser negativos" });

            var produto = new Produto
            {
                Nome = req.Nome.Trim(),
                Descricao = req.Descricao?.Trim(),
                PrecoCentavos = req.PrecoCentavos,
                Estoque = req.Estoque,
                Ativo = req.Ativo
            };

            db.Produtos.Add(produto);
            await db.SaveChangesAsync();
            return Results.Ok(new { produto.Id });
        });

        p.MapPut("/{id:guid}", async (Guid id, ProdutoRequest req, AppDbContext db) =>
        {
            var produto = await db.Produtos.FirstOrDefaultAsync(x => x.Id == id);
            if (produto is null) return Results.NotFound();

            if (req.PrecoCentavos < 0 || req.Estoque < 0)
                return Results.BadRequest(new { erro = "Preco e estoque nao podem ser negativos" });

            produto.Nome = req.Nome.Trim();
            produto.Descricao = req.Descricao?.Trim();
            produto.PrecoCentavos = req.PrecoCentavos;
            produto.Estoque = req.Estoque;
            produto.Ativo = req.Ativo;

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        p.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Produtos.Where(x => x.Id == id).ExecuteDeleteAsync() > 0
                ? Results.Ok() : Results.NotFound());

        var c = app.MapGroup("/api/clientes").RequireAuthorization("Clientes");

        c.MapGet("/", async (string? busca, AppDbContext db) =>
        {
            var q = db.Clientes.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim();
                var telefone = TelefoneBr.Normalizar(termo);

                q = telefone is not null
                    ? q.Where(x => x.Telefone == telefone)
                    : q.Where(x => EF.Functions.ILike(x.Nome, $"%{termo}%"));
            }

            var lista = await q
                .OrderBy(x => x.Nome)
                .Take(200)
                .Select(x => new
                {
                    x.Id, x.Nome, x.Bloqueado, x.CriadoEmUtc,
                    telefone = TelefoneBr.Formatar(x.Telefone),
                    agendamentos = x.Agendamentos.Count(a => a.Status != StatusAgendamento.Cancelado)
                })
                .ToListAsync();

            return Results.Ok(lista);
        });

        c.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (cliente is null) return Results.NotFound();

            var historico = await db.Agendamentos.AsNoTracking()
                .Include(a => a.Servico).Include(a => a.Barbeiro)
                .Where(a => a.ClienteId == id)
                .OrderByDescending(a => a.InicioUtc)
                .Take(20)
                .ToListAsync();

            return Results.Ok(new
            {
                cliente.Id, cliente.Nome, cliente.Bloqueado,
                telefone = TelefoneBr.Formatar(cliente.Telefone),
                historico = historico.Select(a => new
                {
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    servico = a.Servico!.Nome,
                    barbeiro = a.Barbeiro!.Nome,
                    status = a.Status.ToString()
                })
            });
        });

        c.MapPut("/{id:guid}", async (Guid id, ClienteRequest req, AppDbContext db) =>
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id);
            if (cliente is null) return Results.NotFound();

            cliente.Nome = (req.Nome ?? "").Trim();
            cliente.Bloqueado = req.Bloqueado;

            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
