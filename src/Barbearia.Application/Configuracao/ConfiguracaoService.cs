using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Barbearia.Application.Configuracao;

/// <summary>
/// Configuracao vinda do banco (editavel na tela do admin) com fallback para as
/// variaveis de ambiente. O banco vence quando preenchido.
/// </summary>
public class ConfiguracaoService(
    IAppDbContext db,
    IOptions<WhatsAppOptions> envWhatsApp,
    IOptions<AppOptions> envApp,
    IOptions<BarbeariaOptions> envBarbearia)
{
    public const string WhatsAppHabilitado = "whatsapp.habilitado";
    public const string WhatsAppVerifyToken = "whatsapp.verifyToken";
    public const string WhatsAppAppSecret = "whatsapp.appSecret";
    public const string WhatsAppAccessToken = "whatsapp.accessToken";
    public const string WhatsAppPhoneNumberId = "whatsapp.phoneNumberId";
    public const string WhatsAppNumeroExibicao = "whatsapp.numeroExibicao";
    public const string WhatsAppNumerosPermitidos = "whatsapp.numerosPermitidos";

    public const string AppUrlPublica = "app.urlPublica";
    public const string AppMagicLinkMinutos = "app.magicLinkMinutos";
    public const string LimiteUsuarios = "sistema.limiteUsuarios";
    public const string BarbeariaNome = "barbearia.nome";

    public const string IntervaloSlot = "barbearia.intervaloSlotMinutos";
    public const string AntecedenciaMinima = "barbearia.antecedenciaMinimaMinutos";
    public const string DiasMaximos = "barbearia.diasMaximosNoFuturo";
    public const string HorasParaCancelar = "barbearia.horasMinimasParaCancelar";
    public const string MaxPorCliente = "barbearia.maxAgendamentosPorCliente";

    public static readonly string[] ChavesSecretas = [WhatsAppAppSecret, WhatsAppAccessToken];

    public async Task<WhatsAppOptions> ObterWhatsAppAsync(CancellationToken ct = default)
    {
        var mapa = await LerAsync(ct);
        var env = envWhatsApp.Value;

        return new WhatsAppOptions
        {
            Habilitado = Ler(mapa, WhatsAppHabilitado) is { } h ? h == "true" : env.Habilitado,
            VerifyToken = Ler(mapa, WhatsAppVerifyToken) ?? env.VerifyToken,
            AppSecret = Ler(mapa, WhatsAppAppSecret) ?? env.AppSecret,
            AccessToken = Ler(mapa, WhatsAppAccessToken) ?? env.AccessToken,
            PhoneNumberId = Ler(mapa, WhatsAppPhoneNumberId) ?? env.PhoneNumberId,
            NumeroExibicao = Ler(mapa, WhatsAppNumeroExibicao) ?? env.NumeroExibicao,
            NumerosPermitidos = Ler(mapa, WhatsAppNumerosPermitidos) ?? env.NumerosPermitidos,
            ApiVersion = env.ApiVersion
        };
    }

    public async Task<BarbeariaOptions> ObterBarbeariaAsync(CancellationToken ct = default)
    {
        var mapa = await LerAsync(ct);
        var env = envBarbearia.Value;

        return new BarbeariaOptions
        {
            IntervaloSlotMinutos = Inteiro(mapa, IntervaloSlot, env.IntervaloSlotMinutos, 5, 120),
            AntecedenciaMinimaMinutos = Inteiro(mapa, AntecedenciaMinima, env.AntecedenciaMinimaMinutos, 0, 1440),
            DiasMaximosNoFuturo = Inteiro(mapa, DiasMaximos, env.DiasMaximosNoFuturo, 1, 365),
            HorasMinimasParaCancelar = Inteiro(mapa, HorasParaCancelar, env.HorasMinimasParaCancelar, 0, 168),
            MaxAgendamentosPorCliente = Inteiro(mapa, MaxPorCliente, env.MaxAgendamentosPorCliente, 1, 50)
        };
    }

    public async Task<string> ObterUrlPublicaAsync(CancellationToken ct = default)
    {
        var mapa = await LerAsync(ct);
        return Ler(mapa, AppUrlPublica) ?? envApp.Value.UrlPublica;
    }

    public async Task<int> ObterMagicLinkMinutosAsync(CancellationToken ct = default)
    {
        var mapa = await LerAsync(ct);
        return Inteiro(mapa, AppMagicLinkMinutos, envApp.Value.MagicLinkMinutosValidade, 5, 1440);
    }

    public async Task<int> ObterLimiteUsuariosAsync(CancellationToken ct = default)
    {
        var mapa = await LerAsync(ct);
        return Inteiro(mapa, LimiteUsuarios, 5, 1, 500);
    }

    public async Task<string> ObterNomeBarbeariaAsync(CancellationToken ct = default)
    {
        var mapa = await LerAsync(ct);
        return Ler(mapa, BarbeariaNome) ?? "Barbearia";
    }

    /// <summary>Para a tela do admin: segredo vira apenas "preenchido: sim/nao".</summary>
    public async Task<Dictionary<string, object>> ObterParaTelaAsync(CancellationToken ct = default)
    {
        var whatsapp = await ObterWhatsAppAsync(ct);
        var barbearia = await ObterBarbeariaAsync(ct);

        return new Dictionary<string, object>
        {
            [BarbeariaNome] = await ObterNomeBarbeariaAsync(ct),
            [AppUrlPublica] = await ObterUrlPublicaAsync(ct),
            [AppMagicLinkMinutos] = await ObterMagicLinkMinutosAsync(ct),
            [LimiteUsuarios] = await ObterLimiteUsuariosAsync(ct),

            [IntervaloSlot] = barbearia.IntervaloSlotMinutos,
            [AntecedenciaMinima] = barbearia.AntecedenciaMinimaMinutos,
            [DiasMaximos] = barbearia.DiasMaximosNoFuturo,
            [HorasParaCancelar] = barbearia.HorasMinimasParaCancelar,
            [MaxPorCliente] = barbearia.MaxAgendamentosPorCliente,

            [WhatsAppHabilitado] = whatsapp.Habilitado,
            [WhatsAppVerifyToken] = whatsapp.VerifyToken,
            [WhatsAppPhoneNumberId] = whatsapp.PhoneNumberId,
            [WhatsAppNumeroExibicao] = whatsapp.NumeroExibicao,
            [WhatsAppNumerosPermitidos] = whatsapp.NumerosPermitidos,
            ["whatsapp.appSecretPreenchido"] = !string.IsNullOrWhiteSpace(whatsapp.AppSecret),
            ["whatsapp.accessTokenPreenchido"] = !string.IsNullOrWhiteSpace(whatsapp.AccessToken),
            ["whatsapp.pronto"] = whatsapp.EstaConfigurado()
        };
    }

    /// <summary>Chave com valor vazio e ignorada: nao apaga segredo sem querer.</summary>
    public async Task SalvarAsync(IDictionary<string, string?> valores, CancellationToken ct = default)
    {
        var existentes = await db.Configuracoes.ToDictionaryAsync(x => x.Chave, ct);

        foreach (var (chave, valor) in valores)
        {
            if (valor is null) continue;
            if (ChavesSecretas.Contains(chave) && string.IsNullOrWhiteSpace(valor)) continue;

            if (existentes.TryGetValue(chave, out var atual))
            {
                atual.Valor = valor;
                atual.AtualizadoEmUtc = DateTime.UtcNow;
            }
            else
            {
                db.Configuracoes.Add(new Domain.Entities.Configuracao
                {
                    Chave = chave,
                    Valor = valor,
                    Secreto = ChavesSecretas.Contains(chave)
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, string>> LerAsync(CancellationToken ct) =>
        await db.Configuracoes.AsNoTracking().ToDictionaryAsync(x => x.Chave, x => x.Valor, ct);

    private static string? Ler(Dictionary<string, string> mapa, string chave) =>
        mapa.TryGetValue(chave, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>Valor fora da faixa cai no padrao — evita 0 minutos de slot travar a agenda.</summary>
    private static int Inteiro(Dictionary<string, string> mapa, string chave, int padrao, int min, int max)
    {
        if (Ler(mapa, chave) is not { } bruto || !int.TryParse(bruto, out var valor)) return padrao;
        return valor < min || valor > max ? padrao : valor;
    }
}
