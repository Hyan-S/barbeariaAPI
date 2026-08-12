namespace Barbearia.Application.WhatsApp;

public enum Intencao
{
    Desconhecida,
    Saudacao,
    Agendar,
    PedirLink,
    Cancelar,
    ListarMeus,
    Confirmar,
    Negar,
    Ajuda
}

public enum PeriodoDia
{
    Manha,
    Tarde,
    Noite
}

public enum Confianca
{
    Baixa,

    Media,

    Alta
}

public record LeituraMensagem(
    Intencao Intencao,
    DateOnly? Data = null,
    TimeOnly? Hora = null,
    PeriodoDia? Periodo = null,
    Confianca Confianca = Confianca.Baixa)
{
    public bool TemHorarioExato => Data.HasValue && Hora.HasValue;
}
