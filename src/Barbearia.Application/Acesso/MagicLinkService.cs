using System.Security.Cryptography;
using System.Text;
using Barbearia.Application.Configuracao;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Barbearia.Application.Acesso;

public class MagicLinkService(IAppDbContext db, ConfiguracaoService configuracao)
{
    public async Task<string> GerarUrlAsync(Guid clienteId, CancellationToken ct = default)
    {
        var token = GerarToken();
        var minutos = await configuracao.ObterMagicLinkMinutosAsync(ct);

        db.MagicLinks.Add(new MagicLink
        {
            ClienteId = clienteId,
            TokenHash = Hash(token),
            ExpiraEmUtc = DateTime.UtcNow.AddMinutes(minutos)
        });

        await db.SaveChangesAsync(ct);

        var baseUrl = await configuracao.ObterUrlPublicaAsync(ct);
        return $"{baseUrl.TrimEnd('/')}/agendar.html?t={token}";
    }

    public async Task<Cliente?> ResolverAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = Hash(token);
        var agora = DateTime.UtcNow;

        var link = await db.MagicLinks
            .Include(x => x.Cliente)
            .FirstOrDefaultAsync(x => x.TokenHash == hash && x.ExpiraEmUtc > agora, ct);

        if (link?.Cliente is null || link.Cliente.Bloqueado) return null;

        if (link.UsadoEmUtc is null)
        {
            link.UsadoEmUtc = agora;
            await db.SaveChangesAsync(ct);
        }

        return link.Cliente;
    }

    public async Task<int> LimparExpiradosAsync(CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow.AddDays(-1);
        return await db.MagicLinks
            .Where(x => x.ExpiraEmUtc < limite)
            .ExecuteDeleteAsync(ct);
    }

    private static string GerarToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
