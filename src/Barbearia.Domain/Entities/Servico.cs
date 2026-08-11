namespace Barbearia.Domain.Entities;

public class Servico
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public int DuracaoMinutos { get; set; }

    /// <summary>Em centavos, para nao arrastar erro de arredondamento.</summary>
    public int PrecoCentavos { get; set; }

    public bool Ativo { get; set; } = true;
}
