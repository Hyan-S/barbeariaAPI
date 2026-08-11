using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Barbearia.Application.Configuracao;
using Barbearia.Application.WhatsApp;
using Microsoft.Extensions.Logging;

namespace Barbearia.Infrastructure.WhatsApp;

public class WhatsAppClient(
    HttpClient http,
    ConfiguracaoService configuracao,
    ILogger<WhatsAppClient> logger) : IWhatsAppClient
{
    public Task EnviarTextoAsync(string telefone, string texto, CancellationToken ct = default) =>
        EnviarAsync(new
        {
            messaging_product = "whatsapp",
            to = telefone,
            type = "text",
            text = new { preview_url = true, body = Truncar(texto, 4096) }
        }, ct);

    public Task EnviarBotoesAsync(string telefone, string texto,
        IReadOnlyList<OpcaoInterativa> opcoes, CancellationToken ct = default) =>
        EnviarAsync(new
        {
            messaging_product = "whatsapp",
            to = telefone,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = Truncar(texto, 1024) },
                action = new
                {
                    buttons = opcoes.Take(3).Select(o => new
                    {
                        type = "reply",
                        reply = new { id = Truncar(o.Id, 256), title = Truncar(o.Titulo, 20) }
                    })
                }
            }
        }, ct);

    public Task EnviarListaAsync(string telefone, string texto, string tituloBotao,
        IReadOnlyList<OpcaoInterativa> opcoes, CancellationToken ct = default) =>
        EnviarAsync(new
        {
            messaging_product = "whatsapp",
            to = telefone,
            type = "interactive",
            interactive = new
            {
                type = "list",
                body = new { text = Truncar(texto, 1024) },
                action = new
                {
                    button = Truncar(tituloBotao, 20),
                    sections = new[]
                    {
                        new
                        {
                            title = "Horarios",
                            rows = opcoes.Take(10).Select(o => new
                            {
                                id = Truncar(o.Id, 200),
                                title = Truncar(o.Titulo, 24),
                                description = Truncar(o.Descricao ?? string.Empty, 72)
                            })
                        }
                    }
                }
            }
        }, ct);

    private async Task EnviarAsync(object payload, CancellationToken ct)
    {
        var cfg = await configuracao.ObterWhatsAppAsync(ct);

        if (!cfg.EstaConfigurado())
        {
            logger.LogInformation("WhatsApp desligado. Nao enviado: {Payload}",
                JsonSerializer.Serialize(payload));
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{cfg.ApiVersion}/{cfg.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload)
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.AccessToken);

        using var resposta = await http.SendAsync(req, ct);

        if (!resposta.IsSuccessStatusCode)
            logger.LogError("Falha ao enviar WhatsApp ({Status}): {Corpo}",
                (int)resposta.StatusCode, await resposta.Content.ReadAsStringAsync(ct));
    }

    private static string Truncar(string texto, int max) =>
        texto.Length <= max ? texto : texto[..max];
}
