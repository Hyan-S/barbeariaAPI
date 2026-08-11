namespace Barbearia.Domain.Entities;

/// <summary>Folga, feriado, medico. Recorta o expediente sem virar agendamento.</summary>
public class Bloqueio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BarbeiroId { get; set; }
    public Barbeiro? Barbeiro { get; set; }

    public DateTime InicioUtc { get; set; }
    public DateTime FimUtc { get; set; }
    public string Motivo { get; set; } = string.Empty;
}
