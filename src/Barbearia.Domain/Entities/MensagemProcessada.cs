namespace Barbearia.Domain.Entities;

public class MensagemProcessada
{
    public string Id { get; set; } = string.Empty;
    public DateTime ProcessadaEmUtc { get; set; } = DateTime.UtcNow;
}
