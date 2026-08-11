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
    /// <summary>Nao deu para entender. Manda o link, nao chuta.</summary>
    Baixa,

    /// <summary>Entendeu o dia ou o periodo, mas nao a hora exata.</summary>
    Media,

    /// <summary>Dia e hora explicitos. Pode propor o horario direto.</summary>
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
