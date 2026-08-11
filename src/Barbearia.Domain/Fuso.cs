namespace Barbearia.Domain;

/// <summary>
/// Conversao entre UTC (como tudo e gravado) e o horario local da barbearia.
/// </summary>
public static class Fuso
{
    public static readonly TimeZoneInfo Barbearia = ResolverFuso();

    public static DateTime ParaLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), Barbearia);

    public static DateTime ParaUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Barbearia);

    public static DateTime AgoraLocal() => ParaLocal(DateTime.UtcNow);

    public static DateOnly HojeLocal() => DateOnly.FromDateTime(AgoraLocal());

    private static TimeZoneInfo ResolverFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        // O Brasil nao usa horario de verao desde 2019, entao UTC-3 fixo e correto.
        return TimeZoneInfo.CreateCustomTimeZone("BRT-Fallback", TimeSpan.FromHours(-3), "BRT", "BRT");
    }
}
