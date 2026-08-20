using Barbearia.Application;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Barbeiro> Barbeiros => Set<Barbeiro>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Expediente> Expedientes => Set<Expediente>();
    public DbSet<Bloqueio> Bloqueios => Set<Bloqueio>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<MagicLink> MagicLinks => Set<MagicLink>();
    public DbSet<MensagemProcessada> MensagensProcessadas => Set<MensagemProcessada>();
    public DbSet<ConversaEstado> ConversaEstados => Set<ConversaEstado>();
    public DbSet<Configuracao> Configuracoes => Set<Configuracao>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<BarbeiroServico> BarbeiroServicos => Set<BarbeiroServico>();
    public DbSet<Avaliacao> Avaliacoes => Set<Avaliacao>();
    public DbSet<PedidoProduto> PedidosProduto => Set<PedidoProduto>();

    public const string ConstraintSemSobreposicao = "ck_agendamentos_sem_sobreposicao";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("btree_gist");

        b.Entity<Barbeiro>(e =>
        {
            e.ToTable("barbeiros");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(160).IsRequired();
            e.Property(x => x.SenhaHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.Perfil).HasConversion<int>();
            e.Property(x => x.Telefone).HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne<Cliente>()
                .WithMany()
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Configuracao>(e =>
        {
            e.ToTable("configuracoes");
            e.HasKey(x => x.Chave);
            e.Property(x => x.Chave).HasMaxLength(80);
            e.Property(x => x.Valor).HasMaxLength(1000);
        });

        b.Entity<Servico>(e =>
        {
            e.ToTable("servicos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        });

        b.Entity<BarbeiroServico>(e =>
        {
            e.ToTable("barbeiro_servicos");
            e.HasKey(x => new { x.BarbeiroId, x.ServicoId });
            e.HasOne(x => x.Barbeiro).WithMany().HasForeignKey(x => x.BarbeiroId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Servico).WithMany().HasForeignKey(x => x.ServicoId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ServicoId);
        });

        b.Entity<Produto>(e =>
        {
            e.ToTable("produtos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(400);
        });

        b.Entity<Avaliacao>(e =>
        {
            e.ToTable("avaliacoes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Comentario).HasMaxLength(600);

            e.HasOne(x => x.Produto)
                .WithMany()
                .HasForeignKey(x => x.ProdutoId)
                .OnDelete(DeleteBehavior.Cascade);

            // Uma avaliacao por telefone por produto. E o freio de spam que
            // sobra quando qualquer visitante pode avaliar: sem ele, a mesma
            // pessoa (ou um script) empilha notas no mesmo produto a vontade.
            e.HasIndex(x => new { x.ProdutoId, x.Telefone }).IsUnique();

            // A vitrine le sempre "visiveis deste produto".
            e.HasIndex(x => new { x.ProdutoId, x.Visivel });
        });

        b.Entity<PedidoProduto>(e =>
        {
            e.ToTable("pedidos_produto");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<int>();

            e.HasOne(x => x.Agendamento)
                .WithMany()
                .HasForeignKey(x => x.AgendamentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Produto)
                .WithMany()
                .HasForeignKey(x => x.ProdutoId)
                .OnDelete(DeleteBehavior.Cascade);

            // Um pedido por produto em cada agendamento: clicar de novo troca
            // entre "usar" e "levar" em vez de criar linha repetida.
            e.HasIndex(x => new { x.AgendamentoId, x.ProdutoId }).IsUnique();
        });

        b.Entity<Cliente>(e =>
        {
            e.ToTable("clientes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Telefone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.SenhaHash).HasMaxLength(100);

            // O telefone ja era unico para nao duplicar cliente; agora ele tambem e
            // o nome de usuario com que a pessoa entra em agendar.html.
            e.HasIndex(x => x.Telefone).IsUnique();
        });

        b.Entity<Expediente>(e =>
        {
            e.ToTable("expedientes");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Barbeiro)
                .WithMany(x => x.Expedientes)
                .HasForeignKey(x => x.BarbeiroId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BarbeiroId, x.DiaSemana });
        });

        b.Entity<Bloqueio>(e =>
        {
            e.ToTable("bloqueios");
            e.HasKey(x => x.Id);
            e.Property(x => x.Motivo).HasMaxLength(200);
            e.HasOne(x => x.Barbeiro)
                .WithMany(x => x.Bloqueios)
                .HasForeignKey(x => x.BarbeiroId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BarbeiroId, x.InicioUtc, x.FimUtc });
        });

        b.Entity<Agendamento>(e =>
        {
            e.ToTable("agendamentos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Origem).HasConversion<int>();
            e.Ignore(x => x.EstaAtivo);

            e.HasOne(x => x.Barbeiro)
                .WithMany(x => x.Agendamentos)
                .HasForeignKey(x => x.BarbeiroId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Cliente)
                .WithMany(x => x.Agendamentos)
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Servico)
                .WithMany()
                .HasForeignKey(x => x.ServicoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.BarbeiroId, x.InicioUtc });
            e.HasIndex(x => new { x.ClienteId, x.InicioUtc });
        });

        b.Entity<MagicLink>(e =>
        {
            e.ToTable("magic_links");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Cliente)
                .WithMany()
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MensagemProcessada>(e =>
        {
            e.ToTable("mensagens_processadas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.HasIndex(x => x.ProcessadaEmUtc);
        });

        b.Entity<ConversaEstado>(e =>
        {
            e.ToTable("conversa_estados");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClienteId).IsUnique();
            e.Ignore(x => x.Cliente);
            e.HasOne<Cliente>()
                .WithMany()
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
