using System.Security.Claims;
using Barbearia.Api.Seguranca;
using Barbearia.Application.Acesso;
using Barbearia.Application.Configuracao;
using Barbearia.Application.WhatsApp;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace Barbearia.Api.Endpoints;

public static class AdminEndpoints
{
    public record ConfigRequest(
        string? BarbeariaNome, string? AppUrlPublica, int? MagicLinkMinutos, int? LimiteUsuarios,
        int? IntervaloSlotMinutos, int? AntecedenciaMinimaMinutos, int? DiasMaximosNoFuturo,
        int? HorasMinimasParaCancelar, int? MaxAgendamentosPorCliente,
        bool? WhatsAppHabilitado, string? VerifyToken, string? PhoneNumberId,
        string? NumeroExibicao, string? NumerosPermitidos,
        string? AppSecret, string? AccessToken);

    public record UsuarioRequest(
        string Nome, string Email, string? Senha, string Perfil, bool Atende, bool Ativo,
        bool PodeGerenciarServicos, bool PodeGerenciarProdutos, bool PodeGerenciarClientes,
        string? Telefone, Guid? ClienteId);

    public record TesteRequest(string Telefone);

    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        g.MapGet("/config", async (ConfiguracaoService cfg) =>
            Results.Ok(await cfg.ObterParaTelaAsync()));

        g.MapPut("/config", async (ConfigRequest req, ConfiguracaoService cfg) =>
        {
            await cfg.SalvarAsync(new Dictionary<string, string?>
            {
                [ConfiguracaoService.BarbeariaNome] = req.BarbeariaNome,
                [ConfiguracaoService.AppUrlPublica] = req.AppUrlPublica,
                [ConfiguracaoService.AppMagicLinkMinutos] = req.MagicLinkMinutos?.ToString(),
                [ConfiguracaoService.LimiteUsuarios] = req.LimiteUsuarios?.ToString(),
                [ConfiguracaoService.IntervaloSlot] = req.IntervaloSlotMinutos?.ToString(),
                [ConfiguracaoService.AntecedenciaMinima] = req.AntecedenciaMinimaMinutos?.ToString(),
                [ConfiguracaoService.DiasMaximos] = req.DiasMaximosNoFuturo?.ToString(),
                [ConfiguracaoService.HorasParaCancelar] = req.HorasMinimasParaCancelar?.ToString(),
                [ConfiguracaoService.MaxPorCliente] = req.MaxAgendamentosPorCliente?.ToString(),
                [ConfiguracaoService.WhatsAppHabilitado] = req.WhatsAppHabilitado?.ToString().ToLowerInvariant(),
                [ConfiguracaoService.WhatsAppVerifyToken] = req.VerifyToken,
                [ConfiguracaoService.WhatsAppPhoneNumberId] = req.PhoneNumberId,
                [ConfiguracaoService.WhatsAppNumeroExibicao] = req.NumeroExibicao,
                [ConfiguracaoService.WhatsAppNumerosPermitidos] = req.NumerosPermitidos,
                [ConfiguracaoService.WhatsAppAppSecret] = req.AppSecret,
                [ConfiguracaoService.WhatsAppAccessToken] = req.AccessToken
            });

            return Results.Ok(await cfg.ObterParaTelaAsync());
        });
        g.MapGet("/sistema", async (AppDbContext db, IConfiguration config) =>
        {
            var bruta = config.GetConnectionString("Postgres") ?? "";
            var builder = new NpgsqlConnectionStringBuilder(bruta);

            var conectado = false;
            string? versao = null;
            string? erro = null;

            try
            {
                conectado = await db.Database.CanConnectAsync();
                if (conectado)
                    versao = (await db.Database
                        .SqlQueryRaw<string>("SELECT version() AS \"Value\"")
                        .FirstAsync()).Split(',')[0];
            }
            catch (Exception ex) { erro = ex.Message; }

            return Results.Ok(new
            {
                banco = new
                {
                    host = builder.Host,
                    porta = builder.Port,
                    nome = builder.Database,
                    usuario = builder.Username,
                    senha = string.IsNullOrEmpty(builder.Password) ? "(vazia)" : "••••••••",
                    ssl = builder.SslMode.ToString(),
                    conectado,
                    versao,
                    erro
                },
                aplicacao = new
                {
                    ambiente = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                    dotnet = Environment.Version.ToString(),
                    fuso = Fuso.Barbearia.Id,
                    agoraLocal = Fuso.AgoraLocal(),
                    agoraUtc = DateTime.UtcNow
                },
                contagem = new
                {
                    usuarios = await db.Barbeiros.CountAsync(),
                    clientes = await db.Clientes.CountAsync(),
                    servicos = await db.Servicos.CountAsync(),
                    produtos = await db.Produtos.CountAsync(),
                    agendamentos = await db.Agendamentos.CountAsync()
                },
                variaveis = new[]
                {
                    "ConnectionStrings__Postgres", "Jwt__Secret",
                    "App__UrlPublica", "ADMIN_EMAIL", "ADMIN_SENHA"
                }
            });
        });
        g.MapGet("/usuarios", async (AppDbContext db, ConfiguracaoService cfg) =>
        {
            var lista = await db.Barbeiros.AsNoTracking()
                .OrderBy(x => x.Nome)
                .Select(x => new
                {
                    x.Id, x.Nome, x.Email, x.Ativo, x.Atende, x.PrecisaTrocarSenha,
                    x.Telefone, x.ClienteId,
                    perfil = x.Perfil.ToString(),
                    x.PodeGerenciarServicos, x.PodeGerenciarProdutos, x.PodeGerenciarClientes
                })
                .ToListAsync();

            var limite = await cfg.ObterLimiteUsuariosAsync();

            return Results.Ok(new
            {
                limite,
                emUso = lista.Count(x => x.Ativo),
                usuarios = lista
            });
        });

