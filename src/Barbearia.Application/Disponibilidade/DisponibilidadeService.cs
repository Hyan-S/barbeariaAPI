using Barbearia.Application.Configuracao;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Barbearia.Application.Disponibilidade;

/// <summary>
/// Disponivel = expediente do dia - agendamentos ativos - bloqueios - passado.
/// </summary>
public class DisponibilidadeService(IAppDbContext db, ConfiguracaoService configuracao)
{
    /// <summary>Só os livres. <paramref name="barbeiroId"/> nulo = qualquer barbeiro.</summary>
    public Task<IReadOnlyList<Slot>> ObterDoDiaAsync(
        DateOnly diaLocal, Guid servicoId, Guid? barbeiroId = null, CancellationToken ct = default) =>
        CalcularAsync(diaLocal, servicoId, barbeiroId, false, null, ct);

    /// <summary>
    /// Grade para reagendamento: o proprio agendamento sai da conta, senao mover
    /// das 14h para 14h15 conflitaria com ele mesmo.
    /// </summary>
    public Task<IReadOnlyList<Slot>> ObterGradeParaMoverAsync(
        DateOnly diaLocal, Guid servicoId, Guid ignorar, Guid? barbeiroId = null,
        CancellationToken ct = default) =>
        CalcularAsync(diaLocal, servicoId, barbeiroId, true, ignorar, ct);

    /// <summary>
    /// Dia inteiro marcando livre/ocupado. O cliente ve que a barbearia atende ate
    /// as 19h e que as 15h ja foi, em vez de so ver um buraco na lista.
    /// </summary>
    public Task<IReadOnlyList<Slot>> ObterGradeDoDiaAsync(
        DateOnly diaLocal, Guid servicoId, Guid? barbeiroId = null, CancellationToken ct = default) =>
        CalcularAsync(diaLocal, servicoId, barbeiroId, true, null, ct);

    private async Task<IReadOnlyList<Slot>> CalcularAsync(
        DateOnly diaLocal,
        Guid servicoId,
        Guid? barbeiroId,
        bool incluirOcupados,
        Guid? ignorarAgendamentoId,
        CancellationToken ct = default)
    {
        var cfg = await configuracao.ObterBarbeariaAsync(ct);

        var servico = await db.Servicos
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == servicoId && s.Ativo, ct);

        if (servico is null) return [];

        // Servico sem vinculo cadastrado e atendido por todos os barbeiros ativos.
        var habilitados = await db.BarbeiroServicos
            .AsNoTracking()
            .Where(x => x.ServicoId == servicoId)
            .Select(x => x.BarbeiroId)
            .ToListAsync(ct);

        var filtrarPorServico = habilitados.Count > 0;

        var barbeiros = await db.Barbeiros
            .AsNoTracking()
            .Where(x => x.Ativo && x.Atende
                        && (barbeiroId == null || x.Id == barbeiroId)
                        && (!filtrarPorServico || habilitados.Contains(x.Id)))
            .Select(x => new { x.Id, x.Nome })
            .ToListAsync(ct);

        if (barbeiros.Count == 0) return [];

        var ids = barbeiros.Select(x => x.Id).ToList();

