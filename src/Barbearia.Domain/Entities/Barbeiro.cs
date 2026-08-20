namespace Barbearia.Domain.Entities;

public class Barbeiro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;

    public Guid? ClienteId { get; set; }

    public Perfil Perfil { get; set; } = Perfil.Barbeiro;

    public bool Atende { get; set; } = true;

    public bool PrecisaTrocarSenha { get; set; }

    public bool PodeGerenciarServicos { get; set; }
    public bool PodeGerenciarProdutos { get; set; }
    public bool PodeGerenciarClientes { get; set; }

    public bool PodeVerDashboard { get; set; }

    // Corte de sessao. Todo token do painel carrega a claim "emitido"; se ela for
    // anterior a este selo, o token nao vale mais. E o que faz desativar alguem,
    // mexer na permissao dele ou trocar a senha valer na hora, e nao daqui a 8
    // horas, quando o token venceria por conta propria.
    public DateTime TokensValidosDesdeUtc { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    public List<Expediente> Expedientes { get; set; } = [];
    public List<Agendamento> Agendamentos { get; set; } = [];
    public List<Bloqueio> Bloqueios { get; set; } = [];
}