        g.MapPost("/usuarios", async (
            UsuarioRequest req, AppDbContext db, IHashDeSenha hash, ConfiguracaoService cfg) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email))
                return Results.BadRequest(new { erro = "Informe o e-mail de login" });

            if (string.IsNullOrWhiteSpace(req.Senha) || req.Senha.Length < 8)
                return Results.BadRequest(new { erro = "Senha provisoria precisa de no minimo 8 caracteres" });

            if (!Enum.TryParse<Perfil>(req.Perfil, out var perfil))
                return Results.BadRequest(new { erro = "Perfil invalido" });

            var limite = await cfg.ObterLimiteUsuariosAsync();
            var ativos = await db.Barbeiros.CountAsync(x => x.Ativo);

            if (req.Ativo && ativos >= limite)
                return Results.BadRequest(new
                {
                    erro = $"Limite de {limite} usuarios ativos atingido. " +
                           "Aumente o limite em Configuracao ou desative alguem."
                });

            var email = req.Email.Trim().ToLowerInvariant();

            if (await db.Barbeiros.AnyAsync(x => x.Email.ToLower() == email))
                return Results.Conflict(new { erro = "Ja existe usuario com esse e-mail" });

            Cliente? origem = null;
            if (req.ClienteId.HasValue)
            {
                origem = await db.Clientes.FirstOrDefaultAsync(c => c.Id == req.ClienteId.Value);
                if (origem is null)
                    return Results.BadRequest(new { erro = "Cliente informado nao existe" });

                if (await db.Barbeiros.AnyAsync(x => x.ClienteId == origem.Id))
                    return Results.Conflict(new { erro = "Essa pessoa ja tem cadastro de funcionario" });
            }

            var usuario = new Barbeiro
            {
                Nome = string.IsNullOrWhiteSpace(req.Nome) ? origem?.Nome ?? "" : req.Nome.Trim(),
                Email = email,
                SenhaHash = hash.Gerar(req.Senha),
                Telefone = TelefoneBr.Normalizar(req.Telefone) ?? origem?.Telefone,
                ClienteId = origem?.Id,
                Perfil = perfil,
                Atende = req.Atende,
                Ativo = req.Ativo,
                PrecisaTrocarSenha = true,
                PodeGerenciarServicos = req.PodeGerenciarServicos,
                PodeGerenciarProdutos = req.PodeGerenciarProdutos,
                PodeGerenciarClientes = req.PodeGerenciarClientes
            };

            if (string.IsNullOrWhiteSpace(usuario.Nome))
                return Results.BadRequest(new { erro = "Informe o nome" });

            db.Barbeiros.Add(usuario);

            // Nasce trabalhando no horario da barbearia. Sem isto o funcionario novo
            // ficava com zero expediente, e zero expediente significa nenhum horario na
            // tela do cliente — mesmo depois de ser marcado no servico. Quem nao atende
            // (recepcao, caixa) nao recebe faixa nenhuma, que e o certo: ele nao entra na
            // agenda. O numero volta na resposta para o painel poder avisar quando a
            // barbearia ainda nao tem funcionamento de quem copiar.
            var horarios = usuario.Atende
                ? await FuncionamentoDaBarbearia.AplicarAsync(db, usuario.Id)
                : 0;

            await db.SaveChangesAsync();

            return Results.Ok(new { usuario.Id, horarios });
        });

        g.MapPut("/usuarios/{id:guid}", async (
            Guid id, UsuarioRequest req, AppDbContext db, IHashDeSenha hash,
            ConfiguracaoService cfg, IMemoryCache cache) =>
        {
            var usuario = await db.Barbeiros.FirstOrDefaultAsync(x => x.Id == id);
            if (usuario is null) return Results.NotFound();

            // A edicao nao mexia no e-mail: o campo chegava aqui e era jogado fora, e o
            // painel ainda desabilitava ele na tela. Quem tivesse nascido sem login ficava
            // sem conserto pelo sistema — so por SQL na mao. Agora o login e editavel, que
            // e como se arruma uma conta antiga sem e-mail e como se corrige um e-mail
            // digitado errado.
            if (string.IsNullOrWhiteSpace(req.Email))
                return Results.BadRequest(new { erro = "Informe o e-mail de login" });

            if (string.IsNullOrWhiteSpace(req.Nome))
                return Results.BadRequest(new { erro = "Informe o nome" });

            if (!Enum.TryParse<Perfil>(req.Perfil, out var perfil))
                return Results.BadRequest(new { erro = "Perfil invalido" });
            var deixaDeSerAdmin = usuario.Perfil == Perfil.Admin && (perfil != Perfil.Admin || !req.Ativo);

            if (deixaDeSerAdmin)
            {
                var outros = await db.Barbeiros
                    .CountAsync(x => x.Perfil == Perfil.Admin && x.Ativo && x.Id != id);

                if (outros == 0)
                    return Results.BadRequest(new { erro = "Precisa existir pelo menos um admin ativo" });
            }

            if (req.Ativo && !usuario.Ativo)
            {
                var limite = await cfg.ObterLimiteUsuariosAsync();
                if (await db.Barbeiros.CountAsync(x => x.Ativo) >= limite)
                    return Results.BadRequest(new { erro = $"Limite de {limite} usuarios ativos atingido" });
            }

            // O que estava valendo antes da edicao. Mudanca em qualquer um destes
            // torna o token que a pessoa tem na mao mentiroso, entao a sessao cai.
            // Trocar so o nome ou o telefone nao derruba ninguem.
            var antes = (usuario.Perfil, usuario.Ativo, usuario.PodeGerenciarServicos,
                         usuario.PodeGerenciarProdutos, usuario.PodeGerenciarClientes);

            var email = req.Email.Trim().ToLowerInvariant();

            if (email != usuario.Email
                && await db.Barbeiros.AnyAsync(x => x.Email.ToLower() == email && x.Id != id))
                return Results.Conflict(new { erro = "Ja existe usuario com esse e-mail" });

            usuario.Email = email;
            usuario.Nome = req.Nome.Trim();
            usuario.Telefone = TelefoneBr.Normalizar(req.Telefone);
            usuario.Perfil = perfil;
            usuario.Atende = req.Atende;
            usuario.Ativo = req.Ativo;
            usuario.PodeGerenciarServicos = req.PodeGerenciarServicos;
            usuario.PodeGerenciarProdutos = req.PodeGerenciarProdutos;
            usuario.PodeGerenciarClientes = req.PodeGerenciarClientes;

            var trocouSenha = false;

            if (!string.IsNullOrWhiteSpace(req.Senha))
            {
                if (req.Senha.Length < 8)
                    return Results.BadRequest(new { erro = "Senha precisa de no minimo 8 caracteres" });
                usuario.SenhaHash = hash.Gerar(req.Senha);
                usuario.PrecisaTrocarSenha = true;
                trocouSenha = true;
            }

            var depois = (usuario.Perfil, usuario.Ativo, usuario.PodeGerenciarServicos,
                          usuario.PodeGerenciarProdutos, usuario.PodeGerenciarClientes);

            if (trocouSenha || antes != depois)
                GuardaDeSessao.CortarSessoes(usuario, cache);

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Tira o funcionario do banco de vez. Desativar ja corta o acesso na hora (e o
        // GuardaDeSessao que faz isso); excluir e para quando o cadastro nao deve nem
        // aparecer mais na lista. As travas e o porque delas estao em RegraDeExclusao.
        g.MapDelete("/usuarios/{id:guid}", async (
            Guid id, ClaimsPrincipal quem, AppDbContext db, IMemoryCache cache) =>
        {
            var usuario = await db.Barbeiros.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Ativo })
                .FirstOrDefaultAsync();

            if (usuario is null) return Results.NotFound();

            // Antes de qualquer outra coisa: ninguem se apaga. Um admin que se exclui
            // sai do sistema no meio da propria requisicao e nao tem como voltar.
            if (Guid.TryParse(quem.FindFirstValue("sub"), out var eu) && eu == id)
                return RegraDeExclusao.Recusa("Nao da para excluir o seu proprio usuario.");

            if (usuario.Ativo)
                return RegraDeExclusao.Recusa(
                    "Este funcionario ainda esta ativo. Desative primeiro — so isso ja tira o "
                    + "acesso dele na hora — e exclua depois, se quiser tirar o cadastro tambem.");

            var agendamentos = await db.Agendamentos.CountAsync(a => a.BarbeiroId == id);

            if (agendamentos > 0)
                return RegraDeExclusao.Recusa(
                    RegraDeExclusao.Contagem(agendamentos, "agendamento esta", "agendamentos estao")
                    + " no nome deste funcionario, e o historico da agenda depende dele para dizer "
                    + "quem atendeu. Deixe inativo: ele nao entra mais e nao aparece para marcar.");

            // Fechou caixa de atendimento de outra pessoa. FechadoPorId nao e chave
            // estrangeira, entao o banco deixaria excluir — e o registro do caixa
            // passaria a apontar para um id que nao existe mais, sem ninguem perceber.
            var fechamentos = await db.Agendamentos.CountAsync(a => a.FechadoPorId == id);

            if (fechamentos > 0)
                return RegraDeExclusao.Recusa(
                    "Este funcionario fechou "
                    + RegraDeExclusao.Contagem(fechamentos, "atendimento", "atendimentos")
                    + " no caixa. Excluir o cadastro apagaria o nome de quem recebeu esse "
                    + "dinheiro. Deixe inativo.");

            // Expedientes, bloqueios e os vinculos com servico caem junto, por Cascade.
            await db.Barbeiros.Where(x => x.Id == id).ExecuteDeleteAsync();

            // Sem isso o guarda de sessao continuaria respondendo pela leitura guardada
            // por ate 30 segundos. Nao e falha de seguranca — o cadastro estava inativo,
            // e inativo ja e recusado — mas deixa a entrada morta ocupando o cache.
            cache.Remove(GuardaDeSessao.Chave(id));

            return Results.Ok();
        });

        g.MapGet("/pessoas", async (string? busca, AppDbContext db) =>
        {
            var termo = (busca ?? "").Trim();
            var telefone = TelefoneBr.Normalizar(termo);

            var funcionarios = await db.Barbeiros.AsNoTracking()
                .Where(x => termo == ""
                            || EF.Functions.ILike(x.Nome, $"%{termo}%")
                            || EF.Functions.ILike(x.Email, $"%{termo}%"))
                .Select(x => new
                {
                    x.Id, x.Nome, x.Email, x.Telefone, x.Ativo, x.ClienteId,
                    perfil = x.Perfil.ToString()
                })
                .ToListAsync();

            var clientes = await db.Clientes.AsNoTracking()
                .Where(x => termo == ""
                            || (telefone != null && x.Telefone == telefone)
                            || EF.Functions.ILike(x.Nome, $"%{termo}%"))
                .Select(x => new
                {
                    x.Id, x.Nome, x.Telefone, x.Bloqueado,
                    agendamentos = x.Agendamentos.Count(a => a.Status != StatusAgendamento.Cancelado)
                })
                .Take(200)
                .ToListAsync();

            var vinculados = funcionarios.Where(f => f.ClienteId.HasValue)
                .ToDictionary(f => f.ClienteId!.Value, f => f.perfil);

            return Results.Ok(new
            {
                funcionarios = funcionarios.Select(f => new
                {
                    f.Id, f.Nome, f.Email, f.Ativo, f.perfil, f.ClienteId,
                    telefone = f.Telefone is null ? null : TelefoneBr.Formatar(f.Telefone)
                }),
                clientes = clientes.Select(c => new
                {
                    c.Id, c.Nome, c.Bloqueado, c.agendamentos,
                    telefone = TelefoneBr.Formatar(c.Telefone),
                    ehFuncionario = vinculados.TryGetValue(c.Id, out var p) ? p : null
                })
            });
        });

        g.MapPost("/whatsapp/teste", async (
            TesteRequest req, IWhatsAppClient whatsapp, ConfiguracaoService cfg) =>
        {
            var telefone = TelefoneBr.Normalizar(req.Telefone);
            if (telefone is null) return Results.BadRequest(new { erro = "Telefone invalido" });

            var config = await cfg.ObterWhatsAppAsync();
            if (!config.EstaConfigurado())
                return Results.BadRequest(new { erro = "WhatsApp ainda nao esta configurado" });

            if (!config.PodeAtender(telefone))
                return Results.BadRequest(new { erro = "Esse numero nao esta na lista de permitidos" });

            await whatsapp.EnviarTextoAsync(telefone,
                "Teste da API da barbearia. Se voce recebeu isso, a integracao esta funcionando.");

            return Results.Ok(new { enviado = true });
        });
    }
}
