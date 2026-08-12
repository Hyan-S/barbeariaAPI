using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Barbearia.Application.Acesso;
using Barbearia.Application.Configuracao;
using Barbearia.Domain.Entities;
using Microsoft.Extensions.Configuration;
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

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, barbeiro.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, barbeiro.Email),
            new("name", barbeiro.Nome),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.Add(new Claim("role", barbeiro.Perfil.ToString()));

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

public class HashDeSenha(IConfiguration config) : IHashDeSenha
{
    private readonly int _custo = Faixa(config.GetValue("Seguranca:BcryptWorkFactor", 10));

    public int Custo => _custo;

    public string Gerar(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, _custo);

    public bool Conferir(string senha, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(senha, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    public bool PrecisaRegerar(string hash)
    {
        var partes = hash.Split('$');
        return partes.Length < 4 || !int.TryParse(partes[2], out var custo) || custo != _custo;
    }

    private static int Faixa(int valor) => valor < 8 ? 8 : valor > 15 ? 15 : valor;
}
