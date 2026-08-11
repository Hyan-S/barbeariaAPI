using System.Text.Json.Serialization;

namespace Barbearia.Api.WhatsApp;

/// <summary>
/// Recorte do payload da Meta. Tudo anulavel: o mesmo endpoint recebe eventos de
/// entrega, leitura e mudanca de perfil, e nao pode quebrar em nenhum deles.
/// </summary>
public record WebhookPayload
{
    [JsonPropertyName("entry")] public List<Entry>? Entry { get; init; }
}

public record Entry
{
    [JsonPropertyName("changes")] public List<Change>? Changes { get; init; }
}

public record Change
{
    [JsonPropertyName("value")] public ChangeValue? Value { get; init; }
}

public record ChangeValue
{
    [JsonPropertyName("contacts")] public List<Contact>? Contacts { get; init; }
    [JsonPropertyName("messages")] public List<Message>? Messages { get; init; }
}

public record Contact
{
    [JsonPropertyName("wa_id")] public string? WaId { get; init; }
    [JsonPropertyName("profile")] public Profile? Profile { get; init; }
}

public record Profile
{
    [JsonPropertyName("name")] public string? Name { get; init; }
}

public record Message
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("from")] public string? From { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("text")] public TextBody? Text { get; init; }
    [JsonPropertyName("interactive")] public Interactive? Interactive { get; init; }
}

public record TextBody
{
    [JsonPropertyName("body")] public string? Body { get; init; }
}

public record Interactive
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("button_reply")] public Reply? ButtonReply { get; init; }
    [JsonPropertyName("list_reply")] public Reply? ListReply { get; init; }
}

public record Reply
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
}

public record MensagemRecebida(
    string MessageId,
    string Telefone,
    string? NomePerfil,
    string? Texto,
    string? OpcaoSelecionada);
