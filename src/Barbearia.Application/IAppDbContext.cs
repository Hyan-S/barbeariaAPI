using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Application;

public interface IAppDbContext
{
    DbSet<Barbeiro> Barbeiros { get; }
    DbSet<Servico> Servicos { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Expediente> Expedientes { get; }
    DbSet<Bloqueio> Bloqueios { get; }
    DbSet<Agendamento> Agendamentos { get; }
    DbSet<MagicLink> MagicLinks { get; }
    DbSet<MensagemProcessada> MensagensProcessadas { get; }
    DbSet<ConversaEstado> ConversaEstados { get; }
    DbSet<Domain.Entities.Configuracao> Configuracoes { get; }
    DbSet<Produto> Produtos { get; }
    DbSet<BarbeiroServico> BarbeiroServicos { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
