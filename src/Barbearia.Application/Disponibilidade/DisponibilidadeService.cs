using Barbearia.Application.Configuracao;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Barbearia.Application.Disponibilidade;

public class DisponibilidadeService(IAppDbContext db, ConfiguracaoService configuracao)
{
    // dispensarAntecedencia e para quem atende, nao para quem marca pelo site: o
    // funcionario precisa lancar o encaixe de quem ja esta na cadeira, e a antecedencia
    // minima existe para o cliente nao marcar em cima da hora sem a barbearia saber.
    public Task<IReadOnlyList<Slot>> ObterDoDiaAsync(
        DateOnly diaLocal, Guid servicoId, Guid? barbeiroId = null,
        bool dispensarAntecedencia = false, CancellationToken ct = default) =>
        CalcularAsync(diaLocal, servicoId, barbeiroId, false, null, dispensarAntecedencia, ct);

    public Task<IReadOnlyList<Slot>> ObterGradeParaMoverAsync(
        DateOnly diaLocal, Guid servicoId, Guid ignorar, Guid? barbeiroId = null,
        bool dispensarAntecedencia = false, CancellationToken ct = default) =>
        CalcularAsync(diaLocal, servicoId, barbeiroId, true, ignorar, dispensarAntecedencia, ct);

    public Task<IReadOnlyList<Slot>> ObterGradeDoDiaAsync(
        DateOnly diaLocal, Guid servicoId, Guid? barbeiroId = null,
        bool dispensarAntecedencia = false, CancellationToken ct = default) =>
        CalcularAsync(diaLocal, servicoId, barbeiroId, true, null, dispensarAntecedencia, ct);

    private async Task<IReadOnlyList<Slot>> CalcularAsync(
        DateOnly diaLocal,
        Guid servicoId,
        Guid? barbeiroId,
        bool incluirOcupados,
        Guid? ignorarAgendamentoId,
        bool dispensarAntecedencia,
        CancellationToken ct = default)
    {
        var cfg = await configuracao.ObterBarbeariaAsync(ct);

        var servico = await db.Servicos
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == servicoId && s.Ativo, ct);

        if (servico is null) return [];

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

        // Ocupa a agenda tudo que nao foi cancelado — Concluido inclusive. Esta e a
        // mesma condicao da trava do banco (EXCLUDE ... WHERE "Status" <> 2), e as duas
        // precisam dizer a mesma coisa.
        //
        // Antes aqui estava (Pendente || Confirmado), o que deixava o Concluido de fora.
        // Enquanto ninguem concluia nada isso nao aparecia; quando o fechamento de caixa
        // entrou e passou a gravar Concluido, o intervalo do atendimento fechado voltava
        // a ser oferecido como livre na tela e o insert batia na trava do banco. O
        // AgendamentoService le a violacao como "esse barbeiro nao da, tenta o proximo",
        // acaba os candidatos e responde "esse horario acabou de ser ocupado" — com o
        // barbeiro livre, no dia certo, na hora certa. Pior: a sugestao seguinte saia
        // desta mesma grade e devolvia o horario que acabou de ser recusado.
        var ocupados = await db.Agendamentos
            .AsNoTracking()
            .Where(x => ids.Contains(x.BarbeiroId)
                        && x.Status != StatusAgendamento.Cancelado
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

        // MinValue deixa a comparacao de baixo sempre passar, sem precisar de um segundo
        // caminho no laco. Para quem atende, o dia inteiro esta na mesa.
        var minimoUtc = dispensarAntecedencia
            ? DateTime.MinValue
            : DateTime.UtcNow.AddMinutes(cfg.AntecedenciaMinimaMinutos);
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
        bool dispensarAntecedencia = false,
        CancellationToken ct = default)
    {
        var slots = await ObterTodosLivresAsync(
            inicioUtc, servicoId, barbeiroId, dispensarAntecedencia, ct);
        return slots.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Slot>> ObterTodosLivresAsync(
        DateTime inicioUtc,
        Guid servicoId,
        Guid? barbeiroId = null,
        bool dispensarAntecedencia = false,
        CancellationToken ct = default)
    {
        var diaLocal = DateOnly.FromDateTime(Fuso.ParaLocal(inicioUtc));
        var slots = await ObterDoDiaAsync(
            diaLocal, servicoId, barbeiroId, dispensarAntecedencia, ct);
        return slots.Where(s => s.InicioUtc == inicioUtc).ToList();
    }

    // Separa "esse horario nao existe na agenda desse dia" de "existe e esta ocupado".
    // Sem essa distincao as duas coisas chegavam como "esse horario acabou de ser
    // ocupado": um horario fora do expediente, ou que nao cai no passo da grade, ou
    // longo demais para caber antes do almoco, era anunciado como disputa por vaga.
    public async Task<bool> ExisteNaGradeAsync(
        DateTime inicioUtc,
        Guid servicoId,
        Guid? barbeiroId = null,
        bool dispensarAntecedencia = false,
        CancellationToken ct = default)
    {
        var diaLocal = DateOnly.FromDateTime(Fuso.ParaLocal(inicioUtc));
        var grade = await ObterGradeDoDiaAsync(
            diaLocal, servicoId, barbeiroId, dispensarAntecedencia, ct);
        return grade.Any(s => s.InicioUtc == inicioUtc);
    }

    public async Task<IReadOnlyList<Slot>> SugerirProximosAsync(
        DateTime desejadoUtc,
        Guid servicoId,
        Guid? barbeiroId = null,
        int quantidade = 3,
        int diasParaVarrer = 7,
        bool dispensarAntecedencia = false,
        CancellationToken ct = default)
    {
        var cfg = await configuracao.ObterBarbeariaAsync(ct);
        var candidatos = new List<Slot>();
        var diaInicial = DateOnly.FromDateTime(Fuso.ParaLocal(desejadoUtc));

        for (var i = 0; i < diasParaVarrer && candidatos.Count < quantidade * 8; i++)
        {
            var dia = diaInicial.AddDays(i);
            if (dia > Fuso.HojeLocal().AddDays(cfg.DiasMaximosNoFuturo)) break;

            candidatos.AddRange(await ObterDoDiaAsync(
                dia, servicoId, barbeiroId, dispensarAntecedencia, ct));
        }

        return candidatos
            .OrderBy(s => Math.Abs((s.InicioUtc - desejadoUtc).TotalMinutes))
            .Take(quantidade)
            .OrderBy(s => s.InicioUtc)
            .ToList();
    }

    private record Intervalo(Guid BarbeiroId, DateTime InicioUtc, DateTime FimUtc);
}
