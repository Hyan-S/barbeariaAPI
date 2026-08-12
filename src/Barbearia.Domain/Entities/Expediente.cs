namespace Barbearia.Domain.Entities;

public class Expediente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BarbeiroId { get; set; }
    public Barbeiro? Barbeiro { get; set; }

    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFim { get; set; }
}
