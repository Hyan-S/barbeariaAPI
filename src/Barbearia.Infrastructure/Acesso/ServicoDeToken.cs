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

public class HashDeSenha(IConfiguration config) : IHashDeSenha
{
    // Cada ponto dobra o custo. 12 leva ~250ms num PC comum, mas o plano free do
    // Render da 0,1 de CPU e o mesmo calculo passa de 2s — o usuario sente o login
    // travar. 10 e o minimo recomendado pela OWASP e cai para ~0,5s aqui; com o
    // rate limit de 8 tentativas por minuto, a forca bruta continua inviavel.
    // Suba de novo (Seguranca__BcryptWorkFactor) se um dia sair do plano free.
    private readonly int _custo = Faixa(config.GetValue("Seguranca:BcryptWorkFactor", 10));

    public int Custo => _custo;

    public string Gerar(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, _custo);

    public bool Conferir(string senha, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(senha, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    /// <summary>Hash antigo, gerado com custo diferente do atual.</summary>
    public bool PrecisaRegerar(string hash)
    {
        // Formato: $2a$<custo>$<salt+hash>
        var partes = hash.Split('$');
        return partes.Length < 4 || !int.TryParse(partes[2], out var custo) || custo != _custo;
    }

    private static int Faixa(int valor) => valor < 8 ? 8 : valor > 15 ? 15 : valor;
}
