using System.Security.Claims;
using Barbearia.Application;
using Barbearia.Application.Agendamentos;
using Barbearia.Application.Disponibilidade;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class GestorEndpoints
{
    public record ItemVendidoRequest(Guid ProdutoId, int Quantidade);

    public record FechamentoRequest(
        int? ValorCobradoCentavos, string? FormaPagamento, List<ItemVendidoRequest>? Produtos);

    // Teto de digitacao. Nao e regra de negocio: e para um zero a mais nao virar
    // R$ 400.000 no caixa do dia sem ninguem perceber.
    private const int TetoValorCentavos = 5_000_000;
    private const int TetoQuantidade = 99;

    public record ServicoRequest(
        string Nome, int DuracaoMinutos, int PrecoCentavos, bool Ativo, Guid[]? BarbeiroIds);

    private static async Task VincularAsync(AppDbContext db, Guid servicoId, Guid[]? barbeiroIds)
    {
        if (barbeiroIds is null || barbeiroIds.Length == 0) return;

        var validos = await db.Barbeiros.AsNoTracking()
            .Where(b => barbeiroIds.Contains(b.Id) && b.Ativo && b.Atende)
            .Select(b => b.Id)
            .ToListAsync();

        foreach (var barbeiroId in validos)
            db.BarbeiroServicos.Add(new BarbeiroServico { ServicoId = servicoId, BarbeiroId = barbeiroId });
    }
    public record ExpedienteRequest(Guid BarbeiroId, int DiaSemana, string HoraInicio, string HoraFim);
    public record CopiarFuncionamento(Guid BarbeiroId);
    public record BloqueioRequest(Guid BarbeiroId, DateTime InicioLocal, DateTime FimLocal, string? Motivo);

    public record ReagendarRequest(DateTime NovoInicioUtc, Guid? BarbeiroId);

    public record AgendamentoPeloPainel(
        Guid? ClienteId, string? Telefone, string? Nome,
        Guid ServicoId, DateTime InicioUtc, Guid? BarbeiroId, string? Observacao);

    public static void MapGestor(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gestor").RequireAuthorization("Gestao");

        var painel = app.MapGroup("/api/gestor").RequireAuthorization("Painel");

        const int TetoBusca = 3000;
        const int TetoExibicao = 500;

        painel.MapGet("/agenda", async (
            DateOnly de, DateOnly ate, string? busca, Guid? barbeiroId, Guid? servicoId,
            string? status, string? origem, int? horaDe, int? horaAte, AppDbContext db) =>
        {
            if (ate < de) (de, ate) = (ate, de);

            var inicioUtc = Fuso.ParaUtc(de.ToDateTime(TimeOnly.MinValue));
            var fimUtc = Fuso.ParaUtc(ate.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var q = db.Agendamentos.AsNoTracking()
                .Include(a => a.Cliente).Include(a => a.Servico).Include(a => a.Barbeiro)
                .Where(a => a.InicioUtc >= inicioUtc && a.InicioUtc < fimUtc);

            if (barbeiroId.HasValue) q = q.Where(a => a.BarbeiroId == barbeiroId.Value);
            if (servicoId.HasValue) q = q.Where(a => a.ServicoId == servicoId.Value);

            if (Enum.TryParse<StatusAgendamento>(status, out var st))
                q = q.Where(a => a.Status == st);

            if (Enum.TryParse<OrigemAgendamento>(origem, out var og))
                q = q.Where(a => a.Origem == og);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim();
                var telefone = TelefoneBr.Normalizar(termo);

                q = telefone is not null
                    ? q.Where(a => a.Cliente!.Telefone == telefone)
                    : q.Where(a => EF.Functions.ILike(a.Cliente!.Nome, $"%{termo}%"));
            }

            var lista = await q.OrderBy(a => a.InicioUtc).Take(TetoBusca).ToListAsync();

            if (horaDe.HasValue || horaAte.HasValue)
            {
                var h1 = horaDe ?? 0;
                var h2 = horaAte ?? 23;
                lista = lista.Where(a =>
                {
                    var h = Fuso.ParaLocal(a.InicioUtc).Hour;
                    return h >= h1 && h <= h2;
                }).ToList();
            }

            var ativos = lista.Where(a => a.Status != StatusAgendamento.Cancelado).ToList();

            var exibir = lista.Take(TetoExibicao).ToList();

            // Produtos que o cliente marcou na vitrine, buscados de uma vez para
            // a pagina inteira: dentro do Select seria uma consulta por linha.
            var ids = exibir.Select(a => a.Id).ToList();

            var pedidos = (await db.PedidosProduto.AsNoTracking()
                    .Where(p => ids.Contains(p.AgendamentoId))
                    .Select(p => new
                    {
                        p.AgendamentoId,
                        p.ProdutoId,
                        produto = p.Produto!.Nome,
                        // Vendido leva o preco congelado na venda; o resto e so
                        // intencao do cliente, e ai vale a tabela de hoje.
                        precoCentavos = p.Vendido && p.PrecoCentavosNaVenda != null
                            ? p.PrecoCentavosNaVenda.Value
                            : p.Produto.PrecoCentavos,
                        tipo = p.Tipo,
                        p.Vendido,
                        p.Quantidade
                    })
                    .ToListAsync())
                .GroupBy(p => p.AgendamentoId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => new
                          {
                              p.ProdutoId, p.produto, p.precoCentavos,
                              tipo = p.tipo.ToString(), p.Vendido, p.Quantidade
                          })
                          .OrderBy(p => p.produto)
                          .ToList());

            return Results.Ok(new
            {
                total = lista.Count,
                truncado = lista.Count >= TetoBusca,
                resumo = new
                {
                    confirmados = lista.Count(a => a.Status == StatusAgendamento.Confirmado),
                    concluidos = lista.Count(a => a.Status == StatusAgendamento.Concluido),
                    cancelados = lista.Count(a => a.Status == StatusAgendamento.Cancelado),
                    minutos = ativos.Sum(a => (int)(a.FimUtc - a.InicioUtc).TotalMinutes),
                    receitaCentavos = ativos.Sum(a => (long)a.PrecoCentavos),
                    fechados = ativos.Count(a => a.EstaFechado),
                    caixaCentavos = ativos.Sum(a => (long)(a.ValorCobradoCentavos ?? 0))
                },
                itens = exibir.Select(a => new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    fim = Fuso.ParaLocal(a.FimUtc),
                    duracao = (int)(a.FimUtc - a.InicioUtc).TotalMinutes,
                    cliente = a.Cliente!.Nome,
                    telefone = TelefoneBr.Formatar(a.Cliente.Telefone),
                    servico = a.Servico!.Nome,
                    a.ServicoId,
                    precoCentavos = a.PrecoCentavos,
                    barbeiro = a.Barbeiro!.Nome,
                    a.BarbeiroId,
                    status = a.Status.ToString(),
                    origem = a.Origem.ToString(),
                    a.Observacao,
                    fechado = a.EstaFechado,
                    a.ValorCobradoCentavos,
                    forma = a.FormaPagamento?.ToString(),
                    fechadoEm = a.FechadoEmUtc is null ? (DateTime?)null : Fuso.ParaLocal(a.FechadoEmUtc.Value),
                    produtos = pedidos.GetValueOrDefault(a.Id)
                })
            });
        });

        painel.MapGet("/agendamentos/{id:guid}/grade", async (
            Guid id, DateOnly data, AppDbContext db, DisponibilidadeService disponibilidade) =>
        {
            var a = await db.Agendamentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();

            var slots = await disponibilidade.ObterGradeParaMoverAsync(data, a.ServicoId, id);

            return Results.Ok(slots.Select(s => new
            {
                s.BarbeiroId, s.BarbeiroNome, s.InicioUtc, s.Livre, hora = s.HoraFormatada
            }));
        });

        painel.MapPost("/agendamentos/{id:guid}/reagendar", async (
            Guid id, ReagendarRequest req, AppDbContext db,
            DisponibilidadeService disponibilidade, IDetectorDeConflito detector) =>
        {
            var a = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();

            if (!a.EstaAtivo)
                return Results.BadRequest(new { erro = "Agendamento cancelado nao pode ser remarcado" });

            var servico = await db.Servicos.AsNoTracking().FirstAsync(s => s.Id == a.ServicoId);
            var diaLocal = DateOnly.FromDateTime(Fuso.ParaLocal(req.NovoInicioUtc));

            var slots = await disponibilidade.ObterGradeParaMoverAsync(
                diaLocal, a.ServicoId, id, req.BarbeiroId);

            var slot = slots.FirstOrDefault(s => s.InicioUtc == req.NovoInicioUtc && s.Livre);
            if (slot is null)
                return Results.Conflict(new { erro = "Esse horario nao esta livre" });

            var anterior = Fuso.ParaLocal(a.InicioUtc);

            a.InicioUtc = slot.InicioUtc;
            a.FimUtc = slot.InicioUtc.AddMinutes(servico.DuracaoMinutos);
            a.BarbeiroId = slot.BarbeiroId;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex) when (detector.EhConflitoDeHorario(ex))
            {
                return Results.Conflict(new { erro = "Esse horario acabou de ser ocupado" });
            }

            return Results.Ok(new
            {
                de = anterior,
                para = Fuso.ParaLocal(a.InicioUtc),
                barbeiro = slot.BarbeiroNome
            });
        });

        painel.MapPost("/agendamentos/{id:guid}/cancelar", async (Guid id, AgendamentoService servico) =>
            await servico.CancelarAsync(id, null, false) ? Results.Ok() : Results.NotFound());

        // Fechamento do atendimento: o unico lugar do sistema onde se registra que o
        // dinheiro entrou. O valor cobrado vale para o servico; produto entra por
        // fora, com o preco congelado no item, para o caixa poder separar depois
        // quanto veio de servico e quanto veio de prateleira.
        painel.MapPost("/agendamentos/{id:guid}/fechar", async (
            Guid id, FechamentoRequest req, ClaimsPrincipal user, AppDbContext db) =>
        {
            var a = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();

            if (a.Status == StatusAgendamento.Cancelado)
                return Results.BadRequest(new { erro = "Agendamento cancelado nao pode ser fechado" });

            if (a.EstaFechado)
                return Results.Conflict(new { erro = "Este atendimento ja foi fechado" });

            if (a.InicioUtc > DateTime.UtcNow)
                return Results.BadRequest(new { erro = "O atendimento ainda nao comecou" });

            if (!Enum.TryParse<FormaPagamento>(req.FormaPagamento, out var forma))
                return Results.BadRequest(new { erro = "Informe a forma de pagamento" });

            // Sem valor informado, cobra o preco combinado no agendamento. Zero e
            // valido: cortesia acontece, e registrar zero e diferente de nao fechar.
            var cobrado = req.ValorCobradoCentavos ?? a.PrecoCentavos;
            if (cobrado < 0 || cobrado > TetoValorCentavos)
                return Results.BadRequest(new { erro = "Valor cobrado invalido" });

            var pedidos = await db.PedidosProduto
                .Where(x => x.AgendamentoId == id)
                .ToListAsync();

            var itens = req.Produtos ?? [];

            if (itens.Any(x => x.Quantidade < 1 || x.Quantidade > TetoQuantidade))
                return Results.BadRequest(new { erro = "Quantidade invalida" });

            if (itens.Select(x => x.ProdutoId).Distinct().Count() != itens.Count)
                return Results.BadRequest(new { erro = "Produto repetido na lista" });

            var ids = itens.Select(x => x.ProdutoId).ToList();
            var produtos = await db.Produtos.Where(x => ids.Contains(x.Id)).ToListAsync();

            if (produtos.Count != ids.Count)
                return Results.BadRequest(new { erro = "Produto informado nao existe" });

            long totalProdutos = 0;

            foreach (var item in itens)
            {
                var produto = produtos.First(x => x.Id == item.ProdutoId);
                var pedido = pedidos.FirstOrDefault(x => x.ProdutoId == item.ProdutoId);

                if (pedido is null)
                {
                    // O cliente nao pediu na vitrine, levou na hora.
                    pedido = new PedidoProduto
                    {
                        AgendamentoId = id, ProdutoId = produto.Id, Tipo = TipoPedido.Comprar
                    };
                    db.PedidosProduto.Add(pedido);
                    pedidos.Add(pedido);
                }

                pedido.Vendido = true;
                pedido.Quantidade = item.Quantidade;
                pedido.PrecoCentavosNaVenda = produto.PrecoCentavos;

                // Estoque nao vai a negativo: o numero do painel pode estar
                // desatualizado, e travar a venda por causa disso atrapalharia o
                // atendimento em vez de ajudar.
                produto.Estoque = Math.Max(0, produto.Estoque - item.Quantidade);

                totalProdutos += (long)produto.PrecoCentavos * item.Quantidade;
            }

            // O que o cliente marcou na vitrine e nao levou fica registrado como nao
            // vendido, em vez de continuar parecendo pedido em aberto.
            foreach (var sobra in pedidos.Where(x => !ids.Contains(x.ProdutoId)))
            {
                sobra.Vendido = false;
                sobra.PrecoCentavosNaVenda = null;
            }

            a.ValorCobradoCentavos = cobrado;
            a.FormaPagamento = forma;
            a.FechadoEmUtc = DateTime.UtcNow;
            a.Status = StatusAgendamento.Concluido;

            if (Guid.TryParse(user.FindFirstValue("sub"), out var quem)) a.FechadoPorId = quem;

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                servicoCentavos = cobrado,
                produtosCentavos = totalProdutos,
                totalCentavos = cobrado + totalProdutos,
                descontoCentavos = Math.Max(0, a.PrecoCentavos - cobrado),
                forma = forma.ToString(),
                fechadoEm = Fuso.ParaLocal(a.FechadoEmUtc!.Value)
            });
        });

        // Reabrir apaga um registro de caixa, entao fica na Gestao e nao no painel:
        // corrigir o proprio erro de digitacao e uma coisa, poder desfazer o caixa de
        // qualquer dia e outra.
        g.MapPost("/agendamentos/{id:guid}/reabrir", async (Guid id, AppDbContext db) =>
        {
            var a = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();

            if (!a.EstaFechado)
                return Results.BadRequest(new { erro = "Este atendimento nao esta fechado" });

            var vendidos = await db.PedidosProduto
                .Where(x => x.AgendamentoId == id && x.Vendido)
                .ToListAsync();

            if (vendidos.Count > 0)
            {
                var idsVendidos = vendidos.Select(x => x.ProdutoId).ToList();
                var produtos = await db.Produtos.Where(x => idsVendidos.Contains(x.Id)).ToListAsync();

                foreach (var pedido in vendidos)
                {
                    // Devolve ao estoque o que a venda tirou.
                    var produto = produtos.FirstOrDefault(x => x.Id == pedido.ProdutoId);
                    if (produto is not null) produto.Estoque += pedido.Quantidade;

                    pedido.Vendido = false;
                    pedido.PrecoCentavosNaVenda = null;
                }
            }

            a.ValorCobradoCentavos = null;
            a.FormaPagamento = null;
            a.FechadoEmUtc = null;
            a.FechadoPorId = null;
            a.Status = StatusAgendamento.Confirmado;

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        painel.MapPost("/agendamentos", async (
            AgendamentoPeloPainel req, AgendamentoService servico, AppDbContext db) =>
        {
            Cliente? cliente = null;

            if (req.ClienteId.HasValue)
                cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == req.ClienteId.Value);

            if (cliente is null)
            {
                var telefone = TelefoneBr.Normalizar(req.Telefone);
                if (telefone is null)
                    return Results.BadRequest(new { erro = "Escolha um cliente ou informe um telefone valido" });

                cliente = await servico.ObterOuCriarClienteAsync(telefone, req.Nome);
            }

            var resultado = await servico.CriarAsync(
                cliente.Id, req.ServicoId, req.InicioUtc, req.BarbeiroId,
                OrigemAgendamento.Painel, req.Observacao, comoStaff: true);

            if (resultado.Sucesso)
            {
                var a = resultado.Agendamento!;
                return Results.Ok(new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    cliente = cliente.Nome,
                    telefone = TelefoneBr.Formatar(cliente.Telefone)
                });
            }

            return Results.Json(new
            {
                erro = resultado.Tipo switch
                {
                    ResultadoTipo.HorarioIndisponivel => "Esse horario acabou de ser ocupado",
                    ResultadoTipo.ForaDaJanelaDeAgenda => "Data alem do limite configurado da agenda",
                    ResultadoTipo.ClienteBloqueado => "Cliente bloqueado",
                    ResultadoTipo.ServicoInvalido => "Servico indisponivel",
                    _ => "Nao foi possivel agendar"
                },
                sugestoes = resultado.Sugestoes?.Select(s => new
                {
                    s.InicioUtc, hora = s.HoraFormatada, s.BarbeiroNome
                })
            }, statusCode: 409);
        });

        painel.MapGet("/barbeiros", async (AppDbContext db) =>
            await db.Barbeiros.AsNoTracking()
                .Where(b => b.Ativo && b.Atende)
                .OrderBy(b => b.Nome)
                .Select(b => new { b.Id, b.Nome })
                .ToListAsync());

        var servicos = app.MapGroup("/api/gestor").RequireAuthorization("Servicos");

        servicos.MapGet("/servicos", async (AppDbContext db) =>
        {
            var vinculos = await db.BarbeiroServicos.AsNoTracking().ToListAsync();

            var lista = await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync();

            return Results.Ok(lista.Select(s => new
            {
                s.Id, s.Nome, s.DuracaoMinutos, s.PrecoCentavos, s.Ativo,
                barbeiroIds = vinculos.Where(v => v.ServicoId == s.Id).Select(v => v.BarbeiroId)
            }));
        });

        servicos.MapPost("/servicos", async (ServicoRequest req, AppDbContext db) =>
        {
            // Nome vazio nao era recusado em lugar nenhum: entrava um servico sem nome, que
            // aparecia como linha em branco na tela de agendar e nao dava para escolher. E
            // nome nulo era pior — o Trim logo abaixo estourava, e a resposta virava 500 em
            // vez de dizer o que faltava.
            if (string.IsNullOrWhiteSpace(req.Nome))
                return Results.BadRequest(new { erro = "Informe o nome do servico" });

            if (req.DuracaoMinutos is < 5 or > 480)
                return Results.BadRequest(new { erro = "Duracao deve ficar entre 5 e 480 minutos" });

            var servico = new Servico
            {
                Nome = req.Nome.Trim(),
                DuracaoMinutos = req.DuracaoMinutos,
                PrecoCentavos = req.PrecoCentavos,
                Ativo = req.Ativo
            };

            db.Servicos.Add(servico);
            await VincularAsync(db, servico.Id, req.BarbeiroIds);

            await db.SaveChangesAsync();
            return Results.Ok(new { servico.Id });
        });

        servicos.MapPut("/servicos/{id:guid}", async (Guid id, ServicoRequest req, AppDbContext db) =>
        {
            var servico = await db.Servicos.FirstOrDefaultAsync(s => s.Id == id);
            if (servico is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(req.Nome))
                return Results.BadRequest(new { erro = "Informe o nome do servico" });

            if (req.DuracaoMinutos is < 5 or > 480)
                return Results.BadRequest(new { erro = "Duracao deve ficar entre 5 e 480 minutos" });

            servico.Nome = req.Nome.Trim();
            servico.DuracaoMinutos = req.DuracaoMinutos;
            servico.PrecoCentavos = req.PrecoCentavos;
            servico.Ativo = req.Ativo;

            db.BarbeiroServicos.RemoveRange(
                await db.BarbeiroServicos.Where(x => x.ServicoId == id).ToListAsync());

            await VincularAsync(db, id, req.BarbeiroIds);

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Tira o servico do banco de vez. So o inativo sai, e so se nenhum agendamento
        // aponta para ele: o motivo das duas travas esta em RegraDeExclusao.
        servicos.MapDelete("/servicos/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var servico = await db.Servicos.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.Ativo })
                .FirstOrDefaultAsync();

            if (servico is null) return Results.NotFound();

            if (servico.Ativo)
                return RegraDeExclusao.Recusa(
                    "Este servico ainda esta ativo. Desative, confira que a agenda nao precisa "
                    + "mais dele, e ai exclua.");

            var agendamentos = await db.Agendamentos.CountAsync(a => a.ServicoId == id);

            if (agendamentos > 0)
                return RegraDeExclusao.Recusa(
                    RegraDeExclusao.Contagem(agendamentos, "agendamento usa", "agendamentos usam")
                    + " este servico, e apagar o servico apagaria o registro deles. Deixe inativo: "
                    + "assim ele nao aparece mais para marcar e o historico continua de pe.");

            // Os vinculos em barbeiro_servicos caem junto, pelo Cascade do banco. Nao
            // apago aqui de proposito: uma instrucao so, sem transacao para coordenar.
            await db.Servicos.Where(s => s.Id == id).ExecuteDeleteAsync();
            return Results.Ok();
        });

        // A tela listava as faixas sem dizer de quem eram, e era assim que o bug se
        // escondia: quem cadastrava um profissional novo via a tabela cheia de linhas —
        // as dos outros — e concluia que o horario estava resolvido. Agora vem o nome
        // junto, e vem tambem quem atende sem nenhuma faixa cadastrada, que e
        // exatamente quem nao aparece para o cliente marcar.
        g.MapGet("/expedientes", async (AppDbContext db) =>
        {
            var expedientes = await db.Expedientes.AsNoTracking()
                .OrderBy(e => e.Barbeiro!.Nome).ThenBy(e => e.DiaSemana).ThenBy(e => e.HoraInicio)
                .Select(e => new
                {
                    e.Id, e.BarbeiroId,
                    barbeiro = e.Barbeiro!.Nome,
                    diaSemana = (int)e.DiaSemana,
                    horaInicio = e.HoraInicio.ToString("HH:mm"),
                    horaFim = e.HoraFim.ToString("HH:mm")
                })
                .ToListAsync();

            var semHorario = await db.Barbeiros.AsNoTracking()
                .Where(b => b.Ativo && b.Atende && !b.Expedientes.Any())
                .OrderBy(b => b.Nome)
                .Select(b => new { b.Id, b.Nome })
                .ToListAsync();

            var temModelo = (await FuncionamentoDaBarbearia.ModeloAsync(db)).Count > 0;

            return Results.Ok(new { expedientes, semHorario, temModelo });
        });

        // Da ao profissional o mesmo funcionamento que a barbearia pratica. E o conserto
        // de quem foi cadastrado antes desta versao, quando o funcionario nascia sem
        // expediente nenhum e por isso nao aparecia para marcar.
        g.MapPost("/expedientes/copiar", async (CopiarFuncionamento req, AppDbContext db) =>
        {
            var barbeiro = await db.Barbeiros.AsNoTracking()
                .Where(b => b.Id == req.BarbeiroId)
                .Select(b => new { b.Nome, b.Atende })
                .FirstOrDefaultAsync();

            if (barbeiro is null) return Results.NotFound();

            if (!barbeiro.Atende)
                return Results.BadRequest(new
                {
                    erro = $"{barbeiro.Nome} esta marcado como quem nao atende, entao nao entra na "
                           + "agenda. Marque \"Atende clientes\" no cadastro se ele for pegar horario."
                });

            // Nao mistura com o que ja existe: duas faixas sobrepostas no mesmo dia fazem
            // o mesmo horario aparecer duas vezes para o cliente.
            if (await db.Expedientes.AnyAsync(e => e.BarbeiroId == req.BarbeiroId))
                return Results.Conflict(new
                {
                    erro = $"{barbeiro.Nome} ja tem horario cadastrado. Apague as faixas dele antes "
                           + "de copiar o funcionamento da barbearia."
                });

            var criados = await FuncionamentoDaBarbearia.AplicarAsync(db, req.BarbeiroId);

            if (criados == 0)
                return Results.Conflict(new
                {
                    erro = "Nenhum profissional ativo tem horario cadastrado, entao nao existe "
                           + "funcionamento para copiar. Cadastre as faixas de um deles primeiro."
                });

            await db.SaveChangesAsync();
            return Results.Ok(new { criados });
        });

        g.MapPost("/expedientes", async (ExpedienteRequest req, AppDbContext db) =>
        {
            if (!TimeOnly.TryParse(req.HoraInicio, out var inicio)
                || !TimeOnly.TryParse(req.HoraFim, out var fim))
                return Results.BadRequest(new { erro = "Horario invalido" });

            if (fim <= inicio)
                return Results.BadRequest(new { erro = "Hora fim precisa ser maior que a de inicio" });

            if (req.DiaSemana is < 0 or > 6)
                return Results.BadRequest(new { erro = "Dia da semana invalido" });

            db.Expedientes.Add(new Expediente
            {
                BarbeiroId = req.BarbeiroId,
                DiaSemana = (DayOfWeek)req.DiaSemana,
                HoraInicio = inicio,
                HoraFim = fim
            });

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        g.MapDelete("/expedientes/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Expedientes.Where(e => e.Id == id).ExecuteDeleteAsync() > 0
                ? Results.Ok() : Results.NotFound());

        g.MapGet("/bloqueios", async (AppDbContext db) =>
        {
            var desde = DateTime.UtcNow.AddDays(-7);
            var lista = await db.Bloqueios.AsNoTracking()
                .Include(b => b.Barbeiro)
                .Where(b => b.FimUtc >= desde)
                .OrderBy(b => b.InicioUtc)
                .ToListAsync();

            return Results.Ok(lista.Select(b => new
            {
                b.Id, b.BarbeiroId,
                barbeiro = b.Barbeiro!.Nome,
                inicio = Fuso.ParaLocal(b.InicioUtc),
                fim = Fuso.ParaLocal(b.FimUtc),
                b.Motivo
            }));
        });

        g.MapPost("/bloqueios", async (BloqueioRequest req, AppDbContext db) =>
        {
            if (req.FimLocal <= req.InicioLocal)
                return Results.BadRequest(new { erro = "Fim precisa ser maior que o inicio" });

            db.Bloqueios.Add(new Bloqueio
            {
                BarbeiroId = req.BarbeiroId,
                InicioUtc = Fuso.ParaUtc(req.InicioLocal),
                FimUtc = Fuso.ParaUtc(req.FimLocal),
                Motivo = req.Motivo ?? ""
            });

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        g.MapDelete("/bloqueios/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Bloqueios.Where(b => b.Id == id).ExecuteDeleteAsync() > 0
                ? Results.Ok() : Results.NotFound());
    }
}
