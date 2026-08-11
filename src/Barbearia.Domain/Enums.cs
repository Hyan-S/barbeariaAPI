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

/// <summary>
/// Admin: dono do sistema, mexe em integracao e cria contas.
/// Gestor: dono da barbearia. Barbeiro: so a propria agenda.
/// </summary>
public enum Perfil
{
    Admin = 0,
    Gestor = 1,
    Barbeiro = 2
}
