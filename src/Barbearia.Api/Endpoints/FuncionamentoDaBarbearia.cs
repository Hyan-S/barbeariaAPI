using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api.Endpoints;

// O "horario da barbearia" nunca existiu como cadastro: expediente e sempre por
// pessoa, e funcionario novo nascia sem nenhum. Esse e o bug que ninguem conseguia
// explicar — cadastrar o profissional, marcar ele no servico, e a tela de horarios
// continuar vazia. O DisponibilidadeService desiste logo depois de ler os
// expedientes do dia: sem faixa de trabalho nao ha o que varrer, e ele devolve lista
// vazia sem erro nenhum. Nada na tela dizia o porque.
//
// Como nao ha cadastro de funcionamento da loja, ele e deduzido do que a equipe
// pratica: a grade mais repetida entre quem esta ativo e atende. Numa barbearia onde
// todos trabalham das 9 as 18 de terca a sabado, e exatamente essa.
//
// Copio a grade de uma pessoa so, e nao a soma de todas, de proposito: juntar o
// 09-12 de um com o 09-18 de outro criaria duas faixas sobrepostas no mesmo dia, e o
// mesmo horario apareceria duas vezes para o cliente escolher.
internal static class FuncionamentoDaBarbearia
{
    public record Janela(DayOfWeek DiaSemana, TimeOnly HoraInicio, TimeOnly HoraFim);

    // Vem vazio quando ninguem na loja tem horario ainda: a primeira pessoa cadastrada
    // nao tem de quem herdar, e ai o funcionamento e digitado a mao mesmo.
    public static async Task<List<Janela>> ModeloAsync(AppDbContext db, Guid? ignorar = null)
    {
        var expedientes = await db.Expedientes.AsNoTracking()
            .Where(e => e.Barbeiro!.Ativo && e.Barbeiro.Atende
                        && (ignorar == null || e.BarbeiroId != ignorar))
            .Select(e => new { e.BarbeiroId, e.DiaSemana, e.HoraInicio, e.HoraFim })
            .ToListAsync();

        var porPessoa = expedientes
            .GroupBy(e => e.BarbeiroId)
            .Select(g => g
                .Select(e => new Janela(e.DiaSemana, e.HoraInicio, e.HoraFim))
                .Distinct()
                .OrderBy(j => j.DiaSemana).ThenBy(j => j.HoraInicio)
                .ToList())
            .ToList();

        // Desempate: primeiro a grade que mais gente pratica, depois a mais cheia. Duas
        // grades diferentes com o mesmo tanto de gente e o mesmo tanto de faixas empatam
        // de verdade — qualquer uma serve, e da para ajustar em Funcionamento depois.
        return porPessoa
            .GroupBy(Assinatura)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.First().Count)
            .FirstOrDefault()?.First() ?? [];
    }

    private static string Assinatura(List<Janela> janelas) => string.Join("|",
        janelas.Select(j => $"{(int)j.DiaSemana}:{j.HoraInicio.Ticks}-{j.HoraFim.Ticks}"));

    // Devolve quantas faixas entraram. Nao chama SaveChanges: quem chamou decide
    // quando, porque no cadastro de funcionario isso vai junto com o proprio usuario.
    public static async Task<int> AplicarAsync(AppDbContext db, Guid barbeiroId)
    {
        var modelo = await ModeloAsync(db, barbeiroId);

        foreach (var janela in modelo)
            db.Expedientes.Add(new Expediente
            {
                BarbeiroId = barbeiroId,
                DiaSemana = janela.DiaSemana,
                HoraInicio = janela.HoraInicio,
                HoraFim = janela.HoraFim
            });

        return modelo.Count;
    }
}
