using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Barbearia.Domain;

namespace Barbearia.Application.WhatsApp;

public static partial class InterpretadorMensagem
{
    public static LeituraMensagem Ler(string? texto, DateTime? agoraLocal = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return new LeituraMensagem(Intencao.Desconhecida);

        var agora = agoraLocal ?? Fuso.AgoraLocal();
        var t = Normalizar(texto);

        if (RegexConfirmar().IsMatch(t))
            return new LeituraMensagem(Intencao.Confirmar, Confianca: Confianca.Alta);

        if (RegexNegar().IsMatch(t))
            return new LeituraMensagem(Intencao.Negar, Confianca: Confianca.Alta);

        if (RegexCancelar().IsMatch(t))
            return new LeituraMensagem(Intencao.Cancelar, Confianca: Confianca.Alta);

        if (RegexListar().IsMatch(t))
            return new LeituraMensagem(Intencao.ListarMeus, Confianca: Confianca.Alta);

        if (RegexPedirLink().IsMatch(t))
            return new LeituraMensagem(Intencao.PedirLink, Confianca: Confianca.Alta);

        if (RegexAjuda().IsMatch(t))
            return new LeituraMensagem(Intencao.Ajuda, Confianca: Confianca.Alta);

        var (data, restante) = ExtrairData(t, agora);
        var hora = ExtrairHora(restante);
        var periodo = ExtrairPeriodo(restante);

        if (hora.HasValue && periodo.HasValue && hora.Value.Hour is >= 1 and <= 11)
        {
            if (periodo is PeriodoDia.Tarde or PeriodoDia.Noite)
                hora = hora.Value.AddHours(12);
        }

        var querAgendar = RegexAgendar().IsMatch(t) || data.HasValue || hora.HasValue || periodo.HasValue;

        if (!querAgendar)
        {
            return RegexSaudacao().IsMatch(t)
                ? new LeituraMensagem(Intencao.Saudacao, Confianca: Confianca.Alta)
                : new LeituraMensagem(Intencao.Desconhecida);
        }

        if (hora.HasValue && !data.HasValue)
        {
            var hoje = DateOnly.FromDateTime(agora);
            data = hora.Value > TimeOnly.FromDateTime(agora) ? hoje : hoje.AddDays(1);
        }

        var confianca = (data, hora, periodo) switch
        {
            ({ }, { }, _) => Confianca.Alta,
            ({ }, null, { }) => Confianca.Media,
            ({ }, null, null) => Confianca.Media,
            _ => Confianca.Baixa
        };

        return new LeituraMensagem(Intencao.Agendar, data, hora, periodo, confianca);
    }

    private static (DateOnly? Data, string Restante) ExtrairData(string t, DateTime agora)
    {
        var hoje = DateOnly.FromDateTime(agora);
        var m = RegexDepoisDeAmanha().Match(t);
        if (m.Success) return (hoje.AddDays(2), Remover(t, m));

        m = RegexAmanha().Match(t);
        if (m.Success) return (hoje.AddDays(1), Remover(t, m));

        m = RegexHoje().Match(t);
        if (m.Success) return (hoje, Remover(t, m));

        m = RegexDataNumerica().Match(t);
        if (m.Success)
        {
            var dia = int.Parse(m.Groups[1].Value);
            var mes = int.Parse(m.Groups[2].Value);
            var ano = m.Groups[3].Success ? NormalizarAno(m.Groups[3].Value) : hoje.Year;

            if (TentarMontar(dia, mes, ano, out var data))
            {
                if (!m.Groups[3].Success && data < hoje) data = data.AddYears(1);
                return (data, Remover(t, m));
            }
        }

        m = RegexDiaDoMes().Match(t);
        if (m.Success)
        {
            var dia = int.Parse(m.Groups[1].Value);
            if (TentarMontar(dia, hoje.Month, hoje.Year, out var data))
            {
                if (data < hoje) data = data.AddMonths(1);
                return (data, Remover(t, m));
            }
        }

        m = RegexDiaSemana().Match(t);
        if (m.Success)
        {
            var alvo = MapearDiaSemana(m.Groups[1].Value);
            if (alvo.HasValue)
            {
                var delta = ((int)alvo.Value - (int)hoje.DayOfWeek + 7) % 7;
                return (hoje.AddDays(delta), Remover(t, m));
            }
        }

        return (null, t);
    }

    private static TimeOnly? ExtrairHora(string t)
    {
        if (RegexMeioDia().IsMatch(t)) return new TimeOnly(12, 0);
        if (RegexMeiaNoite().IsMatch(t)) return new TimeOnly(0, 0);

        var m = RegexHoraMinuto().Match(t);
        if (m.Success && TentarHora(m.Groups[1].Value, m.Groups[2].Value, out var hm))
            return hm;

        m = RegexHoraCheia().Match(t);
        if (m.Success && TentarHora(m.Groups[1].Value, "0", out var hc))
            return hc;

        m = RegexHoraComPreposicao().Match(t);
        if (m.Success && TentarHora(m.Groups[1].Value, "0", out var hp))
            return hp;

        m = RegexHoraComPeriodo().Match(t);
        if (m.Success && TentarHora(m.Groups[1].Value, "0", out var hpp))
            return hpp;

        return null;
    }

