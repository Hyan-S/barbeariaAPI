namespace Barbearia.Domain.Entities;

public class Agendamento
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BarbeiroId { get; set; }
    public Barbeiro? Barbeiro { get; set; }

    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public Guid ServicoId { get; set; }
    public Servico? Servico { get; set; }

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
