using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Barbearia.Application.Acesso;
using Barbearia.Application.Configuracao;
using Barbearia.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Barbearia.Infrastructure.Acesso;

public class ServicoDeToken(IOptions<JwtOptions> options) : IServicoDeToken
{
    private readonly JwtOptions _cfg = options.Value;

    public string GerarParaBarbeiro(Barbeiro barbeiro)
    {
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg.Secret));
        var credenciais = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256);

        // Nomes curtos ("role", "name") em vez dos URIs longos da Microsoft: o handler
        // le com MapInboundClaims=false, e o token nao carrega schema desnecessario.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, barbeiro.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, barbeiro.Email),
            new("name", barbeiro.Nome),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.Add(new Claim("role", barbeiro.Perfil.ToString()));

        // Senha provisoria pendente: o token nao abre nada.
        if (barbeiro.PrecisaTrocarSenha)
            claims.Add(new Claim("trocar_senha", "1"));

        if (barbeiro.PodeGerenciarServicos) claims.Add(new Claim("perm", "servicos"));
        if (barbeiro.PodeGerenciarProdutos) claims.Add(new Claim("perm", "produtos"));
        if (barbeiro.PodeGerenciarClientes) claims.Add(new Claim("perm", "clientes"));
        if (barbeiro.PodeVerDashboard) claims.Add(new Claim("perm", "dashboard"));

        var token = new JwtSecurityToken(
            issuer: _cfg.Issuer,
            audience: _cfg.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_cfg.HorasValidade),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class HashDeSenha : IHashDeSenha
{
    // ~250ms por hash: caro para forca bruta, aceitavel num login de painel.
    private const int WorkFactor = 12;

    public string Gerar(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, WorkFactor);

    public bool Conferir(string senha, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(senha, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
}
