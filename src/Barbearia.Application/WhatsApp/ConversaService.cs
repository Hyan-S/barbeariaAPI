using Barbearia.Application.Acesso;
using Barbearia.Application.Agendamentos;
using Barbearia.Application.Configuracao;
using Barbearia.Application.Disponibilidade;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Barbearia.Application.WhatsApp;

public class ConversaService(
    IAppDbContext db,
    AgendamentoService agendamentos,
    DisponibilidadeService disponibilidade,
    MagicLinkService magicLinks,
    IWhatsAppClient whatsapp,
    ConfiguracaoService configuracao,
    ILogger<ConversaService> logger)
{
    private const int MinutosValidadeProposta = 15;

    public async Task ProcessarTextoAsync(string telefone, string? nomePerfil, string texto,
        CancellationToken ct = default)
    {
        if (!await AtendeAsync(telefone, ct)) return;

        var cliente = await agendamentos.ObterOuCriarClienteAsync(telefone, nomePerfil, ct);
        if (cliente.Bloqueado) return;

        var leitura = InterpretadorMensagem.Ler(texto);

        logger.LogInformation("WhatsApp de {Telefone}: intencao={Intencao} confianca={Confianca}",
            cliente.Telefone, leitura.Intencao, leitura.Confianca);

        switch (leitura.Intencao)
        {
            case Intencao.Confirmar:
                await ConfirmarPropostaAsync(cliente, ct);
                break;

            case Intencao.Negar:
                await LimparPropostaAsync(cliente.Id, ct);
                await EnviarLinkAsync(cliente, "Sem problema! Escolhe o melhor horario aqui:", ct);
                break;

            case Intencao.Cancelar:
                await CancelarProximoAsync(cliente, ct);
                break;

            case Intencao.ListarMeus:
                await ListarAgendamentosAsync(cliente, ct);
                break;

            case Intencao.Agendar:
                await TratarPedidoDeAgendamentoAsync(cliente, leitura, ct);
                break;

            case Intencao.Saudacao:
                await whatsapp.EnviarTextoAsync(cliente.Telefone,
                    $"Opa{(string.IsNullOrWhiteSpace(cliente.Nome) ? "" : $", {PrimeiroNome(cliente.Nome)}")}! " +
                    "E so me dizer o dia e a hora que voce quer (ex.: \"amanha as 15h\") " +
                    "que eu ja vejo se ta livre. Se preferir escolher na agenda, e so pedir o link.", ct);
                break;

            default:
                await EnviarLinkAsync(cliente,
                    "Nao consegui entender o horario. Da uma olhada na agenda e escolhe o que preferir:", ct);
                break;
        }
    }

    public async Task ProcessarSelecaoAsync(string telefone, string? nomePerfil, string idOpcao,
        CancellationToken ct = default)
    {
        if (!await AtendeAsync(telefone, ct)) return;

        var cliente = await agendamentos.ObterOuCriarClienteAsync(telefone, nomePerfil, ct);
        if (cliente.Bloqueado) return;

        if (idOpcao == Opcoes.Confirmar)
        {
            await ConfirmarPropostaAsync(cliente, ct);
            return;
        }

        if (idOpcao == Opcoes.Link)
        {
            await EnviarLinkAsync(cliente, "Beleza! Escolhe aqui:", ct);
            return;
        }

        if (Opcoes.TentarLerSlot(idOpcao, out var barbeiroId, out var servicoId, out var inicioUtc))
        {
            await AgendarAsync(cliente, servicoId, inicioUtc, barbeiroId, ct);
            return;
        }

        await EnviarLinkAsync(cliente, "Escolhe o horario por aqui:", ct);
    }

    private async Task TratarPedidoDeAgendamentoAsync(Cliente cliente, LeituraMensagem leitura,
        CancellationToken ct)
    {
        var servico = await ServicoPadraoAsync(ct);
        if (servico is null)
        {
            await whatsapp.EnviarTextoAsync(cliente.Telefone,
                "A agenda ainda nao esta configurada. Fala com a gente daqui a pouco!", ct);
            return;
        }

        if (leitura.Confianca == Confianca.Baixa || !leitura.Data.HasValue)
        {
            await EnviarLinkAsync(cliente,
                "Me diz o dia e a hora (ex.: \"sexta as 10h\") ou escolhe direto na agenda:", ct);
            return;
        }

        var dia = leitura.Data.Value;

        if (leitura.Hora.HasValue)
        {
            var inicioUtc = Fuso.ParaUtc(dia.ToDateTime(leitura.Hora.Value));
            var slot = await disponibilidade.ObterExatoAsync(inicioUtc, servico.Id, ct: ct);

            if (slot is not null)
            {
                await ProporAsync(cliente, slot, servico, ct);
                return;
            }

            var sugestoes = await disponibilidade.SugerirProximosAsync(
                inicioUtc, servico.Id, quantidade: 5, ct: ct);

            if (sugestoes.Count == 0)
            {
                await EnviarLinkAsync(cliente,
                    $"Nao achei vaga perto de {leitura.Hora.Value:HH\\:mm} de {Dia(dia)}. " +
                    "Da uma olhada na agenda completa:", ct);
                return;
            }

            await whatsapp.EnviarListaAsync(cliente.Telefone,
                $"{leitura.Hora.Value:HH\\:mm} de {Dia(dia)} ja esta ocupado. " +
                "Esses aqui estao livres — qual prefere?",
                "Ver horarios",
                sugestoes.Select(s => OpcaoDoSlot(s, servico)).ToList(), ct);
            return;
        }

        var slots = await disponibilidade.ObterDoDiaAsync(dia, servico.Id, ct: ct);
        slots = FiltrarPorPeriodo(slots, leitura.Periodo);

        if (slots.Count == 0)
        {
            var alternativas = await disponibilidade.SugerirProximosAsync(
                Fuso.ParaUtc(dia.ToDateTime(new TimeOnly(9, 0))), servico.Id, quantidade: 5, ct: ct);

            if (alternativas.Count == 0)
            {
                await EnviarLinkAsync(cliente,
                    $"Nao tenho horario livre em {Dia(dia)}. Ve a agenda completa aqui:", ct);
                return;
            }

            await whatsapp.EnviarListaAsync(cliente.Telefone,
                $"Em {Dia(dia)} nao sobrou horario. O mais proximo que tenho:",
                "Ver horarios",
                alternativas.Select(s => OpcaoDoSlot(s, servico)).ToList(), ct);
            return;
        }

        await whatsapp.EnviarListaAsync(cliente.Telefone,
            $"Tenho esses horarios em {Dia(dia)}. Qual fica melhor?",
            "Ver horarios",
            EspalharSlots(slots).Select(s => OpcaoDoSlot(s, servico)).ToList(), ct);
    }

    private async Task ProporAsync(Cliente cliente, Slot slot, Servico servico, CancellationToken ct)
    {
        await SalvarPropostaAsync(cliente.Id, slot, servico.Id, ct);

        await whatsapp.EnviarBotoesAsync(cliente.Telefone,
            $"{Dia(DateOnly.FromDateTime(slot.InicioLocal))} as {slot.HoraFormatada} esta livre " +
            $"com {PrimeiroNome(slot.BarbeiroNome)} ({servico.Nome}, {servico.DuracaoMinutos}min). Confirma?",
            [
                new OpcaoInterativa(Opcoes.Confirmar, "Confirmar"),
                new OpcaoInterativa(Opcoes.Link, "Outro horario")
            ], ct);
    }

    private async Task ConfirmarPropostaAsync(Cliente cliente, CancellationToken ct)
    {
        var estado = await db.ConversaEstados.FirstOrDefaultAsync(x => x.ClienteId == cliente.Id, ct);

        if (estado is null || !estado.TemPropostaValida(DateTime.UtcNow))
        {
            await EnviarLinkAsync(cliente,
                "Nao tenho nenhum horario pendente pra confirmar. Escolhe um aqui:", ct);
            return;
        }

        await AgendarAsync(cliente, estado.PropostaServicoId!.Value, estado.PropostaInicioUtc!.Value,
            estado.PropostaBarbeiroId, ct);
    }

    private async Task AgendarAsync(Cliente cliente, Guid servicoId, DateTime inicioUtc,
        Guid? barbeiroId, CancellationToken ct)
    {
        var resultado = await agendamentos.CriarAsync(
            cliente.Id, servicoId, inicioUtc, barbeiroId, OrigemAgendamento.WhatsApp, ct: ct);

        await LimparPropostaAsync(cliente.Id, ct);

        if (resultado.Sucesso)
        {
            var a = resultado.Agendamento!;
            var local = Fuso.ParaLocal(a.InicioUtc);

            await whatsapp.EnviarTextoAsync(cliente.Telefone,
                $"Fechado! {Dia(DateOnly.FromDateTime(local))} as {local:HH\\:mm}. " +
                "Se precisar desmarcar, e so me mandar \"cancelar\".", ct);
            return;
        }

        var mensagem = resultado.Tipo switch
        {
            ResultadoTipo.HorarioIndisponivel =>
                "Esse horario acabou de ser preenchido. Escolhe outro aqui:",
            ResultadoTipo.ForaDaAntecedencia =>
                "Esse horario ja esta muito em cima. Ve as opcoes aqui:",
            ResultadoTipo.ForaDaJanelaDeAgenda =>
                "Ainda nao abri a agenda pra essa data. Ve o que esta disponivel:",
            ResultadoTipo.LimiteDeAgendamentosAtingido =>
                "Voce ja tem agendamentos abertos. Cancela um antes de marcar outro — " +
                "e so mandar \"cancelar\". Pra consultar:",
            _ => "Nao consegui concluir o agendamento. Tenta por aqui:"
        };

        await EnviarLinkAsync(cliente, mensagem, ct);
    }

    private async Task CancelarProximoAsync(Cliente cliente, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        var proximo = await db.Agendamentos
            .Include(a => a.Servico)
            .Where(a => a.ClienteId == cliente.Id
                        && a.InicioUtc > agora
                        && (a.Status == StatusAgendamento.Pendente || a.Status == StatusAgendamento.Confirmado))
            .OrderBy(a => a.InicioUtc)
            .FirstOrDefaultAsync(ct);

        if (proximo is null)
        {
            await whatsapp.EnviarTextoAsync(cliente.Telefone,
                "Voce nao tem nenhum agendamento marcado.", ct);
            return;
        }

        var local = Fuso.ParaLocal(proximo.InicioUtc);
        var cancelou = await agendamentos.CancelarAsync(proximo.Id, cliente.Id, true, ct);

        await whatsapp.EnviarTextoAsync(cliente.Telefone,
            cancelou
                ? $"Cancelado: {Dia(DateOnly.FromDateTime(local))} as {local:HH\\:mm}. " +
                  "Quando quiser remarcar e so chamar!"
                : $"Seu horario de {Dia(DateOnly.FromDateTime(local))} as {local:HH\\:mm} esta muito proximo " +
                  "pra cancelar pelo automatico. Fala direto com a gente, por favor.", ct);
    }

    private async Task ListarAgendamentosAsync(Cliente cliente, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        var lista = await db.Agendamentos
            .Include(a => a.Servico)
            .Include(a => a.Barbeiro)
            .Where(a => a.ClienteId == cliente.Id
                        && a.InicioUtc > agora
                        && (a.Status == StatusAgendamento.Pendente || a.Status == StatusAgendamento.Confirmado))
            .OrderBy(a => a.InicioUtc)
            .Take(5)
            .ToListAsync(ct);

        if (lista.Count == 0)
        {
            await EnviarLinkAsync(cliente, "Voce nao tem horario marcado. Bora marcar?", ct);
            return;
        }

        var linhas = lista.Select(a =>
        {
            var local = Fuso.ParaLocal(a.InicioUtc);
            return $"- {Dia(DateOnly.FromDateTime(local))} as {local:HH\\:mm} " +
                   $"({a.Servico?.Nome} com {PrimeiroNome(a.Barbeiro?.Nome ?? "")})";
        });

        await whatsapp.EnviarTextoAsync(cliente.Telefone,
            "Seus horarios:\n" + string.Join("\n", linhas), ct);
    }

    private async Task EnviarLinkAsync(Cliente cliente, string mensagem, CancellationToken ct)
    {
        var url = await magicLinks.GerarUrlAsync(cliente.Id, ct);
        await whatsapp.EnviarTextoAsync(cliente.Telefone, $"{mensagem}\n{url}", ct);
    }

    private async Task SalvarPropostaAsync(Guid clienteId, Slot slot, Guid servicoId, CancellationToken ct)
    {
        var estado = await db.ConversaEstados.FirstOrDefaultAsync(x => x.ClienteId == clienteId, ct);

        if (estado is null)
        {
            estado = new ConversaEstado { ClienteId = clienteId };
            db.ConversaEstados.Add(estado);
        }

        estado.PropostaInicioUtc = slot.InicioUtc;
        estado.PropostaBarbeiroId = slot.BarbeiroId;
        estado.PropostaServicoId = servicoId;
        estado.ExpiraEmUtc = DateTime.UtcNow.AddMinutes(MinutosValidadeProposta);
        estado.AtualizadoEmUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private async Task LimparPropostaAsync(Guid clienteId, CancellationToken ct)
    {
        var estado = await db.ConversaEstados.FirstOrDefaultAsync(x => x.ClienteId == clienteId, ct);
        if (estado is null) return;

        estado.PropostaInicioUtc = null;
        estado.PropostaBarbeiroId = null;
        estado.PropostaServicoId = null;
        estado.AtualizadoEmUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private Task<Servico?> ServicoPadraoAsync(CancellationToken ct) =>
        db.Servicos.AsNoTracking()
            .Where(s => s.Ativo)
            .OrderBy(s => s.DuracaoMinutos)
            .FirstOrDefaultAsync(ct);

    private static IReadOnlyList<Slot> FiltrarPorPeriodo(IReadOnlyList<Slot> slots, PeriodoDia? periodo) =>
        periodo switch
        {
            PeriodoDia.Manha => slots.Where(s => s.InicioLocal.Hour < 12).ToList(),
            PeriodoDia.Tarde => slots.Where(s => s.InicioLocal.Hour is >= 12 and < 18).ToList(),
            PeriodoDia.Noite => slots.Where(s => s.InicioLocal.Hour >= 18).ToList(),
            _ => slots
        };
    private static IReadOnlyList<Slot> EspalharSlots(IReadOnlyList<Slot> slots, int maximo = 10)
    {
        if (slots.Count <= maximo) return slots;

        var passo = (double)slots.Count / maximo;
        return Enumerable.Range(0, maximo)
            .Select(i => slots[(int)(i * passo)])
            .ToList();
    }

    private OpcaoInterativa OpcaoDoSlot(Slot slot, Servico servico) => new(
        Opcoes.Slot(slot.BarbeiroId, servico.Id, slot.InicioUtc),
        $"{slot.InicioLocal:dd/MM} as {slot.HoraFormatada}",
        PrimeiroNome(slot.BarbeiroNome));

    private async Task<bool> AtendeAsync(string telefone, CancellationToken ct)
    {
        var canonico = TelefoneBr.Normalizar(telefone);
        var cfg = await configuracao.ObterWhatsAppAsync(ct);

        if (canonico is null || !cfg.PodeAtender(canonico))
        {
            logger.LogWarning("Mensagem de {Telefone} ignorada: fora da lista de numeros permitidos",
                telefone);
            return false;
        }

        return true;
    }

    private static string PrimeiroNome(string nome) =>
        string.IsNullOrWhiteSpace(nome) ? nome : nome.Split(' ')[0];
    private static string Dia(DateOnly data)
    {
        var hoje = Fuso.HojeLocal();
        if (data == hoje) return "hoje";
        if (data == hoje.AddDays(1)) return "amanha";

        var nome = data.DayOfWeek switch
        {
            DayOfWeek.Sunday => "domingo",
            DayOfWeek.Monday => "segunda",
            DayOfWeek.Tuesday => "terca",
            DayOfWeek.Wednesday => "quarta",
            DayOfWeek.Thursday => "quinta",
            DayOfWeek.Friday => "sexta",
            _ => "sabado"
        };

        return $"{nome} ({data:dd/MM})";
    }
}
public static class Opcoes
{
    public const string Confirmar = "confirmar";
    public const string Link = "link";

    public static string Slot(Guid barbeiroId, Guid servicoId, DateTime inicioUtc) =>
        $"s|{barbeiroId:N}|{servicoId:N}|{inicioUtc.Ticks}";

    public static bool TentarLerSlot(string id, out Guid barbeiroId, out Guid servicoId,
        out DateTime inicioUtc)
    {
        barbeiroId = default;
        servicoId = default;
        inicioUtc = default;

        var partes = id.Split('|');
        if (partes.Length != 4 || partes[0] != "s") return false;

        if (!Guid.TryParseExact(partes[1], "N", out barbeiroId)) return false;
        if (!Guid.TryParseExact(partes[2], "N", out servicoId)) return false;
        if (!long.TryParse(partes[3], out var ticks)) return false;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return false;

        inicioUtc = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }
}
