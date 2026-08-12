using System.Threading.Channels;
using Barbearia.Application;
using Barbearia.Application.WhatsApp;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.WhatsApp;

public class FilaDeMensagens
{
    private readonly Channel<MensagemRecebida> _canal =
        Channel.CreateBounded<MensagemRecebida>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public bool Enfileirar(MensagemRecebida mensagem) => _canal.Writer.TryWrite(mensagem);

    public IAsyncEnumerable<MensagemRecebida> LerTudoAsync(CancellationToken ct) =>
        _canal.Reader.ReadAllAsync(ct);
}

public class ProcessadorDeMensagens(
    FilaDeMensagens fila,
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessadorDeMensagens> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var mensagem in fila.LerTudoAsync(stoppingToken))
        {
            try
            {
                await ProcessarAsync(mensagem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao processar mensagem {Id}", mensagem.MessageId);
            }
        }
    }

    private async Task ProcessarAsync(MensagemRecebida mensagem, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var conversa = scope.ServiceProvider.GetRequiredService<ConversaService>();

        if (await db.MensagensProcessadas.AnyAsync(m => m.Id == mensagem.MessageId, ct))
        {
            logger.LogDebug("Mensagem {Id} ja processada, ignorando", mensagem.MessageId);
            return;
        }

        db.MensagensProcessadas.Add(new MensagemProcessada { Id = mensagem.MessageId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            logger.LogDebug("Mensagem {Id} gravada em paralelo, ignorando", mensagem.MessageId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(mensagem.OpcaoSelecionada))
        {
            await conversa.ProcessarSelecaoAsync(
                mensagem.Telefone, mensagem.NomePerfil, mensagem.OpcaoSelecionada, ct);
        }
        else if (!string.IsNullOrWhiteSpace(mensagem.Texto))
        {
            await conversa.ProcessarTextoAsync(
                mensagem.Telefone, mensagem.NomePerfil, mensagem.Texto, ct);
        }
    }
}
