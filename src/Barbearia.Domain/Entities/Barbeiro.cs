namespace Barbearia.Domain.Entities;

public class Barbeiro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Preenchido quando o funcionario foi cadastrado a partir de alguem que ja
    /// era cliente. Mantem as duas fichas ligadas em vez de duplicar a pessoa.
    /// </summary>
    public Guid? ClienteId { get; set; }

    public Perfil Perfil { get; set; } = Perfil.Barbeiro;

    /// <summary>Falso para quem so administra e nao atende cliente.</summary>
    public bool Atende { get; set; } = true;

    /// <summary>
    /// Enquanto ligado, o usuario entra mas nao acessa nada alem da troca de senha.
    /// Impede que a senha provisoria dada pelo admin vire definitiva.
    /// </summary>
    public bool PrecisaTrocarSenha { get; set; }

    public bool PodeGerenciarServicos { get; set; }
    public bool PodeGerenciarProdutos { get; set; }
    public bool PodeGerenciarClientes { get; set; }

    /// <summary>Faturamento e ranking de profissionais: so quem o admin liberar.</summary>
    public bool PodeVerDashboard { get; set; }

    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    public List<Expediente> Expedientes { get; set; } = [];
    public List<Agendamento> Agendamentos { get; set; } = [];
    public List<Bloqueio> Bloqueios { get; set; } = [];
}
