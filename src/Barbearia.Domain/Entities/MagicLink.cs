namespace Barbearia.Domain.Entities;

/// <summary>
/// Acesso sem senha enviado pelo WhatsApp. So o hash fica no banco: se ele vazar,
/// os links nao sao reutilizaveis.
/// </summary>
public class MagicLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiraEmUtc { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsadoEmUtc { get; set; }
}
