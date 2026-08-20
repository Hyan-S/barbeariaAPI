using Barbearia.Domain.Entities;

namespace Barbearia.Application.Acesso;

public interface IServicoDeToken
{
    string GerarParaBarbeiro(Barbeiro barbeiro);
    string GerarParaCliente(Cliente cliente);
}

public interface IHashDeSenha
{
    string Gerar(string senha);
    bool Conferir(string senha, string hash);

    bool PrecisaRegerar(string hash);

    // Gasta o mesmo tempo de um Conferir de verdade e sempre devolve false. Serve
    // para o login nao entregar pelo relogio quais contas existem: sem isso, um
    // e-mail inexistente responde em milissegundos (o BCrypt nunca roda) e um
    // e-mail real demora o custo do hash.
    bool ConferirEmVao(string senha);
}
