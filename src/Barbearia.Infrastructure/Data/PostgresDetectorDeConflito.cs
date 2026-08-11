using Barbearia.Application;
using Npgsql;

namespace Barbearia.Infrastructure.Data;

public class PostgresDetectorDeConflito : IDetectorDeConflito
{
    private const string ExclusionViolation = "23P01";
    private const string UniqueViolation = "23505";

    public bool EhConflitoDeHorario(Exception excecao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is PostgresException pg)
                return pg.SqlState is ExclusionViolation or UniqueViolation;
        }

        return false;
    }
}
