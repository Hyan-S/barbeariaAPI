namespace Barbearia.Domain.Entities;

public class Cliente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Telefone { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public bool Bloqueado { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    public List<Agendamento> Agendamentos { get; set; } = [];
}
