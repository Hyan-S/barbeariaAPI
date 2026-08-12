namespace Barbearia.Application.Configuracao;

public class BarbeariaOptions
{
    public const string Secao = "Barbearia";

    public int IntervaloSlotMinutos { get; set; } = 15;
    public int AntecedenciaMinimaMinutos { get; set; } = 30;
    public int DiasMaximosNoFuturo { get; set; } = 60;
    public int HorasMinimasParaCancelar { get; set; } = 2;
    public int MaxAgendamentosPorCliente { get; set; } = 3;
}

public class JwtOptions
{
    public const string Secao = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "barbearia-api";
    public string Audience { get; set; } = "barbearia-painel";
    public int HorasValidade { get; set; } = 8;
}

public class AppOptions
{
    public const string Secao = "App";

    public string UrlPublica { get; set; } = "http://localhost:5173";

    public string OrigensPermitidas { get; set; } = "http://localhost:5173";

    public int MagicLinkMinutosValidade { get; set; } = 30;

    public string[] OrigensComoLista() =>
        OrigensPermitidas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class WhatsAppOptions
{
    public const string Secao = "WhatsApp";

    public bool Habilitado { get; set; }
    public string VerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;

    public string PhoneNumberId { get; set; } = string.Empty;

    public string NumeroExibicao { get; set; } = string.Empty;

    public string NumerosPermitidos { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v21.0";

    public bool PodeAtender(string telefoneCanonico)
    {
        if (string.IsNullOrWhiteSpace(NumerosPermitidos)) return true;

        return NumerosPermitidos
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Domain.TelefoneBr.Normalizar)
            .Any(n => n == telefoneCanonico);
    }

    public bool EstaConfigurado() =>
        Habilitado
        && !string.IsNullOrWhiteSpace(AppSecret)
        && !string.IsNullOrWhiteSpace(AccessToken)
        && !string.IsNullOrWhiteSpace(PhoneNumberId);
}
