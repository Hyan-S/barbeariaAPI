namespace Barbearia.Domain.Entities;

// O que o cliente marcou na vitrine que aparece depois de confirmar o horario:
// "usa esse no meu corte" ou "quero levar". Fica preso ao agendamento, entao o
// barbeiro ve o pedido na linha da agenda e chega no atendimento sabendo.
// Nao e venda: nao baixa estoque nem entra no faturamento previsto.
public class PedidoProduto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgendamentoId { get; set; }
    public Agendamento? Agendamento { get; set; }

    public Guid ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public TipoPedido Tipo { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
}
