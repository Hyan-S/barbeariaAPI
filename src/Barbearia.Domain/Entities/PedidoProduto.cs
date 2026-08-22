namespace Barbearia.Domain.Entities;

// O que o cliente marcou na vitrine que aparece depois de confirmar o horario:
// "usa esse no meu corte" ou "quero levar". Fica preso ao agendamento, entao o
// barbeiro ve o pedido na linha da agenda e chega no atendimento sabendo.
// Enquanto Vendido e false continua sendo so o recado: nao baixa estoque nem
// entra em faturamento. Quem transforma em venda e o fechamento do atendimento,
// onde o barbeiro confirma o que saiu de verdade.
public class PedidoProduto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AgendamentoId { get; set; }
    public Agendamento? Agendamento { get; set; }

    public Guid ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public TipoPedido Tipo { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    // Confirmado no fechamento. O preco fica congelado aqui e nao lido do produto:
    // tabela de preco muda, e o que entrou no caixa naquele dia nao pode mudar
    // junto. Quantidade existe porque o indice unico impede duas linhas do mesmo
    // produto no mesmo atendimento.
    public bool Vendido { get; set; }
    public int Quantidade { get; set; } = 1;
    public int? PrecoCentavosNaVenda { get; set; }
}
