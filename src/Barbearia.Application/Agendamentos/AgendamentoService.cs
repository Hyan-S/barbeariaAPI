using Barbearia.Application.Configuracao;
using Barbearia.Application.Disponibilidade;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Barbearia.Application.Agendamentos;

public enum ResultadoTipo
{
    Criado,
    HorarioIndisponivel,
    ForaDaAntecedencia,
    ForaDaJanelaDeAgenda,
    ServicoInvalido,
    LimiteDeAgendamentosAtingido,
    ClienteBloqueado
}

public record ResultadoAgendamento(
    ResultadoTipo Tipo,
    Agendamento? Agendamento = null,
    IReadOnlyList<Slot>? Sugestoes = null)
{
    public bool Sucesso => Tipo == ResultadoTipo.Criado;
}

public class AgendamentoService(
    IAppDbContext db,
    DisponibilidadeService disponibilidade,
    IDetectorDeConflito detector,
    ConfiguracaoService configuracao,
    ILogger<AgendamentoService> logger)
{
    public async Task<ResultadoAgendamento> CriarAsync(
        Guid clienteId,
        Guid servicoId,
        DateTime inicioUtc,
        Guid? barbeiroId,
        OrigemAgendamento origem,
        string? observacao = null,
        bool comoStaff = false,
        CancellationToken ct = default)
    {
        var cfg = await configuracao.ObterBarbeariaAsync(ct);

        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null || cliente.Bloqueado)
            return new ResultadoAgendamento(ResultadoTipo.ClienteBloqueado);

        var servico = await db.Servicos.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == servicoId && s.Ativo, ct);
        if (servico is null)
            return new ResultadoAgendamento(ResultadoTipo.ServicoInvalido);

        var agora = DateTime.UtcNow;

        if (!comoStaff && inicioUtc < agora.AddMinutes(cfg.AntecedenciaMinimaMinutos))
            return new ResultadoAgendamento(ResultadoTipo.ForaDaAntecedencia);

        if (inicioUtc > agora.AddDays(cfg.DiasMaximosNoFuturo))
            return new ResultadoAgendamento(ResultadoTipo.ForaDaJanelaDeAgenda);

        if (!comoStaff)
        {
            var ativos = await db.Agendamentos.CountAsync(
                a => a.ClienteId == clienteId
                     && a.InicioUtc > agora
                     && (a.Status == StatusAgendamento.Pendente || a.Status == StatusAgendamento.Confirmado),
                ct);

            if (ativos >= cfg.MaxAgendamentosPorCliente)
                return new ResultadoAgendamento(ResultadoTipo.LimiteDeAgendamentosAtingido);
        }

        var candidatos = await disponibilidade.ObterTodosLivresAsync(inicioUtc, servicoId, barbeiroId, ct);

        foreach (var slot in candidatos)
        {
            var agendamento = new Agendamento
            {
                BarbeiroId = slot.BarbeiroId,
                ClienteId = clienteId,
                ServicoId = servicoId,
                PrecoCentavos = servico.PrecoCentavos,
                InicioUtc = slot.InicioUtc,
                FimUtc = slot.FimUtc,
                Status = StatusAgendamento.Confirmado,
                Origem = origem,
                Observacao = observacao
            };

            db.Agendamentos.Add(agendamento);

            try
            {
                await db.SaveChangesAsync(ct);
                return new ResultadoAgendamento(ResultadoTipo.Criado, agendamento);
            }
            catch (Exception ex) when (detector.EhConflitoDeHorario(ex))
            {
                logger.LogInformation(
                    "Conflito de horario ao agendar {Inicio} para barbeiro {Barbeiro}",
                    slot.InicioUtc, slot.BarbeiroId);

                db.Agendamentos.Remove(agendamento);
            }
        }

        var sugestoes = await disponibilidade.SugerirProximosAsync(
            inicioUtc, servicoId, barbeiroId, ct: ct);

        return new ResultadoAgendamento(ResultadoTipo.HorarioIndisponivel, Sugestoes: sugestoes);
    }

    public async Task<bool> CancelarAsync(
        Guid agendamentoId,
        Guid? clienteIdParaValidar,
        bool exigirAntecedencia,
        CancellationToken ct = default)
    {
        var agendamento = await db.Agendamentos
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, ct);

        if (agendamento is null || !agendamento.EstaAtivo) return false;

        if (clienteIdParaValidar.HasValue && agendamento.ClienteId != clienteIdParaValidar.Value)
            return false;

        if (exigirAntecedencia)
        {
            var cfg = await configuracao.ObterBarbeariaAsync(ct);
            if (agendamento.InicioUtc < DateTime.UtcNow.AddHours(cfg.HorasMinimasParaCancelar))
                return false;
        }

        agendamento.Status = StatusAgendamento.Cancelado;
        agendamento.CanceladoEmUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Cliente> ObterOuCriarClienteAsync(
        string telefoneBruto,
        string? nome,
        CancellationToken ct = default)
    {
        var telefone = TelefoneBr.Normalizar(telefoneBruto)
                       ?? throw new ArgumentException("Telefone invalido", nameof(telefoneBruto));

        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Telefone == telefone, ct);
        if (cliente is not null)
        {
            if (string.IsNullOrWhiteSpace(cliente.Nome) && !string.IsNullOrWhiteSpace(nome))
            {
                cliente.Nome = nome;
                await db.SaveChangesAsync(ct);
            }
            return cliente;
        }

        cliente = new Cliente { Telefone = telefone, Nome = nome ?? string.Empty };
        db.Clientes.Add(cliente);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (detector.EhConflitoDeHorario(ex))
        {
            db.Clientes.Remove(cliente!);
            cliente = await db.Clientes.FirstAsync(c => c.Telefone == telefone, ct);
        }

        return cliente;
    }
}
