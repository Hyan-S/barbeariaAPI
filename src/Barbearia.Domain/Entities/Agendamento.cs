namespace Barbearia.Domain.Entities;

/// <summary>
/// Datas sempre em UTC; a conversao para America/Sao_Paulo acontece so na borda.
///
/// A garantia de que dois clientes nao pegam o mesmo horario esta numa constraint
/// EXCLUDE do Postgres, nao aqui nem no service: checagem em codigo perde a corrida
/// entre duas requisicoes simultaneas.
/// </summary>
public class Agendamento
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BarbeiroId { get; set; }
    public Barbeiro? Barbeiro { get; set; }

    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public Guid ServicoId { get; set; }
    public Servico? Servico { get; set; }

    /// <summary>
    /// Preco congelado no momento do agendamento. Sem isso, reajustar um servico
    /// reescreveria o faturamento ja realizado no dashboard.
    /// </summary>
    public int PrecoCentavos { get; set; }

    public DateTime InicioUtc { get; set; }
    public DateTime FimUtc { get; set; }

    public StatusAgendamento Status { get; set; } = StatusAgendamento.Confirmado;
    public OrigemAgendamento Origem { get; set; }
    public string? Observacao { get; set; }

    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CanceladoEmUtc { get; set; }

    public bool EstaAtivo => Status is StatusAgendamento.Pendente or StatusAgendamento.Confirmado;
}
