namespace Barbearia.Application;

public interface IDetectorDeConflito
{
    bool EhConflitoDeHorario(Exception excecao);
}
