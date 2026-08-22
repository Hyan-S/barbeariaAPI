namespace Barbearia.Domain;

public enum StatusAgendamento
{
    Pendente = 0,
    Confirmado = 1,
    Cancelado = 2,
    Concluido = 3
}

public enum OrigemAgendamento
{
    WhatsApp = 0,
    Web = 1,
    Painel = 2
}

public enum Perfil
{
    Admin = 0,
    Gestor = 1,
    Barbeiro = 2
}

// Como o dinheiro entrou. Sem isso o caixa do dia nao fecha com a gaveta: o dono
// precisa saber quanto foi dinheiro, quanto foi Pix e quanto cai na maquininha
// depois.
public enum FormaPagamento
{
    Dinheiro = 0,
    Pix = 1,
    Debito = 2,
    Credito = 3,
    Outro = 4
}

public enum TipoPedido
{
    Usar = 0,
    Comprar = 1
}

public static class Papeis
{
    // O papel do cliente e uma string fora do enum Perfil de proposito. As politicas
    // do painel sao escritas em cima de Perfil, entao um token de cliente nao casa
    // com nenhuma delas mesmo sendo assinado com a mesma chave.
    public const string Cliente = "Cliente";
}
