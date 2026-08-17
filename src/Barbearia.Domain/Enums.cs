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

public enum TipoPedido
{
    Usar = 0,
    Comprar = 1
}
