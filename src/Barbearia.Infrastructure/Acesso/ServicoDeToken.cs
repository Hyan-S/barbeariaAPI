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

        var agora = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, barbeiro.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, barbeiro.Email),
            new("name", barbeiro.Nome),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

            // Quando este token nasceu, em segundos. O guarda de sessao compara com
            // o selo do usuario no banco: token mais velho que o selo esta morto.
            new("emitido", new DateTimeOffset(agora).ToUnixTimeSeconds().ToString())
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
            expires: agora.AddHours(_cfg.HorasValidade),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Token do cliente. O papel e a string "Cliente", que de proposito nao existe no
    // enum Perfil: nenhuma politica do painel (Admin, Gestao, Painel, Servicos,
    // Produtos, Clientes, Dashboard) casa com ela, entao este token nao abre nada da
    // area da barbearia mesmo sendo assinado com a mesma chave.
    public string GerarParaCliente(Cliente cliente)
    {
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg.Secret));
        var credenciais = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256);

        var agora = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, cliente.Id.ToString()),
            new("name", cliente.Nome),
            new("role", Barbearia.Domain.Papeis.Cliente),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("emitido", new DateTimeOffset(agora).ToUnixTimeSeconds().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _cfg.Issuer,
            audience: _cfg.Audience,
            claims: claims,
            expires: agora.AddHours(_cfg.HorasValidade),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class HashDeSenha(IConfiguration config) : IHashDeSenha
{
    private readonly int _custo = CustoDe(config);

    // Hash de descarte, gerado uma vez por processo e com o mesmo custo dos de
    // verdade. Nao guarda a senha de ninguem: existe so para o Conferir ter contra
    // o que trabalhar quando a conta nao existe. O Lazy evita pagar o BCrypt na
    // subida da aplicacao — ele so nasce na primeira tentativa de login perdida.
    private readonly Lazy<string> _descarte = new(() =>
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), CustoDe(config)));

    public int Custo => _custo;

    public string Gerar(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, _custo);

    public bool Conferir(string senha, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(senha, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    public bool ConferirEmVao(string senha)
    {
        Conferir(senha, _descarte.Value);
        return false;
    }

    public bool PrecisaRegerar(string hash)
    {
        var partes = hash.Split('$');
        return partes.Length < 4 || !int.TryParse(partes[2], out var custo) || custo != _custo;
    }

    private static int CustoDe(IConfiguration config) =>
        Faixa(config.GetValue("Seguranca:BcryptWorkFactor", 10));

    private static int Faixa(int valor) => valor < 8 ? 8 : valor > 15 ? 15 : valor;
}
