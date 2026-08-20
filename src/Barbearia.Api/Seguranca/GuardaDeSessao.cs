using System.Security.Claims;
using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Barbearia.Api.Seguranca;

// Corte de sessao do painel.
//
// As politicas do painel sao assercoes puras em cima das claims do token, e claim
// assinada nao muda mais. Sem este guarda, desativar um funcionario nao tira o
// acesso dele: o token que esta na mao da pessoa continua abrindo a agenda e
// cancelando horario ate vencer, o que pode levar 8 horas.
//
// O lado do cliente nunca teve esse problema porque rele o banco a cada chamada.
// Aqui isso sairia caro — a aplicacao roda em Oregon e o banco em outra regiao,
// entao seria uma ida ao banco a mais em toda requisicao do painel. A leitura
// fica guardada por alguns segundos e e jogada fora na hora em que alguem mexe
// no usuario, entao na pratica o corte e imediato pelo caminho normal, e o cache
// e so o teto de atraso para mudanca feita por fora (SQL na mao, outra instancia).
public static class GuardaDeSessao
{
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(30);

    private record Estado(bool Ativo, DateTime ValidosDesdeUtc);

    public static string Chave(Guid usuarioId) => "sessao-func:" + usuarioId;

    // Mata todo token que ja existe deste usuario. Chame antes do SaveChangesAsync:
    // um token emitido no mesmo segundo ou depois continua valendo, que e o caso do
    // /auth/trocar-senha, onde a propria pessoa recebe um token novo na resposta.
    public static void CortarSessoes(Barbeiro usuario, IMemoryCache cache)
    {
        usuario.TokensValidosDesdeUtc = Segundo(DateTime.UtcNow);
        cache.Remove(Chave(usuario.Id));
    }

    // O selo e a claim do token andam em segundos inteiros. Sem truncar, o selo
    // gravado com milissegundos ficaria a frente do token emitido no mesmo instante
    // e a pessoa cairia para fora na hora de trocar a propria senha.
    public static DateTime Segundo(DateTime valor) => new(
        valor.Year, valor.Month, valor.Day,
        valor.Hour, valor.Minute, valor.Second, DateTimeKind.Utc);

    public static IApplicationBuilder UseGuardaDeSessao(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var usuario = ctx.User;

            // Anonimo passa (quem barra e a policy). Cliente tambem: o
            // ClienteEndpoints le o banco a cada chamada e ja recusa bloqueado.
            if (usuario.Identity?.IsAuthenticated != true || usuario.IsInRole(Papeis.Cliente))
            {
                await next();
                return;
            }

            if (!Guid.TryParse(usuario.FindFirstValue("sub"), out var id))
            {
                await Recusar(ctx);
                return;
            }

            var cache = ctx.RequestServices.GetRequiredService<IMemoryCache>();

            // Estado nulo tambem fica guardado: usuario apagado com token na mao nao
            // deve virar uma ida ao banco por requisicao.
            if (!cache.TryGetValue(Chave(id), out Estado? estado))
            {
                var db = ctx.RequestServices.GetRequiredService<AppDbContext>();

                estado = await db.Barbeiros.AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => new Estado(x.Ativo, x.TokensValidosDesdeUtc))
                    .FirstOrDefaultAsync();

                cache.Set(Chave(id), estado, Validade);
            }

            if (estado is null || !estado.Ativo || Emissao(usuario) < estado.ValidosDesdeUtc)
            {
                await Recusar(ctx);
                return;
            }

            await next();
        });

    // Token sem a claim foi assinado antes desta versao. Ele conta como o mais
    // antigo possivel: morre no primeiro selo e a pessoa entra de novo. Tratar como
    // valido seria abrir justamente o buraco que este guarda fecha.
    public static DateTime Emissao(ClaimsPrincipal usuario) =>
        long.TryParse(usuario.FindFirstValue("emitido"), out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos).UtcDateTime
            : DateTime.MinValue;

    // 401 e nao 403 de proposito: o front trata 401 como sessao expirada, limpa o
    // localStorage e leva para o login, que e exatamente o que tem de acontecer.
    private static Task Recusar(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync("{\"erro\":\"Sua sessao foi encerrada. Entre de novo.\"}");
    }
}
