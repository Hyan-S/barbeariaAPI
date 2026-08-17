namespace Barbearia.Domain.Entities;

public class Avaliacao
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public string Nome { get; set; } = string.Empty;

    // Guardado normalizado e nunca devolvido na API publica: serve para limitar
    // uma avaliacao por pessoa por produto (indice unico com ProdutoId) e para o
    // dono saber quem escreveu. Quem le a vitrine ve so o nome.
    public string Telefone { get; set; } = string.Empty;

    public int Nota { get; set; }
    public string? Comentario { get; set; }

    // Nasce no ar: a avaliacao aparece na hora, sem fila de aprovacao. O painel
    // e curadoria depois do fato — da para tirar do ar (Visivel = false, o texto
    // continua guardado) ou apagar de vez.
    public bool Visivel { get; set; } = true;

    public DateTime CriadaEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? OcultadaEmUtc { get; set; }
}
