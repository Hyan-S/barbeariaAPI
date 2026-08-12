using System.Text;

namespace Barbearia.Domain;

public static class TelefoneBr
{
    private const string CodigoPais = "55";

    public static string? Normalizar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        var digitos = SomenteDigitos(bruto);

        if (digitos.Length is 10 or 11)
            digitos = CodigoPais + digitos;

        if (!digitos.StartsWith(CodigoPais, StringComparison.Ordinal))
            return null;

        var semPais = digitos[2..];
        if (semPais.Length < 10 || semPais.Length > 11) return null;

        var ddd = semPais[..2];
        if (!int.TryParse(ddd, out var dddNum) || dddNum < 11 || dddNum > 99) return null;

        var numero = semPais[2..];

        if (numero.Length == 8 && numero[0] >= '6' && numero[0] <= '9')
            numero = "9" + numero;

        return CodigoPais + ddd + numero;
    }

    public static IReadOnlyList<string> Variantes(string canonico)
    {
        var variantes = new List<string> { canonico };

        if (canonico.Length == 13)
        {
            var ddd = canonico.Substring(2, 2);
            var numero = canonico[4..];
            if (numero.Length == 9 && numero[0] == '9')
                variantes.Add(CodigoPais + ddd + numero[1..]);
        }

        return variantes;
    }

    public static string Formatar(string canonico)
    {
        if (canonico.Length != 13) return canonico;
        var ddd = canonico.Substring(2, 2);
        var numero = canonico[4..];
        return $"({ddd}) {numero[..5]}-{numero[5..]}";
    }

    private static string SomenteDigitos(string entrada)
    {
        var sb = new StringBuilder(entrada.Length);
        foreach (var c in entrada)
            if (char.IsAsciiDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
