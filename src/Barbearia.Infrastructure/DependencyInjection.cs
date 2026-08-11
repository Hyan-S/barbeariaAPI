using Barbearia.Application;
using Barbearia.Application.Acesso;
using Barbearia.Application.Agendamentos;
using Barbearia.Application.Configuracao;
using Barbearia.Application.Disponibilidade;
using Barbearia.Application.WhatsApp;
using Barbearia.Infrastructure.Acesso;
using Barbearia.Infrastructure.Data;
using Barbearia.Infrastructure.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Barbearia.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBarbearia(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<BarbeariaOptions>(config.GetSection(BarbeariaOptions.Secao));
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Secao));
        services.Configure<AppOptions>(config.GetSection(AppOptions.Secao));
        services.Configure<WhatsAppOptions>(config.GetSection(WhatsAppOptions.Secao));

        var conexao = config.GetConnectionString("Postgres")
                      ?? throw new InvalidOperationException(
                          "Connection string 'Postgres' nao configurada. " +
                          "Defina ConnectionStrings__Postgres nas variaveis de ambiente.");

        services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(conexao, npgsql =>
        {
            // O Postgres gratuito hiberna e a primeira query depois de um tempo
            // parado falha; o retry evita o erro chegar no cliente.
            npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(3), null);
        }));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDetectorDeConflito, PostgresDetectorDeConflito>();

        services.AddScoped<ConfiguracaoService>();
        services.AddScoped<DisponibilidadeService>();
        services.AddScoped<AgendamentoService>();
        services.AddScoped<MagicLinkService>();
        services.AddScoped<ConversaService>();

        services.AddSingleton<IServicoDeToken, ServicoDeToken>();
        services.AddSingleton<IHashDeSenha, HashDeSenha>();

        services.AddHttpClient<IWhatsAppClient, WhatsAppClient>(http =>
        {
            http.BaseAddress = new Uri("https://graph.facebook.com/");
            http.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