        var inicioDiaUtc = Fuso.ParaUtc(diaLocal.ToDateTime(TimeOnly.MinValue));
        var fimDiaUtc = Fuso.ParaUtc(diaLocal.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var expedientes = await db.Expedientes
            .AsNoTracking()
            .Where(x => ids.Contains(x.BarbeiroId) && x.DiaSemana == diaLocal.DayOfWeek)
            .ToListAsync(ct);

        if (expedientes.Count == 0) return [];

        var ocupados = await db.Agendamentos
            .AsNoTracking()
            .Where(x => ids.Contains(x.BarbeiroId)
                        && (x.Status == StatusAgendamento.Pendente || x.Status == StatusAgendamento.Confirmado)
                        && (ignorarAgendamentoId == null || x.Id != ignorarAgendamentoId)
                        && x.InicioUtc < fimDiaUtc
                        && x.FimUtc > inicioDiaUtc)
            .Select(x => new Intervalo(x.BarbeiroId, x.InicioUtc, x.FimUtc))
            .ToListAsync(ct);

        var bloqueios = await db.Bloqueios
            .AsNoTracking()
            .Where(x => ids.Contains(x.BarbeiroId)
                        && x.InicioUtc < fimDiaUtc
                        && x.FimUtc > inicioDiaUtc)
            .Select(x => new Intervalo(x.BarbeiroId, x.InicioUtc, x.FimUtc))
            .ToListAsync(ct);

        var indisponiveis = ocupados.Concat(bloqueios).ToLookup(x => x.BarbeiroId);

        var minimoUtc = DateTime.UtcNow.AddMinutes(cfg.AntecedenciaMinimaMinutos);
        var passo = TimeSpan.FromMinutes(cfg.IntervaloSlotMinutos);
        var duracao = TimeSpan.FromMinutes(servico.DuracaoMinutos);

        var slots = new List<Slot>();

        foreach (var barbeiro in barbeiros)
        {
            var conflitos = indisponiveis[barbeiro.Id].ToList();

            foreach (var exp in expedientes.Where(x => x.BarbeiroId == barbeiro.Id))
            {
                var janelaInicioUtc = Fuso.ParaUtc(diaLocal.ToDateTime(exp.HoraInicio));
                var janelaFimUtc = Fuso.ParaUtc(diaLocal.ToDateTime(exp.HoraFim));

                for (var inicio = janelaInicioUtc; inicio + duracao <= janelaFimUtc; inicio += passo)
                {
                    var fim = inicio + duracao;

                    if (inicio < minimoUtc) continue;

                    var ocupado = conflitos.Any(c => inicio < c.FimUtc && fim > c.InicioUtc);
                    if (ocupado && !incluirOcupados) continue;

                    slots.Add(new Slot(barbeiro.Id, barbeiro.Nome, inicio, fim, !ocupado));
                }
            }
        }

        return slots.OrderBy(x => x.InicioUtc).ThenBy(x => x.BarbeiroNome).ToList();
    }

    public async Task<Slot?> ObterExatoAsync(
        DateTime inicioUtc,
        Guid servicoId,
        Guid? barbeiroId = null,
        CancellationToken ct = default)
    {
        var slots = await ObterTodosLivresAsync(inicioUtc, servicoId, barbeiroId, ct);
        return slots.FirstOrDefault();
    }

    /// <summary>
    /// Todos os profissionais livres naquele horario exato. Quando o cliente aceita
    /// "qualquer um", o agendamento tenta o proximo da lista se o primeiro for
    /// tomado entre o calculo e o INSERT.
    /// </summary>
    public async Task<IReadOnlyList<Slot>> ObterTodosLivresAsync(
        DateTime inicioUtc,
        Guid servicoId,
        Guid? barbeiroId = null,
        CancellationToken ct = default)
    {
        var diaLocal = DateOnly.FromDateTime(Fuso.ParaLocal(inicioUtc));
        var slots = await ObterDoDiaAsync(diaLocal, servicoId, barbeiroId, ct);
        return slots.Where(s => s.InicioUtc == inicioUtc).ToList();
    }

    /// <summary>O "se nao tiver, sugere o mais proximo" do fluxo de WhatsApp.</summary>
    public async Task<IReadOnlyList<Slot>> SugerirProximosAsync(
        DateTime desejadoUtc,
        Guid servicoId,
        Guid? barbeiroId = null,
        int quantidade = 3,
        int diasParaVarrer = 7,
        CancellationToken ct = default)
    {
        var cfg = await configuracao.ObterBarbeariaAsync(ct);
        var candidatos = new List<Slot>();
        var diaInicial = DateOnly.FromDateTime(Fuso.ParaLocal(desejadoUtc));

        for (var i = 0; i < diasParaVarrer && candidatos.Count < quantidade * 8; i++)
        {
            var dia = diaInicial.AddDays(i);
            if (dia > Fuso.HojeLocal().AddDays(cfg.DiasMaximosNoFuturo)) break;

            candidatos.AddRange(await ObterDoDiaAsync(dia, servicoId, barbeiroId, ct));
        }

        // Ordenar por distancia do horario pedido; senao um pedido das 15h recebe
        // "08:00 de amanha" como melhor sugestao.
        return candidatos
            .OrderBy(s => Math.Abs((s.InicioUtc - desejadoUtc).TotalMinutes))
            .Take(quantidade)
            .OrderBy(s => s.InicioUtc)
            .ToList();
    }

    private record Intervalo(Guid BarbeiroId, DateTime InicioUtc, DateTime FimUtc);
}
