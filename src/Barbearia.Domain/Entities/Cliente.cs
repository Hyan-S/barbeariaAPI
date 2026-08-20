namespace Barbearia.Domain.Entities;

public class Cliente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Telefone { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public bool Bloqueado { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    // Nulo em quem nasceu pelo balcao ou pelo WhatsApp: essas pessoas existem como
    // cliente mas nunca criaram acesso. Quem tem hash consegue entrar em
    // agendar.html com telefone e senha, ver os proprios horarios e cancelar.
    public string? SenhaHash { get; set; }

    public DateTime? SenhaDefinidaEmUtc { get; set; }

    // Mesmo corte de sessao do funcionario: token emitido antes deste selo nao vale
    // mais. E o que faz trocar a senha derrubar quem estava logado em outro
    // aparelho. Aqui nao precisa de cache: o Atual() ja le o cliente a cada chamada.
    public DateTime TokensValidosDesdeUtc { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public List<Agendamento> Agendamentos { get; set; } = [];
}
