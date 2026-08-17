using Barbearia.Application;
using Barbearia.Application.Agendamentos;
using Barbearia.Application.Disponibilidade;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

public static class GestorEndpoints
{
    public record ServicoRequest(
        string Nome, int DuracaoMinutos, int PrecoCentavos, bool Ativo, Guid[]? BarbeiroIds);

    private static async Task VincularAsync(AppDbContext db, Guid servicoId, Guid[]? barbeiroIds)
    {
        if (barbeiroIds is null || barbeiroIds.Length == 0) return;

        var validos = await db.Barbeiros.AsNoTracking()
            .Where(b => barbeiroIds.Contains(b.Id) && b.Ativo && b.Atende)
            .Select(b => b.Id)
            .ToListAsync();

        foreach (var barbeiroId in validos)
            db.BarbeiroServicos.Add(new BarbeiroServico { ServicoId = servicoId, BarbeiroId = barbeiroId });
    }
    public record ExpedienteRequest(Guid BarbeiroId, int DiaSemana, string HoraInicio, string HoraFim);
    public record BloqueioRequest(Guid BarbeiroId, DateTime InicioLocal, DateTime FimLocal, string? Motivo);

    public record ReagendarRequest(DateTime NovoInicioUtc, Guid? BarbeiroId);

    public record AgendamentoPeloPainel(
        Guid? ClienteId, string? Telefone, string? Nome,
        Guid ServicoId, DateTime InicioUtc, Guid? BarbeiroId, string? Observacao);

