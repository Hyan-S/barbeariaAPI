using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

// Vitrine de produtos: o que o cliente ve depois de confirmar o horario, mais a
// pagina do produto com as avaliacoes. Tudo aqui e publico, sem login — por isso
// cada gravacao tem freio proprio, explicado no ponto onde acontece.
public static class VitrineEndpoints
{
    public record NovaAvaliacao(string? Nome, string? Telefone, int Nota, string? Comentario);
    public record NovoPedido(Guid AgendamentoId, Guid ProdutoId, string? Tipo);
    public record ModerarAvaliacao(bool Visivel);

    private const int MaxNome = 120;
    private const int MaxComentario = 600;
    private const int MaxPedidosPorAgendamento = 20;
    private const int TetoAvaliacoes = 100;

    public static void MapVitrine(this IEndpointRouteBuilder app)
    {
        var v = app.MapGroup("/api/vitrine");

        // Estoque sai como sim/nao: quanta pomada tem no armario e informacao da
        // casa, nao da vitrine. O suficiente e saber se da para levar hoje.
        v.MapGet("/produtos", async (AppDbContext db) =>
        {
            var produtos = await db.Produtos.AsNoTracking()
                .Where(p => p.Ativo)
                .OrderBy(p => p.Nome)
                .Select(p => new
                {
                    p.Id,
                    p.Nome,
                    p.Descricao,
                    p.PrecoCentavos,
                    temEstoque = p.Estoque > 0,
                    media = db.Avaliacoes
                        .Where(a => a.ProdutoId == p.Id && a.Visivel)
                        .Average(a => (double?)a.Nota),
                    totalAvaliacoes = db.Avaliacoes.Count(a => a.ProdutoId == p.Id && a.Visivel)
                })
                .ToListAsync();

            // Arredonda aqui, e nao no SQL: o round do Postgres nao aceita double
            // precision, que e o tipo que sai do AVG. A lista de produtos e curta,
            // entao fazer em memoria nao custa — e a nota sai no mesmo formato de
            // uma casa que a pagina do produto usa, em vez de 4.333333333333333.
            return Results.Ok(produtos.Select(p => new
            {
                p.Id,
                p.Nome,
                p.Descricao,
                p.PrecoCentavos,
                p.temEstoque,
                nota = p.media is null ? null : (double?)Math.Round(p.media.Value, 1),
                p.totalAvaliacoes
            }));
        });

        v.MapGet("/produtos/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var produto = await db.Produtos.AsNoTracking()
                .Where(x => x.Id == id && x.Ativo)
                .Select(x => new { x.Id, x.Nome, x.Descricao, x.PrecoCentavos, temEstoque = x.Estoque > 0 })
                .FirstOrDefaultAsync();

            if (produto is null) return Results.NotFound(new { erro = "Produto nao encontrado" });

            var visiveis = await db.Avaliacoes.AsNoTracking()
                .Where(a => a.ProdutoId == id && a.Visivel)
                .OrderByDescending(a => a.CriadaEmUtc)
                .Take(TetoAvaliacoes)
                .Select(a => new { a.Nome, a.Nota, a.Comentario, a.CriadaEmUtc })
                .ToListAsync();

            return Results.Ok(new
            {
                produto.Id,
                produto.Nome,
                produto.Descricao,
                produto.PrecoCentavos,
                produto.temEstoque,
                nota = visiveis.Count == 0 ? null : (double?)Math.Round(visiveis.Average(a => a.Nota), 1),
                totalAvaliacoes = visiveis.Count,
                distribuicao = Enumerable.Range(1, 5).Reverse()
                    .Select(n => new { nota = n, quantas = visiveis.Count(a => a.Nota == n) }),
                avaliacoes = visiveis.Select(a => new
                {
                    nome = NomePublico(a.Nome),
                    a.Nota,
                    a.Comentario,
                    quando = Fuso.ParaLocal(a.CriadaEmUtc)
                })
            });
        });

