using System.Text.Json;
using Barbearia.Api.WhatsApp;
using Barbearia.Application.Configuracao;
using Barbearia.Infrastructure.WhatsApp;

namespace Barbearia.Api.Endpoints;

public static class WebhookEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static void MapWebhook(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/webhook/whatsapp").ExcludeFromDescription();

        g.MapGet("/", async (HttpContext ctx, ConfiguracaoService cfg) =>
        {
            var q = ctx.Request.Query;
            var config = await cfg.ObterWhatsAppAsync();

            if (q["hub.mode"] == "subscribe"
                && ValidadorAssinatura.ConferirVerifyToken(q["hub.verify_token"], config.VerifyToken))
                return Results.Text(q["hub.challenge"].ToString());

            return Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        g.MapPost("/", async (
            HttpContext ctx, ConfiguracaoService cfg, FilaDeMensagens fila,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("Webhook");
            var config = await cfg.ObterWhatsAppAsync(ct);

            if (!config.EstaConfigurado())
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            using var buffer = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(buffer, ct);
            var corpo = buffer.ToArray();

            if (!ValidadorAssinatura.Conferir(
                    corpo, ctx.Request.Headers["X-Hub-Signature-256"], config.AppSecret))
            {
                log.LogWarning("Webhook com assinatura invalida recusado");
                return Results.Unauthorized();
            }

            WebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<WebhookPayload>(corpo, Json);
            }
            catch (JsonException ex)
            {
                log.LogWarning(ex, "Payload do webhook ilegivel");
                return Results.Ok();
            }

            foreach (var mensagem in Extrair(payload))
                if (!fila.Enfileirar(mensagem))
                    log.LogWarning("Fila cheia, mensagem {Id} descartada", mensagem.MessageId);

            return Results.Ok();
        });
    }

    private static IEnumerable<MensagemRecebida> Extrair(WebhookPayload? payload)
    {
        if (payload?.Entry is null) yield break;

        foreach (var entry in payload.Entry)
        foreach (var change in entry.Changes ?? [])
        {
            var valor = change.Value;
            if (valor?.Messages is null) continue;

            foreach (var msg in valor.Messages)
            {
                if (string.IsNullOrWhiteSpace(msg.Id) || string.IsNullOrWhiteSpace(msg.From))
                    continue;

                var nome = valor.Contacts?.FirstOrDefault(c => c.WaId == msg.From)?.Profile?.Name;
                var opcao = msg.Interactive?.ButtonReply?.Id ?? msg.Interactive?.ListReply?.Id;

                var texto = msg.Text?.Body ?? string.Empty;

                yield return new MensagemRecebida(msg.Id, msg.From, nome, texto, opcao);
            }
        }
    }
}
