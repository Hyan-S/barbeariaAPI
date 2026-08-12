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

    /// <summary>
    /// Hash gravado com custo diferente do configurado hoje. Permite reescrever o
    /// hash no proximo login, em vez de deixar contas antigas presas ao custo velho.
    /// </summary>
    bool PrecisaRegerar(string hash);
}
