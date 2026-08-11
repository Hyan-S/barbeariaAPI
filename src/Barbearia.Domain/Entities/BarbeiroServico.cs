namespace Barbearia.Domain.Entities;

/// <summary>Quem executa o que. Servico sem nenhum vinculo e atendido por qualquer barbeiro ativo.</summary>
public class BarbeiroServico
{
    public Guid BarbeiroId { get; set; }
    public Barbeiro? Barbeiro { get; set; }

    public Guid ServicoId { get; set; }
    public Servico? Servico { get; set; }
}
