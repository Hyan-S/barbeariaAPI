namespace Barbearia.Application.WhatsApp;

public record OpcaoInterativa(string Id, string Titulo, string? Descricao = null);

public interface IWhatsAppClient
{
    Task EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default);

    Task EnviarBotoesAsync(string telefone, string texto, IReadOnlyList<OpcaoInterativa> opcoes,
        CancellationToken ct = default);

    Task EnviarListaAsync(string telefone, string texto, string tituloBotao,
        IReadOnlyList<OpcaoInterativa> opcoes, CancellationToken ct = default);
}
