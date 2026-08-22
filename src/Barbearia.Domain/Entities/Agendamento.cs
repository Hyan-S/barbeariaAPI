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

    // Fechamento do atendimento: o instante em que o dinheiro entrou.
    //
    // Sem ele o sistema nunca soube dizer o que foi recebido — a "receita" era a
    // soma dos precos da agenda, entao quem faltou contava como receita para sempre
    // e nao havia como conferir com a gaveta. O valor cobrado e separado do
    // PrecoCentavos de proposito: o preco e o combinado na hora de marcar, o cobrado
    // e o que a pessoa pagou, e a diferenca entre os dois e o desconto.
    public int? ValorCobradoCentavos { get; set; }
    public FormaPagamento? FormaPagamento { get; set; }
    public DateTime? FechadoEmUtc { get; set; }
    public Guid? FechadoPorId { get; set; }

    public bool EstaFechado => FechadoEmUtc is not null;

    public bool EstaAtivo => Status is StatusAgendamento.Pendente or StatusAgendamento.Confirmado;
}
