using Barbearia.Application.Acesso;
using Barbearia.Application.Agendamentos;
using Barbearia.Application.Configuracao;
using Barbearia.Application.Disponibilidade;
using Barbearia.Domain;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class PublicoEndpoints
{
    public record NovoAgendamento(
        string? Telefone, string? Nome, Guid ServicoId, DateTime InicioUtc, Guid? BarbeiroId, string? Token);

    public static void MapPublico(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api");

        g.MapGet("/config", async (ConfiguracaoService cfg) =>
            Results.Ok(new { nome = await cfg.ObterNomeBarbeariaAsync() }));

        g.MapGet("/servicos", async (AppDbContext db) =>
            await db.Servicos.AsNoTracking()
                .Where(s => s.Ativo)
                .OrderBy(s => s.DuracaoMinutos)
                .Select(s => new { s.Id, s.Nome, s.DuracaoMinutos, s.PrecoCentavos })
                .ToListAsync());

        g.MapGet("/barbeiros", async (AppDbContext db) =>
            await db.Barbeiros.AsNoTracking()
                .Where(b => b.Ativo && b.Atende)
                .OrderBy(b => b.Nome)
                .Select(b => new { b.Id, b.Nome })
                .ToListAsync());

        g.MapGet("/servicos/{id:guid}/barbeiros", async (Guid id, AppDbContext db) =>
        {
            var habilitados = await db.BarbeiroServicos.AsNoTracking()
                .Where(x => x.ServicoId == id)
                .Select(x => x.BarbeiroId)
                .ToListAsync();

            var filtrar = habilitados.Count > 0;

            return Results.Ok(await db.Barbeiros.AsNoTracking()
                .Where(b => b.Ativo && b.Atende && (!filtrar || habilitados.Contains(b.Id)))
                .OrderBy(b => b.Nome)
                .Select(b => new { b.Id, b.Nome })
                .ToListAsync());
        });

        g.MapGet("/disponibilidade", async (
            DateOnly data, Guid servicoId, Guid? barbeiroId, bool? grade,
            DisponibilidadeService servico) =>
        {
            var slots = grade == true
                ? await servico.ObterGradeDoDiaAsync(data, servicoId, barbeiroId)
                : await servico.ObterDoDiaAsync(data, servicoId, barbeiroId);

            return Results.Ok(slots.Select(s => new
            {
                s.BarbeiroId, s.BarbeiroNome, s.InicioUtc, s.Livre, hora = s.HoraFormatada
            }));
        });

        g.MapGet("/sessao", async (string t, MagicLinkService links, AppDbContext db) =>
        {
            var cliente = await links.ResolverAsync(t);
            if (cliente is null) return Results.Json(new { erro = "Link expirado" }, statusCode: 401);

            var agora = DateTime.UtcNow;

            var agendamentos = await db.Agendamentos.AsNoTracking()
                .Include(a => a.Servico).Include(a => a.Barbeiro)
                .Where(a => a.ClienteId == cliente.Id
                            && a.InicioUtc > agora
                            && a.Status != StatusAgendamento.Cancelado)
                .OrderBy(a => a.InicioUtc)
                .ToListAsync();

            return Results.Ok(new
            {
                nome = cliente.Nome,
                telefone = TelefoneBr.Formatar(cliente.Telefone),
                agendamentos = agendamentos.Select(a => new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    servico = a.Servico!.Nome,
                    barbeiro = a.Barbeiro!.Nome
                })
            });
        });

        g.MapPost("/agendamentos", async (
            NovoAgendamento req, AgendamentoService servico, MagicLinkService links, AppDbContext db) =>
        {
            Domain.Entities.Cliente? cliente = null;

            if (!string.IsNullOrWhiteSpace(req.Token))
                cliente = await links.ResolverAsync(req.Token);

            if (cliente is null)
            {
                var telefone = TelefoneBr.Normalizar(req.Telefone);
                if (telefone is null)
                    return Results.BadRequest(new { erro = "Informe um telefone valido com DDD" });

                cliente = await servico.ObterOuCriarClienteAsync(telefone, req.Nome);
            }

            var resultado = await servico.CriarAsync(
                cliente.Id, req.ServicoId, req.InicioUtc, req.BarbeiroId, OrigemAgendamento.Web);

            if (resultado.Sucesso)
            {
                var a = resultado.Agendamento!;

                var barbeiro = await db.Barbeiros.AsNoTracking()
                    .Where(b => b.Id == a.BarbeiroId)
                    .Select(b => b.Nome)
                    .FirstOrDefaultAsync();

                return Results.Ok(new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    a.BarbeiroId,
                    barbeiro
                });
            }

            return Results.Json(new
            {
                erro = Mensagem(resultado.Tipo),
                sugestoes = resultado.Sugestoes?.Select(s => new
                {
                    s.InicioUtc, hora = s.HoraFormatada, s.BarbeiroNome
                })
            }, statusCode: 409);
        }).RequireRateLimiting("agendar");

        g.MapPost("/agendamentos/{id:guid}/cancelar", async (
            Guid id, string t, MagicLinkService links, AgendamentoService servico) =>
        {
            var cliente = await links.ResolverAsync(t);
            if (cliente is null) return Results.Json(new { erro = "Link expirado" }, statusCode: 401);

            return await servico.CancelarAsync(id, cliente.Id, true)
                ? Results.Ok()
                : Results.BadRequest(new { erro = "Nao foi possivel cancelar (muito proximo do horario)" });
        });
    }

    private static string Mensagem(ResultadoTipo tipo) => tipo switch
    {
        ResultadoTipo.HorarioIndisponivel => "Esse horario acabou de ser ocupado",
        ResultadoTipo.ForaDaAntecedencia => "Esse horario ja esta muito em cima",
        ResultadoTipo.ForaDaJanelaDeAgenda => "A agenda ainda nao abriu para essa data",
        ResultadoTipo.LimiteDeAgendamentosAtingido => "Voce ja tem agendamentos abertos demais",
        ResultadoTipo.ServicoInvalido => "Servico indisponivel",
        _ => "Nao foi possivel agendar"
    };
}
