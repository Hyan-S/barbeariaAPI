namespace Barbearia.Domain.Entities;

/// <summary>
/// O cliente nunca cria conta: o telefone do WhatsApp e a identidade dele.
/// <see cref="Telefone"/> guarda sempre a forma canonica de <c>TelefoneBr.Normalizar</c>.
/// </summary>
public class Cliente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Telefone { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public bool Bloqueado { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    public List<Agendamento> Agendamentos { get; set; } = [];
}
