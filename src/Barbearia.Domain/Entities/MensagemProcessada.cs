namespace Barbearia.Domain.Entities;

/// <summary>
/// Idempotencia do webhook: a Meta reentrega a mesma mensagem quando nao recebe
/// 200 a tempo, e sem isso um timeout vira agendamento duplicado.
/// </summary>
public class MensagemProcessada
{
    /// <summary>Id vindo da Meta (wamid...).</summary>
    public string Id { get; set; } = string.Empty;
    public DateTime ProcessadaEmUtc { get; set; } = DateTime.UtcNow;
}
