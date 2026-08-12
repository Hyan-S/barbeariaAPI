using Barbearia.Domain;

namespace Barbearia.Application.Disponibilidade;

public record Slot(Guid BarbeiroId, string BarbeiroNome, DateTime InicioUtc, DateTime FimUtc, bool Livre = true)
{
    public DateTime InicioLocal => Fuso.ParaLocal(InicioUtc);
    public DateTime FimLocal => Fuso.ParaLocal(FimUtc);

    public string HoraFormatada => InicioLocal.ToString("HH:mm");
}
