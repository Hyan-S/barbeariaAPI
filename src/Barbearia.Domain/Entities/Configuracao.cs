namespace Barbearia.Domain.Entities;

/// <summary>
/// Configuracao editavel pela tela do admin, sem redeploy. Chave marcada como
/// <see cref="Secreto"/> nunca volta para o navegador.
/// </summary>
public class Configuracao
{
    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public bool Secreto { get; set; }
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
}
