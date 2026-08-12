namespace Barbearia.Domain.Entities;

public class Servico
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public int DuracaoMinutos { get; set; }

    public int PrecoCentavos { get; set; }

    public bool Ativo { get; set; } = true;
}