    public static void MapGestor(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gestor").RequireAuthorization("Gestao");

        var painel = app.MapGroup("/api/gestor").RequireAuthorization("Painel");

        const int TetoBusca = 3000;
        const int TetoExibicao = 500;

        painel.MapGet("/agenda", async (
            DateOnly de, DateOnly ate, string? busca, Guid? barbeiroId, Guid? servicoId,
            string? status, string? origem, int? horaDe, int? horaAte, AppDbContext db) =>
        {
            if (ate < de) (de, ate) = (ate, de);

            var inicioUtc = Fuso.ParaUtc(de.ToDateTime(TimeOnly.MinValue));
            var fimUtc = Fuso.ParaUtc(ate.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var q = db.Agendamentos.AsNoTracking()
                .Include(a => a.Cliente).Include(a => a.Servico).Include(a => a.Barbeiro)
                .Where(a => a.InicioUtc >= inicioUtc && a.InicioUtc < fimUtc);

            if (barbeiroId.HasValue) q = q.Where(a => a.BarbeiroId == barbeiroId.Value);
            if (servicoId.HasValue) q = q.Where(a => a.ServicoId == servicoId.Value);

            if (Enum.TryParse<StatusAgendamento>(status, out var st))
                q = q.Where(a => a.Status == st);

            if (Enum.TryParse<OrigemAgendamento>(origem, out var og))
                q = q.Where(a => a.Origem == og);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim();
                var telefone = TelefoneBr.Normalizar(termo);

                q = telefone is not null
                    ? q.Where(a => a.Cliente!.Telefone == telefone)
                    : q.Where(a => EF.Functions.ILike(a.Cliente!.Nome, $"%{termo}%"));
            }

            var lista = await q.OrderBy(a => a.InicioUtc).Take(TetoBusca).ToListAsync();

            if (horaDe.HasValue || horaAte.HasValue)
            {
                var h1 = horaDe ?? 0;
                var h2 = horaAte ?? 23;
                lista = lista.Where(a =>
                {
                    var h = Fuso.ParaLocal(a.InicioUtc).Hour;
                    return h >= h1 && h <= h2;
                }).ToList();
            }

            var ativos = lista.Where(a => a.Status != StatusAgendamento.Cancelado).ToList();

            var exibir = lista.Take(TetoExibicao).ToList();

            // Produtos que o cliente marcou na vitrine, buscados de uma vez para
            // a pagina inteira: dentro do Select seria uma consulta por linha.
            var ids = exibir.Select(a => a.Id).ToList();

            var pedidos = (await db.PedidosProduto.AsNoTracking()
                    .Where(p => ids.Contains(p.AgendamentoId))
                    .Select(p => new
                    {
                        p.AgendamentoId,
                        produto = p.Produto!.Nome,
                        precoCentavos = p.Produto.PrecoCentavos,
                        tipo = p.Tipo
                    })
                    .ToListAsync())
                .GroupBy(p => p.AgendamentoId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => new { p.produto, p.precoCentavos, tipo = p.tipo.ToString() })
                          .OrderBy(p => p.produto)
                          .ToList());

            return Results.Ok(new
            {
                total = lista.Count,
                truncado = lista.Count >= TetoBusca,
                resumo = new
                {
                    confirmados = lista.Count(a => a.Status == StatusAgendamento.Confirmado),
                    concluidos = lista.Count(a => a.Status == StatusAgendamento.Concluido),
                    cancelados = lista.Count(a => a.Status == StatusAgendamento.Cancelado),
                    minutos = ativos.Sum(a => (int)(a.FimUtc - a.InicioUtc).TotalMinutes),
                    receitaCentavos = ativos.Sum(a => a.Servico!.PrecoCentavos)
                },
                itens = exibir.Select(a => new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    fim = Fuso.ParaLocal(a.FimUtc),
                    duracao = (int)(a.FimUtc - a.InicioUtc).TotalMinutes,
                    cliente = a.Cliente!.Nome,
                    telefone = TelefoneBr.Formatar(a.Cliente.Telefone),
                    servico = a.Servico!.Nome,
                    a.ServicoId,
                    precoCentavos = a.Servico.PrecoCentavos,
                    barbeiro = a.Barbeiro!.Nome,
                    a.BarbeiroId,
                    status = a.Status.ToString(),
                    origem = a.Origem.ToString(),
                    a.Observacao,
                    produtos = pedidos.GetValueOrDefault(a.Id)
                })
            });
        });

        painel.MapGet("/agendamentos/{id:guid}/grade", async (
            Guid id, DateOnly data, AppDbContext db, DisponibilidadeService disponibilidade) =>
        {
            var a = await db.Agendamentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();

            var slots = await disponibilidade.ObterGradeParaMoverAsync(data, a.ServicoId, id);

            return Results.Ok(slots.Select(s => new
            {
                s.BarbeiroId, s.BarbeiroNome, s.InicioUtc, s.Livre, hora = s.HoraFormatada
            }));
        });

        painel.MapPost("/agendamentos/{id:guid}/reagendar", async (
            Guid id, ReagendarRequest req, AppDbContext db,
            DisponibilidadeService disponibilidade, IDetectorDeConflito detector) =>
        {
            var a = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();

            if (!a.EstaAtivo)
                return Results.BadRequest(new { erro = "Agendamento cancelado nao pode ser remarcado" });

            var servico = await db.Servicos.AsNoTracking().FirstAsync(s => s.Id == a.ServicoId);
            var diaLocal = DateOnly.FromDateTime(Fuso.ParaLocal(req.NovoInicioUtc));

            var slots = await disponibilidade.ObterGradeParaMoverAsync(
                diaLocal, a.ServicoId, id, req.BarbeiroId);

            var slot = slots.FirstOrDefault(s => s.InicioUtc == req.NovoInicioUtc && s.Livre);
            if (slot is null)
                return Results.Conflict(new { erro = "Esse horario nao esta livre" });

            var anterior = Fuso.ParaLocal(a.InicioUtc);

            a.InicioUtc = slot.InicioUtc;
            a.FimUtc = slot.InicioUtc.AddMinutes(servico.DuracaoMinutos);
            a.BarbeiroId = slot.BarbeiroId;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex) when (detector.EhConflitoDeHorario(ex))
            {
                return Results.Conflict(new { erro = "Esse horario acabou de ser ocupado" });
            }

            return Results.Ok(new
            {
                de = anterior,
                para = Fuso.ParaLocal(a.InicioUtc),
                barbeiro = slot.BarbeiroNome
            });
        });

        painel.MapPost("/agendamentos/{id:guid}/cancelar", async (Guid id, AgendamentoService servico) =>
            await servico.CancelarAsync(id, null, false) ? Results.Ok() : Results.NotFound());

        painel.MapPost("/agendamentos", async (
            AgendamentoPeloPainel req, AgendamentoService servico, AppDbContext db) =>
        {
            Cliente? cliente = null;

            if (req.ClienteId.HasValue)
                cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == req.ClienteId.Value);

            if (cliente is null)
            {
                var telefone = TelefoneBr.Normalizar(req.Telefone);
                if (telefone is null)
                    return Results.BadRequest(new { erro = "Escolha um cliente ou informe um telefone valido" });

                cliente = await servico.ObterOuCriarClienteAsync(telefone, req.Nome);
            }

            var resultado = await servico.CriarAsync(
                cliente.Id, req.ServicoId, req.InicioUtc, req.BarbeiroId,
                OrigemAgendamento.Painel, req.Observacao, comoStaff: true);

            if (resultado.Sucesso)
            {
                var a = resultado.Agendamento!;
                return Results.Ok(new
                {
                    a.Id,
                    inicio = Fuso.ParaLocal(a.InicioUtc),
                    cliente = cliente.Nome,
                    telefone = TelefoneBr.Formatar(cliente.Telefone)
                });
            }

            return Results.Json(new
            {
                erro = resultado.Tipo switch
                {
                    ResultadoTipo.HorarioIndisponivel => "Esse horario acabou de ser ocupado",
                    ResultadoTipo.ForaDaJanelaDeAgenda => "Data alem do limite configurado da agenda",
                    ResultadoTipo.ClienteBloqueado => "Cliente bloqueado",
                    ResultadoTipo.ServicoInvalido => "Servico indisponivel",
                    _ => "Nao foi possivel agendar"
                },
                sugestoes = resultado.Sugestoes?.Select(s => new
                {
                    s.InicioUtc, hora = s.HoraFormatada, s.BarbeiroNome
                })
            }, statusCode: 409);
        });

        painel.MapGet("/barbeiros", async (AppDbContext db) =>
            await db.Barbeiros.AsNoTracking()
                .Where(b => b.Ativo && b.Atende)
                .OrderBy(b => b.Nome)
                .Select(b => new { b.Id, b.Nome })
                .ToListAsync());

        var servicos = app.MapGroup("/api/gestor").RequireAuthorization("Servicos");

        servicos.MapGet("/servicos", async (AppDbContext db) =>
        {
            var vinculos = await db.BarbeiroServicos.AsNoTracking().ToListAsync();

            var lista = await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync();

            return Results.Ok(lista.Select(s => new
            {
                s.Id, s.Nome, s.DuracaoMinutos, s.PrecoCentavos, s.Ativo,
                barbeiroIds = vinculos.Where(v => v.ServicoId == s.Id).Select(v => v.BarbeiroId)
            }));
        });

        servicos.MapPost("/servicos", async (ServicoRequest req, AppDbContext db) =>
        {
            if (req.DuracaoMinutos is < 5 or > 480)
                return Results.BadRequest(new { erro = "Duracao deve ficar entre 5 e 480 minutos" });

            var servico = new Servico
            {
                Nome = req.Nome.Trim(),
                DuracaoMinutos = req.DuracaoMinutos,
                PrecoCentavos = req.PrecoCentavos,
                Ativo = req.Ativo
            };

            db.Servicos.Add(servico);
            await VincularAsync(db, servico.Id, req.BarbeiroIds);

            await db.SaveChangesAsync();
            return Results.Ok(new { servico.Id });
        });

        servicos.MapPut("/servicos/{id:guid}", async (Guid id, ServicoRequest req, AppDbContext db) =>
        {
            var servico = await db.Servicos.FirstOrDefaultAsync(s => s.Id == id);
            if (servico is null) return Results.NotFound();

            if (req.DuracaoMinutos is < 5 or > 480)
                return Results.BadRequest(new { erro = "Duracao deve ficar entre 5 e 480 minutos" });

            servico.Nome = req.Nome.Trim();
            servico.DuracaoMinutos = req.DuracaoMinutos;
            servico.PrecoCentavos = req.PrecoCentavos;
            servico.Ativo = req.Ativo;

            db.BarbeiroServicos.RemoveRange(
                await db.BarbeiroServicos.Where(x => x.ServicoId == id).ToListAsync());

            await VincularAsync(db, id, req.BarbeiroIds);

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        g.MapGet("/expedientes", async (AppDbContext db) =>
            await db.Expedientes.AsNoTracking()
                .OrderBy(e => e.DiaSemana).ThenBy(e => e.HoraInicio)
                .Select(e => new
                {
                    e.Id, e.BarbeiroId,
                    diaSemana = (int)e.DiaSemana,
                    horaInicio = e.HoraInicio.ToString("HH:mm"),
                    horaFim = e.HoraFim.ToString("HH:mm")
                })
                .ToListAsync());

        g.MapPost("/expedientes", async (ExpedienteRequest req, AppDbContext db) =>
        {
            if (!TimeOnly.TryParse(req.HoraInicio, out var inicio)
                || !TimeOnly.TryParse(req.HoraFim, out var fim))
                return Results.BadRequest(new { erro = "Horario invalido" });

            if (fim <= inicio)
                return Results.BadRequest(new { erro = "Hora fim precisa ser maior que a de inicio" });

            if (req.DiaSemana is < 0 or > 6)
                return Results.BadRequest(new { erro = "Dia da semana invalido" });

            db.Expedientes.Add(new Expediente
            {
                BarbeiroId = req.BarbeiroId,
                DiaSemana = (DayOfWeek)req.DiaSemana,
                HoraInicio = inicio,
                HoraFim = fim
            });

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        g.MapDelete("/expedientes/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Expedientes.Where(e => e.Id == id).ExecuteDeleteAsync() > 0
                ? Results.Ok() : Results.NotFound());

        g.MapGet("/bloqueios", async (AppDbContext db) =>
        {
            var desde = DateTime.UtcNow.AddDays(-7);
            var lista = await db.Bloqueios.AsNoTracking()
                .Include(b => b.Barbeiro)
                .Where(b => b.FimUtc >= desde)
                .OrderBy(b => b.InicioUtc)
                .ToListAsync();

            return Results.Ok(lista.Select(b => new
            {
                b.Id, b.BarbeiroId,
                barbeiro = b.Barbeiro!.Nome,
                inicio = Fuso.ParaLocal(b.InicioUtc),
                fim = Fuso.ParaLocal(b.FimUtc),
                b.Motivo
            }));
        });

        g.MapPost("/bloqueios", async (BloqueioRequest req, AppDbContext db) =>
        {
            if (req.FimLocal <= req.InicioLocal)
                return Results.BadRequest(new { erro = "Fim precisa ser maior que o inicio" });

            db.Bloqueios.Add(new Bloqueio
            {
                BarbeiroId = req.BarbeiroId,
                InicioUtc = Fuso.ParaUtc(req.InicioLocal),
                FimUtc = Fuso.ParaUtc(req.FimLocal),
                Motivo = req.Motivo ?? ""
            });

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        g.MapDelete("/bloqueios/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Bloqueios.Where(b => b.Id == id).ExecuteDeleteAsync() > 0
                ? Results.Ok() : Results.NotFound());
    }
}
