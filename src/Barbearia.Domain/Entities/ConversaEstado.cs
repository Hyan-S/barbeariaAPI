namespace Barbearia.Domain.Entities;

/// <summary>
/// Memoria curta da conversa: guarda o horario que o bot propos, para quando o
/// cliente responder so "sim" o bot saber do que ele fala. Expira rapido porque
/// um "sim" duas horas depois nao pode agendar nada.
/// </summary>
public class ConversaEstado
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public DateTime? PropostaInicioUtc { get; set; }
    public Guid? PropostaBarbeiroId { get; set; }
    public Guid? PropostaServicoId { get; set; }

    public DateTime ExpiraEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;

    public bool TemPropostaValida(DateTime agoraUtc) =>
        PropostaInicioUtc.HasValue
        && PropostaBarbeiroId.HasValue
        && PropostaServicoId.HasValue
        && ExpiraEmUtc > agoraUtc;
}
