namespace Barbearia.Application.WhatsApp;

public record OpcaoInterativa(string Id, string Titulo, string? Descricao = null);

public interface IWhatsAppClient
{
    Task EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default);

    /// <summary>Ate 3 botoes, limite da Meta.</summary>
    Task EnviarBotoesAsync(string telefone, string texto, IReadOnlyList<OpcaoInterativa> opcoes,
        CancellationToken ct = default);

    /// <summary>Lista de selecao, ate 10 itens: escolher horario sem sair do WhatsApp.</summary>
    Task EnviarListaAsync(string telefone, string texto, string tituloBotao,
        IReadOnlyList<OpcaoInterativa> opcoes, CancellationToken ct = default);
}
