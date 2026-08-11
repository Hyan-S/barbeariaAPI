using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class DashboardEndpoints
{
    // Teto de linhas; ao estourar a resposta sinaliza truncado em vez de mentir o total.
    private const int Teto = 20000;

    private static readonly string[] DiasSemana =
        ["Domingo", "Segunda", "Terca", "Quarta", "Quinta", "Sexta", "Sabado"];

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

            // Periodo que avanca no futuro mistura caixa realizado com agenda vendida.
            // Separar evita ler "receita do mes" como dinheiro que ja entrou.
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
                            .Sum(a => (long)a.PrecoCentavos)
                    })
            });
        });

        // Janelas fixas terminando hoje, independentes do filtro da tela.
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

        // Produtos entram como catalogo e estoque: nao existe registro de venda no
        // sistema, entao faturamento de produto nao tem como ser calculado.
        g.MapGet("/produtos", async (AppDbContext db) =>
        {
            var produtos = await db.Produtos.AsNoTracking().ToListAsync();

            return Results.Ok(new
            {
                vendasRegistradas = false,
                total = produtos.Count,
                ativos = produtos.Count(p => p.Ativo),
                semEstoque = produtos.Count(p => p.Estoque <= 0),
                valorEmEstoqueCentavos = produtos.Sum(p => (long)p.PrecoCentavos * p.Estoque),
                itens = produtos.OrderByDescending(p => p.PrecoCentavos * p.Estoque)
                    .Select(p => new
                    {
                        p.Nome, p.Estoque, p.PrecoCentavos, p.Ativo,
                        valorEstoqueCentavos = (long)p.PrecoCentavos * p.Estoque
                    })
            });
        });
    }

    private static long Div(long total, int divisor) => divisor <= 0 ? 0 : total / divisor;

    private static double Pct(long parte, long total) =>
        total <= 0 ? 0 : Math.Round(parte * 100d / total, 1);

    /// <summary>
    /// Quanto do expediente do profissional virou atendimento. Bloqueios nao entram
    /// na conta, entao ferias e folgas pontuais derrubam o numero.
    /// </summary>
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