        v.MapPost("/produtos/{id:guid}/avaliacoes", async (
            Guid id, NovaAvaliacao req, AppDbContext db) =>
        {
            if (req.Nota is < 1 or > 5)
                return Results.BadRequest(new { erro = "Escolha de 1 a 5 estrelas" });

            var nome = Limpar(req.Nome, MaxNome);
            if (nome is null)
                return Results.BadRequest(new { erro = "Informe seu nome" });

            // O telefone nao e verificado — quem avalia pode digitar qualquer um.
            // Ele existe para dois motivos concretos: segurar uma avaliacao por
            // pessoa por produto e dar ao dono como responder quem reclamou.
            var telefone = TelefoneBr.Normalizar(req.Telefone);
            if (telefone is null)
                return Results.BadRequest(new { erro = "Informe um telefone valido com DDD" });

            if (!await db.Produtos.AnyAsync(p => p.Id == id && p.Ativo))
                return Results.NotFound(new { erro = "Produto nao encontrado" });

            // Mesmo bloqueio que vale no WhatsApp: quem a casa barrou nao escreve.
            if (await db.Clientes.AnyAsync(c => c.Telefone == telefone && c.Bloqueado))
                return Results.Json(new { erro = "Nao foi possivel registrar a avaliacao" }, statusCode: 403);

            if (await db.Avaliacoes.AnyAsync(a => a.ProdutoId == id && a.Telefone == telefone))
                return Results.Conflict(new { erro = "Este telefone ja avaliou este produto" });

            db.Avaliacoes.Add(new Avaliacao
            {
                ProdutoId = id,
                Nome = nome,
                Telefone = telefone,
                Nota = req.Nota,
                Comentario = Limpar(req.Comentario, MaxComentario)
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Duas avaliacoes do mesmo telefone chegando juntas: o indice
                // unico barra a segunda e o AnyAsync acima nao pega essa corrida.
                return Results.Conflict(new { erro = "Este telefone ja avaliou este produto" });
            }

            return Results.Ok(new { aviso = "Avaliacao publicada. Obrigado!" });
        }).RequireRateLimiting("vitrine");

        // Quem pode marcar produto e quem tem o Guid do agendamento, devolvido
        // so para o proprio cliente ao confirmar. Nao da para adivinhar um Guid,
        // e o alcance de acertar um seria marcar pomada no corte de outra pessoa
        // — barato o suficiente para nao exigir login. O que fecha o resto:
        // horario ja passado ou cancelado nao aceita, e ha teto por agendamento.
        v.MapPost("/pedidos", async (NovoPedido req, AppDbContext db) =>
        {
            if (!Enum.TryParse<TipoPedido>(req.Tipo, true, out var tipo))
                return Results.BadRequest(new { erro = "Escolha usar ou comprar" });

            var agora = DateTime.UtcNow;

            var existeAgendamento = await db.Agendamentos.AnyAsync(a =>
                a.Id == req.AgendamentoId
                && a.Status != StatusAgendamento.Cancelado
                && a.FimUtc > agora);

            if (!existeAgendamento)
                return Results.NotFound(new { erro = "Agendamento nao encontrado ou ja passou" });

            var produto = await db.Produtos.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == req.ProdutoId && p.Ativo);

            if (produto is null)
                return Results.NotFound(new { erro = "Produto nao encontrado" });

            if (tipo == TipoPedido.Comprar && produto.Estoque <= 0)
                return Results.BadRequest(new { erro = "Produto sem estoque no momento" });

            var pedido = await db.PedidosProduto
                .FirstOrDefaultAsync(x => x.AgendamentoId == req.AgendamentoId
                                          && x.ProdutoId == req.ProdutoId);

            if (pedido is not null)
            {
                pedido.Tipo = tipo;
            }
            else
            {
                var quantos = await db.PedidosProduto
                    .CountAsync(x => x.AgendamentoId == req.AgendamentoId);

                if (quantos >= MaxPedidosPorAgendamento)
                    return Results.BadRequest(new { erro = "Produtos demais marcados neste horario" });

                db.PedidosProduto.Add(new PedidoProduto
                {
                    AgendamentoId = req.AgendamentoId,
                    ProdutoId = req.ProdutoId,
                    Tipo = tipo
                });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { tipo = tipo.ToString() });
        }).RequireRateLimiting("vitrine");

