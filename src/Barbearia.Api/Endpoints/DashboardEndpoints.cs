using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class DashboardEndpoints
{
    private const int Teto = 20000;

    private static readonly string[] DiasSemana =
        ["Domingo", "Segunda", "Terca", "Quarta", "Quinta", "Sexta", "Sabado"];

    private record LinhaForma(string forma, int quantidade, long centavos);

    private record ResumoCaixa(
        long servicoCentavos, long produtoCentavos, long totalCentavos,
        int atendimentos, long ticketMedioCentavos, long descontoCentavos,
        IReadOnlyList<LinhaForma> porForma);

    public static void MapDashboard(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/dashboard").RequireAuthorization("Dashboard");

        g.MapGet("/", async (DateOnly? de, DateOnly? ate, AppDbContext db) =>
        {
            var hoje = Fuso.HojeLocal();
            var inicio = de ?? hoje.AddDays(-29);
            var fim = ate ?? hoje;
            if (fim < inicio) (inicio, fim) = (fim, inicio);

            var inicioUtc = Fuso.ParaUtc(inicio.ToDateTime(TimeOnly.MinValue));
            var fimUtc = Fuso.ParaUtc(fim.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var agendamentos = await db.Agendamentos.AsNoTracking()
                .Include(a => a.Servico).Include(a => a.Barbeiro).Include(a => a.Cliente)
                .Where(a => a.InicioUtc >= inicioUtc && a.InicioUtc < fimUtc)
                .OrderBy(a => a.InicioUtc)
                .Take(Teto)
                .ToListAsync();

            var ativos = agendamentos.Where(a => a.Status != StatusAgendamento.Cancelado).ToList();
            var cancelados = agendamentos.Count - ativos.Count;

            var receita = ativos.Sum(a => (long)a.PrecoCentavos);
            var minutos = ativos.Sum(a => (long)(a.FimUtc - a.InicioUtc).TotalMinutes);

            var agora = DateTime.UtcNow;
            var realizada = ativos.Where(a => a.InicioUtc <= agora).ToList();
            var agendada = ativos.Where(a => a.InicioUtc > agora).ToList();

            var expedientes = await db.Expedientes.AsNoTracking().ToListAsync();
            var diasCorridos = fim.DayNumber - inicio.DayNumber + 1;

            var diasAbertos = Enumerable.Range(0, diasCorridos)
                .Select(i => inicio.AddDays(i))
                .Count(d => expedientes.Any(e => e.DiaSemana == d.DayOfWeek));

            var diasComAtendimento = ativos
                .Select(a => DateOnly.FromDateTime(Fuso.ParaLocal(a.InicioUtc)))
                .Distinct().Count();

            // Caixa recortado por FechadoEmUtc, e nao por InicioUtc: dinheiro entra
            // no dia em que foi recebido. Na pratica o fechamento acontece no proprio
            // atendimento, entao os dois quase sempre caem no mesmo dia — a diferenca
            // aparece quando alguem fecha um atendimento esquecido dias depois.
            var fechados = await FechadosAsync(db, inicioUtc, fimUtc);
            var produtoPorAtendimento = await ProdutosPorAtendimentoAsync(db, inicioUtc, fimUtc);
            var caixa = Caixa(fechados, produtoPorAtendimento);

            // Atendimento cujo horario passou e ninguem fechou: ou a pessoa nao
            // apareceu, ou o barbeiro esqueceu de encerrar. O painel nao tem como
            // saber qual dos dois, e chutar seria pior do que mostrar o numero.
            var semFechamento = ativos
                .Where(a => !a.EstaFechado && a.InicioUtc <= agora)
                .ToList();

            // Caixa por dia, pre-agrupado: dentro do Select da serie isso seria uma
            // varredura da lista de fechados por dia do periodo.
            var caixaPorDia = fechados
                .GroupBy(f => DateOnly.FromDateTime(Fuso.ParaLocal(f.FechadoEmUtc!.Value)))
                .ToDictionary(
                    x => x.Key,
                    x => x.Sum(f => (long)(f.ValorCobradoCentavos ?? 0)
                                    + produtoPorAtendimento.GetValueOrDefault(f.Id)));

            var idsNoPeriodo = ativos.Select(a => a.ClienteId).Distinct().ToList();
            var primeiraVisita = await db.Agendamentos.AsNoTracking()
                .Where(a => idsNoPeriodo.Contains(a.ClienteId) && a.Status != StatusAgendamento.Cancelado)
                .GroupBy(a => a.ClienteId)
                .Select(x => new { ClienteId = x.Key, Primeiro = x.Min(a => a.InicioUtc) })
                .ToListAsync();

            var novos = primeiraVisita.Count(x => x.Primeiro >= inicioUtc);

            return Results.Ok(new
            {
                periodo = new
                {
                    de = inicio, ate = fim,
                    diasCorridos, diasAbertos, diasComAtendimento,
                    truncado = agendamentos.Count >= Teto
                },

                resumo = new
                {
                    atendimentos = ativos.Count,
                    cancelados,
                    concluidos = ativos.Count(a => a.Status == StatusAgendamento.Concluido),
                    confirmados = ativos.Count(a => a.Status == StatusAgendamento.Confirmado),
                    taxaCancelamentoPct = Pct(cancelados, agendamentos.Count),
                    receitaCentavos = receita,
                    receitaRealizadaCentavos = realizada.Sum(a => (long)a.PrecoCentavos),
                    receitaAgendadaCentavos = agendada.Sum(a => (long)a.PrecoCentavos),
                    atendimentosRealizados = realizada.Count,
                    atendimentosAgendados = agendada.Count,
                    ticketMedioCentavos = ativos.Count == 0 ? 0 : receita / ativos.Count,
                    minutos,
                    horasAtendidas = Math.Round(minutos / 60d, 1),
                    clientesUnicos = idsNoPeriodo.Count,
                    clientesNovos = novos,
                    clientesRecorrentes = idsNoPeriodo.Count - novos
                },

                caixa = new
                {
                    caixa.servicoCentavos,
                    caixa.produtoCentavos,
                    caixa.totalCentavos,
                    caixa.atendimentos,
                    caixa.ticketMedioCentavos,
                    caixa.descontoCentavos,
                    caixa.porForma,
                    mediaPorDiaAberto = Div(caixa.totalCentavos, diasAbertos)
                },

                aberto = new
                {
                    // Previsto que ainda pode entrar (horario futuro) e previsto que
                    // passou sem fechamento. Somados com o caixa dao o previsto todo.
                    aReceberCentavos = agendada.Sum(a => (long)a.PrecoCentavos),
                    aReceberQuantidade = agendada.Count,
                    semFechamentoCentavos = semFechamento.Sum(a => (long)a.PrecoCentavos),
                    semFechamentoQuantidade = semFechamento.Count
                },

                medias = new
                {
                    porDiaCorrido = Div(receita, diasCorridos),
                    porDiaAberto = Div(receita, diasAbertos),
                    porDiaComAtendimento = Div(receita, diasComAtendimento),
                    porSemana = Div(receita, Math.Max(1, (int)Math.Ceiling(diasCorridos / 7d))),
                    porMes = Div(receita, Math.Max(1, (int)Math.Ceiling(diasCorridos / 30d))),
                    atendimentosPorDiaAberto = diasAbertos == 0 ? 0 : Math.Round((double)ativos.Count / diasAbertos, 1)
                },

                porBarbeiro = ativos
                    .GroupBy(a => new { a.BarbeiroId, Nome = a.Barbeiro!.Nome })
                    .Select(x => new
                    {
                        x.Key.BarbeiroId,
                        nome = x.Key.Nome,
                        atendimentos = x.Count(),
                        receitaCentavos = x.Sum(a => (long)a.PrecoCentavos),
                        minutos = x.Sum(a => (long)(a.FimUtc - a.InicioUtc).TotalMinutes),
                        ticketMedioCentavos = x.Sum(a => (long)a.PrecoCentavos) / x.Count(),
                        caixaCentavos = fechados
                            .Where(f => f.BarbeiroId == x.Key.BarbeiroId)
                            .Sum(f => (long)(f.ValorCobradoCentavos ?? 0)
                                      + produtoPorAtendimento.GetValueOrDefault(f.Id)),
                        fechamentos = fechados.Count(f => f.BarbeiroId == x.Key.BarbeiroId),
                        ocupacaoPct = Ocupacao(
                            x.Sum(a => (long)(a.FimUtc - a.InicioUtc).TotalMinutes),
                            expedientes.Where(e => e.BarbeiroId == x.Key.BarbeiroId).ToList(),
                            inicio, diasCorridos)
                    })
                    .OrderByDescending(x => x.receitaCentavos),

                porServico = ativos
                    .GroupBy(a => new { a.ServicoId, Nome = a.Servico!.Nome })
                    .Select(x => new
                    {
                        nome = x.Key.Nome,
                        quantidade = x.Count(),
                        receitaCentavos = x.Sum(a => (long)a.PrecoCentavos),
                        participacaoPct = Pct(x.Sum(a => (long)a.PrecoCentavos), receita)
                    })
                    .OrderByDescending(x => x.receitaCentavos),

                porOrigem = ativos
                    .GroupBy(a => a.Origem)
                    .Select(x => new { origem = x.Key.ToString(), quantidade = x.Count() })
                    .OrderByDescending(x => x.quantidade),

                porDiaSemana = Enumerable.Range(0, 7).Select(d => new
                {
                    dia = DiasSemana[d],
                    quantidade = ativos.Count(a => (int)Fuso.ParaLocal(a.InicioUtc).DayOfWeek == d),
                    receitaCentavos = ativos
                        .Where(a => (int)Fuso.ParaLocal(a.InicioUtc).DayOfWeek == d)
                        .Sum(a => (long)a.PrecoCentavos)
                }),

                porHora = ativos
                    .GroupBy(a => Fuso.ParaLocal(a.InicioUtc).Hour)
                    .Select(x => new { hora = x.Key, quantidade = x.Count() })
                    .OrderBy(x => x.hora),

                topClientes = ativos
                    .GroupBy(a => new { a.ClienteId, Nome = a.Cliente!.Nome, a.Cliente.Telefone })
                    .Select(x => new
                    {
                        nome = string.IsNullOrWhiteSpace(x.Key.Nome) ? "(sem nome)" : x.Key.Nome,
                        telefone = TelefoneBr.Formatar(x.Key.Telefone),
                        atendimentos = x.Count(),
                        receitaCentavos = x.Sum(a => (long)a.PrecoCentavos)
                    })
                    .OrderByDescending(x => x.receitaCentavos).Take(10),

                serie = Enumerable.Range(0, diasCorridos)
                    .Select(i => inicio.AddDays(i))
                    .Select(d => new
                    {
                        data = d,
                        quantidade = ativos.Count(a => DateOnly.FromDateTime(Fuso.ParaLocal(a.InicioUtc)) == d),
                        receitaCentavos = ativos
                            .Where(a => DateOnly.FromDateTime(Fuso.ParaLocal(a.InicioUtc)) == d)
                            .Sum(a => (long)a.PrecoCentavos),
                        caixaCentavos = caixaPorDia.GetValueOrDefault(d)
                    })
            });
        });

        // Caixa de hoje. Nao respeita o filtro de periodo de proposito: e o numero
        // que o dono olha de manha e no fim do dia, e ele nao deveria mudar porque
        // alguem mexeu no filtro para analisar outro mes.
        g.MapGet("/caixa", async (AppDbContext db) =>
        {
            var hoje = Fuso.HojeLocal();
            var deUtc = Fuso.ParaUtc(hoje.ToDateTime(TimeOnly.MinValue));
            var ateUtc = Fuso.ParaUtc(hoje.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var fechados = await FechadosAsync(db, deUtc, ateUtc);
            var produtos = await ProdutosPorAtendimentoAsync(db, deUtc, ateUtc);
            var caixa = Caixa(fechados, produtos);

            var agora = DateTime.UtcNow;

            var doDia = await db.Agendamentos.AsNoTracking()
                .Where(a => a.InicioUtc >= deUtc && a.InicioUtc < ateUtc
                            && a.Status != StatusAgendamento.Cancelado)
                .Select(a => new { a.InicioUtc, a.PrecoCentavos, a.FechadoEmUtc })
                .ToListAsync();

            var aReceber = doDia.Where(a => a.FechadoEmUtc == null && a.InicioUtc > agora).ToList();
            var semFechar = doDia.Where(a => a.FechadoEmUtc == null && a.InicioUtc <= agora).ToList();

            // Comparacao com o mesmo dia da semana nas oito semanas anteriores. Segunda
            // com segunda: comparar com "a media de todos os dias" enganaria, porque o
            // movimento de sabado nao tem nada a ver com o de terca.
            var desdeUtc = Fuso.ParaUtc(hoje.AddDays(-56).ToDateTime(TimeOnly.MinValue));

            var anteriores = await db.Agendamentos.AsNoTracking()
                .Where(a => a.FechadoEmUtc >= desdeUtc && a.FechadoEmUtc < deUtc)
                .Select(a => new { a.FechadoEmUtc, a.ValorCobradoCentavos })
                .ToListAsync();

            var mesmosDias = anteriores
                .Select(a => new
                {
                    dia = DateOnly.FromDateTime(Fuso.ParaLocal(a.FechadoEmUtc!.Value)),
                    centavos = (long)(a.ValorCobradoCentavos ?? 0)
                })
                .Where(a => a.dia.DayOfWeek == hoje.DayOfWeek)
                .GroupBy(a => a.dia)
                .Select(x => x.Sum(a => a.centavos))
                .ToList();

            return Results.Ok(new
            {
                dia = hoje,
                diaSemana = DiasSemana[(int)hoje.DayOfWeek],
                caixa = new
                {
                    caixa.servicoCentavos, caixa.produtoCentavos, caixa.totalCentavos,
                    caixa.atendimentos, caixa.ticketMedioCentavos, caixa.descontoCentavos,
                    caixa.porForma
                },
                aReceberCentavos = aReceber.Sum(a => (long)a.PrecoCentavos),
                aReceberQuantidade = aReceber.Count,
                semFechamentoCentavos = semFechar.Sum(a => (long)a.PrecoCentavos),
                semFechamentoQuantidade = semFechar.Count,
                comparacao = new
                {
                    // So compara o servico, que e o que existe nas oito semanas
                    // anteriores em qualquer cenario; produto entrou agora.
                    semanas = mesmosDias.Count,
                    mediaCentavos = mesmosDias.Count == 0 ? 0 : mesmosDias.Sum() / mesmosDias.Count
                }
            });
        });

        g.MapGet("/janelas", async (AppDbContext db) =>
        {
            var hoje = Fuso.HojeLocal();
            var janelas = new (string Rotulo, int Dias)[]
                { ("Hoje", 1), ("7 dias", 7), ("30 dias", 30), ("6 meses", 180), ("1 ano", 365) };

            var maisAntigaUtc = Fuso.ParaUtc(hoje.AddDays(-364).ToDateTime(TimeOnly.MinValue));
            var fimUtc = Fuso.ParaUtc(hoje.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var dados = await db.Agendamentos.AsNoTracking()
                .Where(a => a.Status != StatusAgendamento.Cancelado
                            && a.InicioUtc >= maisAntigaUtc && a.InicioUtc < fimUtc)
                .Select(a => new { a.InicioUtc, a.PrecoCentavos })
                .ToListAsync();

            return Results.Ok(janelas.Select(j =>
            {
                var desdeUtc = Fuso.ParaUtc(hoje.AddDays(-(j.Dias - 1)).ToDateTime(TimeOnly.MinValue));
                var recorte = dados.Where(a => a.InicioUtc >= desdeUtc).ToList();
                var total = recorte.Sum(a => (long)a.PrecoCentavos);

                return new
                {
                    rotulo = j.Rotulo,
                    atendimentos = recorte.Count,
                    receitaCentavos = total,
                    mediaDiariaCentavos = Div(total, j.Dias)
                };
            }));
        });

        g.MapGet("/produtos", async (DateOnly? de, DateOnly? ate, AppDbContext db) =>
        {
            var produtos = await db.Produtos.AsNoTracking().ToListAsync();

            var hoje = Fuso.HojeLocal();
            var inicio = de ?? hoje.AddDays(-29);
            var fim = ate ?? hoje;
            if (fim < inicio) (inicio, fim) = (fim, inicio);

            var deUtc = Fuso.ParaUtc(inicio.ToDateTime(TimeOnly.MinValue));
            var ateUtc = Fuso.ParaUtc(fim.AddDays(1).ToDateTime(TimeOnly.MinValue));

            // Venda de verdade: o que foi confirmado no fechamento do atendimento.
            var vendas = await db.PedidosProduto.AsNoTracking()
                .Where(x => x.Vendido
                            && x.Agendamento!.FechadoEmUtc >= deUtc
                            && x.Agendamento.FechadoEmUtc < ateUtc)
                .Select(x => new
                {
                    x.ProdutoId,
                    x.Quantidade,
                    centavos = (long)(x.PrecoCentavosNaVenda ?? 0) * x.Quantidade
                })
                .ToListAsync();

            var porProduto = vendas
                .GroupBy(x => x.ProdutoId)
                .ToDictionary(x => x.Key, x => new
                {
                    unidades = x.Sum(v => v.Quantidade),
                    centavos = x.Sum(v => v.centavos)
                });

            return Results.Ok(new
            {
                vendasRegistradas = true,
                periodo = new { de = inicio, ate = fim },
                vendidoCentavos = vendas.Sum(x => x.centavos),
                unidadesVendidas = vendas.Sum(x => x.Quantidade),
                total = produtos.Count,
                ativos = produtos.Count(p => p.Ativo),
                semEstoque = produtos.Count(p => p.Estoque <= 0),
                valorEmEstoqueCentavos = produtos.Sum(p => (long)p.PrecoCentavos * p.Estoque),
                itens = produtos
                    .Select(p => new
                    {
                        p.Nome, p.Estoque, p.PrecoCentavos, p.Ativo,
                        valorEstoqueCentavos = (long)p.PrecoCentavos * p.Estoque,
                        unidadesVendidas = porProduto.GetValueOrDefault(p.Id)?.unidades ?? 0,
                        vendidoCentavos = porProduto.GetValueOrDefault(p.Id)?.centavos ?? 0
                    })
                    .OrderByDescending(p => p.vendidoCentavos)
                    .ThenByDescending(p => p.valorEstoqueCentavos)
            });
        });
    }

    // Caixa e o que foi fechado, e nao o que estava na agenda: soma o valor cobrado
    // do servico com o preco congelado dos produtos que sairam. O desconto e a
    // diferenca entre o preco combinado no agendamento e o que a pessoa pagou.
    private static ResumoCaixa Caixa(
        List<Agendamento> fechados, Dictionary<Guid, long> produtoPorAtendimento)
    {
        long DoAtendimento(Agendamento a) =>
            (a.ValorCobradoCentavos ?? 0) + produtoPorAtendimento.GetValueOrDefault(a.Id);

        var servico = fechados.Sum(a => (long)(a.ValorCobradoCentavos ?? 0));
        var produto = fechados.Sum(a => produtoPorAtendimento.GetValueOrDefault(a.Id));
        var total = servico + produto;

        var porForma = fechados
            .GroupBy(a => a.FormaPagamento)
            .Select(x => new LinhaForma(
                x.Key?.ToString() ?? "Outro",
                x.Count(),
                x.Sum(DoAtendimento)))
            .OrderByDescending(x => x.centavos)
            .ToList();

        return new ResumoCaixa(
            servico, produto, total,
            fechados.Count,
            fechados.Count == 0 ? 0 : total / fechados.Count,
            fechados.Sum(a => (long)Math.Max(0, a.PrecoCentavos - (a.ValorCobradoCentavos ?? 0))),
            porForma);
    }

    // Quanto de produto saiu em cada atendimento fechado da faixa. Fica separado do
    // agendamento porque um atendimento pode ter varios produtos.
    private static async Task<Dictionary<Guid, long>> ProdutosPorAtendimentoAsync(
        AppDbContext db, DateTime deUtc, DateTime ateUtc)
    {
        var linhas = await db.PedidosProduto.AsNoTracking()
            .Where(x => x.Vendido
                        && x.Agendamento!.FechadoEmUtc >= deUtc
                        && x.Agendamento.FechadoEmUtc < ateUtc)
            .Select(x => new
            {
                x.AgendamentoId,
                centavos = (long)(x.PrecoCentavosNaVenda ?? 0) * x.Quantidade
            })
            .ToListAsync();

        return linhas
            .GroupBy(x => x.AgendamentoId)
            .ToDictionary(x => x.Key, x => x.Sum(l => l.centavos));
    }

    private static async Task<List<Agendamento>> FechadosAsync(
        AppDbContext db, DateTime deUtc, DateTime ateUtc) =>
        await db.Agendamentos.AsNoTracking()
            .Include(a => a.Barbeiro)
            .Where(a => a.FechadoEmUtc >= deUtc && a.FechadoEmUtc < ateUtc)
            .ToListAsync();

    private static long Div(long total, int divisor) => divisor <= 0 ? 0 : total / divisor;

    private static double Pct(long parte, long total) =>
        total <= 0 ? 0 : Math.Round(parte * 100d / total, 1);

    private static double Ocupacao(long minutosAtendidos, List<Expediente> doBarbeiro,
        DateOnly inicio, int diasCorridos)
    {
        if (doBarbeiro.Count == 0) return 0;

        var disponivel = Enumerable.Range(0, diasCorridos)
            .Select(i => inicio.AddDays(i))
            .Sum(d => doBarbeiro
                .Where(e => e.DiaSemana == d.DayOfWeek)
                .Sum(e => (e.HoraFim - e.HoraInicio).TotalMinutes));

        return disponivel <= 0 ? 0 : Math.Round(minutosAtendidos * 100 / disponivel, 1);
    }
}
