using Barbearia.Domain;

namespace Barbearia.Application.Disponibilidade;

/// <summary>
/// Slots nao sao gravados: sao calculados a partir de expediente - agendamentos
/// - bloqueios. Materializar exigiria recalcular a tabela a cada mudanca de expediente.
/// </summary>
public record Slot(Guid BarbeiroId, string BarbeiroNome, DateTime InicioUtc, DateTime FimUtc, bool Livre = true)
{
    public DateTime InicioLocal => Fuso.ParaLocal(InicioUtc);
    public DateTime FimLocal => Fuso.ParaLocal(FimUtc);

    public string HoraFormatada => InicioLocal.ToString("HH:mm");
}
