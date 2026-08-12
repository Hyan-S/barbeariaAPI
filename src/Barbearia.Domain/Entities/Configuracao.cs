namespace Barbearia.Domain.Entities;

public class Configuracao
{
    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public bool Secreto { get; set; }
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
}
