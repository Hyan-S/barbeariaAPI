using Barbearia.Api.Seguranca;
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

            // O cadastro recusava nome vazio, mas a edicao nao: dava para salvar por cima e
            // apagar o nome de um produto que ja estava na vitrine. Nome nulo estourava o
            // Trim mais abaixo e virava 500, sem dizer o que faltava.
            if (string.IsNullOrWhiteSpace(req.Nome))
                return Results.BadRequest(new { erro = "Informe o nome do produto" });

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

        // Tira o produto do banco de vez.
        //
        // Antes esta rota apagava qualquer produto sem olhar nada, e era pior do que
        // parece: pedidos_produto aponta para produtos em Cascade, entao excluir um
        // produto que ja foi vendido levava embora o item vendido de atendimentos
        // fechados — o caixa daqueles dias mudava sozinho, para menos, sem aviso.
        p.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var produto = await db.Produtos.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Ativo })
                .FirstOrDefaultAsync();

            if (produto is null) return Results.NotFound();

            if (produto.Ativo)
                return RegraDeExclusao.Recusa(
                    "Este produto ainda esta ativo. Desative, confira que ele saiu da vitrine, "
                    + "e ai exclua.");

            var vendas = await db.PedidosProduto.CountAsync(x => x.ProdutoId == id && x.Vendido);

            if (vendas > 0)
                return RegraDeExclusao.Recusa(
                    "Este produto foi vendido em "
                    + RegraDeExclusao.Contagem(vendas, "atendimento", "atendimentos")
                    + ", e o valor da venda esta guardado nesse item. Apagar o produto apagaria a "
                    + "venda junto e mudaria o caixa daquele dia. Deixe inativo: sai da vitrine e "
                    + "o que foi vendido continua contado.");

            // O que cai junto, e de proposito: as avaliacoes da vitrine e os pedidos que
            // ainda eram so recado ("quer levar") em atendimento aberto. Nenhum dos dois
            // e registro de dinheiro. Devolvo a contagem para o painel poder dizer o que
            // foi, em vez de a pessoa descobrir depois que os comentarios sumiram.
            var avaliacoes = await db.Avaliacoes.CountAsync(x => x.ProdutoId == id);
            var pedidos = await db.PedidosProduto.CountAsync(x => x.ProdutoId == id);

            await db.Produtos.Where(x => x.Id == id).ExecuteDeleteAsync();

            return Results.Ok(new { avaliacoes, pedidos });
        });

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
                    agendamentos = x.Agendamentos.Count(a => a.Status != StatusAgendamento.Cancelado),
                    temAcesso = x.SenhaHash != null
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
                temAcesso = cliente.SenhaHash != null,
                acessoDesde = cliente.SenhaDefinidaEmUtc is null
                    ? (DateTime?)null : Fuso.ParaLocal(cliente.SenhaDefinidaEmUtc.Value),
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

        // Tira o cliente do banco de vez.
        //
        // Aqui o "desligado" nao e Ativo, e Bloqueado: e o estado em que o cliente
        // continua existindo mas nao consegue mais marcar. Serve de rascunho da
        // exclusao do mesmo jeito — bloqueia, ve se falta alguma coisa, e ai apaga.
        //
        // Vale para o cadastro duplicado, o telefone digitado errado e o registro de
        // teste. Quem tem historico nao sai, e nem deveria: e a ficha do cliente.
        c.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var cliente = await db.Clientes.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Bloqueado })
                .FirstOrDefaultAsync();

            if (cliente is null) return Results.NotFound();

            if (!cliente.Bloqueado)
                return RegraDeExclusao.Recusa(
                    "Este cliente nao esta bloqueado. Marque \"Bloqueado\", salve, e ai exclua — "
                    + "bloquear tem volta, excluir nao tem.");

            // Conta agendamento cancelado tambem: a lista de clientes esconde os
            // cancelados na coluna, mas a linha continua no banco e o Restrict do
            // agendamento nao deixaria o cliente sair de baixo dela.
            var agendamentos = await db.Agendamentos.CountAsync(a => a.ClienteId == id);

            if (agendamentos > 0)
                return RegraDeExclusao.Recusa(
                    "Este cliente tem "
                    + RegraDeExclusao.Contagem(agendamentos, "agendamento", "agendamentos")
                    + " no historico, contando os cancelados, e a agenda precisa deles para dizer "
                    + "quem foi atendido. Bloqueado ele ja nao marca mais nada.");

            if (await db.Barbeiros.AnyAsync(x => x.ClienteId == id))
                return RegraDeExclusao.Recusa(
                    "Esta pessoa tambem tem cadastro de funcionario, ligado a este cliente. "
                    + "Exclua o funcionario primeiro.");

            // Vao junto, por Cascade: o link de acesso do WhatsApp (magic_links) e o
            // estado da conversa. Os dois sao rascunho de sessao, nao historico.
            await db.Clientes.Where(x => x.Id == id).ExecuteDeleteAsync();
            return Results.Ok();
        });

        // Cliente que esquece a senha nao tinha saida nenhuma: o cadastro recusa
        // porque ja existe acesso, e nao ha e-mail nem SMS para mandar link. Zerar
        // apaga so a senha — nome, telefone e historico ficam onde estao — e a pessoa
        // cadastra de novo pela tela de sempre. O token que ela tivesse na mao morre
        // na hora, porque o /api/cliente recusa quem esta sem hash.
        c.MapPost("/{id:guid}/zerar-acesso", async (Guid id, AppDbContext db) =>
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id);
            if (cliente is null) return Results.NotFound();

            if (cliente.SenhaHash is null)
                return Results.BadRequest(new { erro = "Este cliente ainda nao criou acesso" });

            cliente.SenhaHash = null;
            cliente.SenhaDefinidaEmUtc = null;

            // Sobe o selo tambem. Apagar o hash ja derruba a sessao agora, mas quando
            // a pessoa recadastrar o hash volta a existir — e sem o selo o token antigo
            // (o do celular perdido, por exemplo) voltaria a valer junto com ele.
            cliente.TokensValidosDesdeUtc = GuardaDeSessao.Segundo(DateTime.UtcNow);

            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
