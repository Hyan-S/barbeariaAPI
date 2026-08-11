namespace Barbearia.Domain.Entities;

/// <summary>
/// Janela de trabalho recorrente, em horario local. Um barbeiro pode ter varias
/// no mesmo dia (ex.: 09:00-12:00 e 14:00-19:00).
/// </summary>
public class Expediente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BarbeiroId { get; set; }
    public Barbeiro? Barbeiro { get; set; }

    public DayOfWeek DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFim { get; set; }
}