    private static PeriodoDia? ExtrairPeriodo(string t)
    {
        if (RegexManha().IsMatch(t)) return PeriodoDia.Manha;
        if (RegexTarde().IsMatch(t)) return PeriodoDia.Tarde;
        if (RegexNoite().IsMatch(t)) return PeriodoDia.Noite;
        return null;
    }

    private static bool TentarHora(string h, string min, out TimeOnly hora)
    {
        hora = default;
        if (!int.TryParse(h, out var hh) || !int.TryParse(min, out var mm)) return false;
        if (hh is < 0 or > 23 || mm is < 0 or > 59) return false;
        hora = new TimeOnly(hh, mm);
        return true;
    }

    private static bool TentarMontar(int dia, int mes, int ano, out DateOnly data)
    {
        data = default;
        if (mes is < 1 or > 12 || dia < 1) return false;
        if (dia > DateTime.DaysInMonth(ano, mes)) return false;
        data = new DateOnly(ano, mes, dia);
        return true;
    }

    private static int NormalizarAno(string bruto)
    {
        var ano = int.Parse(bruto);
        return ano < 100 ? 2000 + ano : ano;
    }

    private static DayOfWeek? MapearDiaSemana(string nome) => nome switch
    {
        "domingo" => DayOfWeek.Sunday,
        "segunda" => DayOfWeek.Monday,
        "terca" => DayOfWeek.Tuesday,
        "quarta" => DayOfWeek.Wednesday,
        "quinta" => DayOfWeek.Thursday,
        "sexta" => DayOfWeek.Friday,
        "sabado" => DayOfWeek.Saturday,
        _ => null
    };

    private static string Remover(string texto, Match m) =>
        texto.Remove(m.Index, m.Length).Insert(m.Index, " ");

    private static string Normalizar(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);

        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"^(sim|s|isso|confirmo?|confirmar|pode ser|fechado|ok|blz|beleza|perfeito|show|ta bom|tá bom|quero esse|esse mesmo|1)$")]
    private static partial Regex RegexConfirmar();

    [GeneratedRegex(@"^(nao|n|negativo|outro|outro horario|nao quero|nem|2)$")]
    private static partial Regex RegexNegar();

    [GeneratedRegex(@"\b(cancelar|cancela|desmarcar|desmarca|desistir)\b")]
    private static partial Regex RegexCancelar();

    [GeneratedRegex(@"\b(meus agendamentos|meu agendamento|meus horarios|ja tenho|quando e meu|qual meu horario)\b")]
    private static partial Regex RegexListar();

    [GeneratedRegex(@"\b(link|site|sistema|agenda completa|ver horarios|ver os horarios|horarios disponiveis|ver disponibilidade|quero escolher|quero ver)\b")]
    private static partial Regex RegexPedirLink();

    [GeneratedRegex(@"\b(ajuda|help|menu|opcoes|como funciona|o que voce faz)\b")]
    private static partial Regex RegexAjuda();

    [GeneratedRegex(@"\b(agendar|agenda|marcar|marca|horario|cortar|corte|barba|atendimento|queria|quero)\b")]
    private static partial Regex RegexAgendar();

    [GeneratedRegex(@"\b(oi|ola|bom dia|boa tarde|boa noite|eai|e ai|opa|salve)\b")]
    private static partial Regex RegexSaudacao();

    [GeneratedRegex(@"\bdepois de amanha\b")]
    private static partial Regex RegexDepoisDeAmanha();

    [GeneratedRegex(@"\bamanha\b")]
    private static partial Regex RegexAmanha();

    [GeneratedRegex(@"\bhoje\b")]
    private static partial Regex RegexHoje();

    [GeneratedRegex(@"\b(\d{1,2})\s*/\s*(\d{1,2})(?:\s*/\s*(\d{2,4}))?\b")]
    private static partial Regex RegexDataNumerica();

    [GeneratedRegex(@"\bdia\s+(\d{1,2})\b")]
    private static partial Regex RegexDiaDoMes();

    [GeneratedRegex(@"\b(domingo|segunda|terca|quarta|quinta|sexta|sabado)(?:\s*-?\s*feira)?\b")]
    private static partial Regex RegexDiaSemana();

    [GeneratedRegex(@"\bmeio\s*-?\s*dia\b")]
    private static partial Regex RegexMeioDia();

    [GeneratedRegex(@"\bmeia\s*-?\s*noite\b")]
    private static partial Regex RegexMeiaNoite();

    [GeneratedRegex(@"\b(\d{1,2})\s*[:h]\s*(\d{2})\b")]
    private static partial Regex RegexHoraMinuto();

    [GeneratedRegex(@"\b(\d{1,2})\s*h(?:s|rs|oras?)?\b")]
    private static partial Regex RegexHoraCheia();

    [GeneratedRegex(@"\b(?:as|a)\s+(\d{1,2})\b")]
    private static partial Regex RegexHoraComPreposicao();

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:da|de)\s+(?:manha|tarde|noite)\b")]
    private static partial Regex RegexHoraComPeriodo();

    [GeneratedRegex(@"\bmanha\b")]
    private static partial Regex RegexManha();

    [GeneratedRegex(@"\btarde\b")]
    private static partial Regex RegexTarde();

    [GeneratedRegex(@"\bnoite\b")]
    private static partial Regex RegexNoite();
}
