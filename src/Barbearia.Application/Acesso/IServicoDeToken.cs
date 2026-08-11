using Barbearia.Domain.Entities;

namespace Barbearia.Application.Acesso;

public interface IServicoDeToken
{
    string GerarParaBarbeiro(Barbeiro barbeiro);
}

public interface IHashDeSenha
{
    string Gerar(string senha);
    bool Conferir(string senha, string hash);
}
