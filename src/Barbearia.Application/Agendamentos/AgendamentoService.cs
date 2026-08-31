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
    HorarioForaDaGrade,
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

        // comoStaff atravessa ate a grade. Dispensar a antecedencia so na checagem de
        // cima nao servia para nada: quem monta os candidatos e a grade, e ela cortava
        // o mesmo horario que este metodo tinha acabado de liberar, entao o encaixe do
        // balcao caia em "esse horario acabou de ser ocupado" com a cadeira vazia.
        var candidatos = await disponibilidade.ObterTodosLivresAsync(
            inicioUtc, servicoId, barbeiroId, comoStaff, ct);

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

        // Nao gravou. Ou o horario nao existe na agenda desse dia (fora do expediente,
        // fora do passo da grade, ou longo demais para caber antes da pausa), ou existe
        // e esta tomado. Quem esta olhando a tela precisa saber qual dos dois: as duas
        // coisas viravam "acabou de ser ocupado", que soa como desculpa quando o horario
        // aparece livre na lista.
        var existeNaGrade = await disponibilidade.ExisteNaGradeAsync(
            inicioUtc, servicoId, barbeiroId, comoStaff, ct);

        var sugestoes = await disponibilidade.SugerirProximosAsync(
            inicioUtc, servicoId, barbeiroId, dispensarAntecedencia: comoStaff, ct: ct);

        return new ResultadoAgendamento(
            existeNaGrade ? ResultadoTipo.HorarioIndisponivel : ResultadoTipo.HorarioForaDaGrade,
            Sugestoes: sugestoes);
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

    // Limite da coluna clientes.Nome. Um nome maior que isso estouraria o insert
    // (e viraria HTTP 500 num endpoint publico). Trunca e remove caracteres de
    // controle antes de salvar; o escape de HTML fica a cargo de quem exibe.
    private const int MaxNome = 120;

    private static string? LimparNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;

        var limpo = new string(nome.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (limpo.Length == 0) return null;

        return limpo.Length <= MaxNome ? limpo : limpo[..MaxNome];
    }

    public async Task<Cliente> ObterOuCriarClienteAsync(
        string telefoneBruto,
        string? nome,
        CancellationToken ct = default)
    {
        var telefone = TelefoneBr.Normalizar(telefoneBruto)
                       ?? throw new ArgumentException("Telefone invalido", nameof(telefoneBruto));

        var nomeLimpo = LimparNome(nome);

        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Telefone == telefone, ct);
        if (cliente is not null)
        {
            if (string.IsNullOrWhiteSpace(cliente.Nome) && nomeLimpo is not null)
            {
                cliente.Nome = nomeLimpo;
                await db.SaveChangesAsync(ct);
            }
            return cliente;
        }

        cliente = new Cliente { Telefone = telefone, Nome = nomeLimpo ?? string.Empty };
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
