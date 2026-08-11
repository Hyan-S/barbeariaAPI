using System.Security.Cryptography;
using System.Text;

namespace Barbearia.Infrastructure.WhatsApp;

/// <summary>
/// Sem esta conferencia o webhook aceitaria qualquer JSON, e daria para forjar
/// mensagem de qualquer numero.
/// </summary>
public static class ValidadorAssinatura
{
    public static bool Conferir(ReadOnlySpan<byte> corpoBruto, string? headerAssinatura, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(appSecret)) return false;
        if (string.IsNullOrWhiteSpace(headerAssinatura)) return false;

        const string prefixo = "sha256=";
        if (!headerAssinatura.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) return false;

        Span<byte> calculada = stackalloc byte[32];
        if (!HMACSHA256.TryHashData(Encoding.UTF8.GetBytes(appSecret), corpoBruto, calculada, out _))
            return false;

        byte[] esperado;
        try { esperado = Convert.FromHexString(headerAssinatura[prefixo.Length..]); }
        catch (FormatException) { return false; }

        return CryptographicOperations.FixedTimeEquals(calculada, esperado);
    }

    public static bool ConferirVerifyToken(string? recebido, string esperado) =>
        !string.IsNullOrWhiteSpace(esperado)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recebido ?? string.Empty),
            Encoding.UTF8.GetBytes(esperado));
}