        v.MapDelete("/pedidos", async (Guid agendamentoId, Guid produtoId, AppDbContext db) =>
            await db.PedidosProduto
                .Where(x => x.AgendamentoId == agendamentoId && x.ProdutoId == produtoId)
                .ExecuteDeleteAsync() > 0
                ? Results.Ok()
                : Results.NotFound()).RequireRateLimiting("vitrine");

        // Curadoria. Fica sob a permissao "produtos" — quem cuida do catalogo e
        // quem cuida do que se fala dele. Nao ha fila de aprovacao: o comentario
        // ja esta no ar, e aqui se tira do ar ou se apaga.
        var m = app.MapGroup("/api/gestor/avaliacoes").RequireAuthorization("Produtos");

        m.MapGet("/", async (Guid? produtoId, bool? visivel, int? nota, AppDbContext db) =>
        {
            var q = db.Avaliacoes.AsNoTracking().AsQueryable();

            if (produtoId.HasValue) q = q.Where(a => a.ProdutoId == produtoId.Value);
            if (visivel.HasValue) q = q.Where(a => a.Visivel == visivel.Value);
            if (nota is >= 1 and <= 5) q = q.Where(a => a.Nota == nota.Value);

            var itens = await q
                .OrderByDescending(a => a.CriadaEmUtc)
                .Take(300)
                .Select(a => new
                {
                    a.Id,
                    a.Nome,
                    telefone = TelefoneBr.Formatar(a.Telefone),
                    a.Nota,
                    a.Comentario,
                    a.Visivel,
                    a.ProdutoId,
                    produto = a.Produto!.Nome,
                    quando = Fuso.ParaLocal(a.CriadaEmUtc)
                })
                .ToListAsync();

            // O filtro por produto e o proprio recorte que o dono quer ("o que
            // andam falando da pomada"), entao a lista de produtos vem junto com
            // a contagem — sem isso o painel precisaria de uma segunda chamada e
            // mostraria produto sem nenhum comentario no seletor.
            var produtos = await db.Produtos.AsNoTracking()
                .Select(p => new
                {
                    p.Id,
                    p.Nome,
                    quantas = db.Avaliacoes.Count(a => a.ProdutoId == p.Id)
                })
                .Where(p => p.quantas > 0)
                .OrderBy(p => p.Nome)
                .ToListAsync();

            return Results.Ok(new
            {
                total = await db.Avaliacoes.CountAsync(),
                ocultas = await db.Avaliacoes.CountAsync(a => !a.Visivel),
                produtos,
                itens
            });
        });

        m.MapPut("/{id:guid}", async (Guid id, ModerarAvaliacao req, AppDbContext db) =>
        {
            var avaliacao = await db.Avaliacoes.FirstOrDefaultAsync(x => x.Id == id);
            if (avaliacao is null) return Results.NotFound();

            avaliacao.Visivel = req.Visivel;
            avaliacao.OcultadaEmUtc = req.Visivel ? null : DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        m.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Avaliacoes.Where(x => x.Id == id).ExecuteDeleteAsync() > 0
                ? Results.Ok()
                : Results.NotFound());
    }

    // Na vitrine sai "Rafael M.", nao o nome inteiro: a pessoa escreveu para
    // opinar sobre pomada, nao para publicar o nome completo num site aberto.
    // O painel continua vendo o nome como foi digitado.
    private static string NomePublico(string nome)
    {
        var partes = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length <= 1 ? nome : $"{partes[0]} {partes[^1][0]}.";
    }

    private static string? Limpar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var limpo = new string(texto.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        if (limpo.Length == 0) return null;

        return limpo.Length <= maximo ? limpo : limpo[..maximo];
    }
}
