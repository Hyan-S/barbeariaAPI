using Barbearia.Domain;
using Barbearia.Domain.Entities;
using Barbearia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Barbearia.Api;

public static class Seed
{
    public static async Task ExecutarAsync(AppDbContext db)
    {
        if (await db.Barbeiros.AnyAsync()) return;

        var senha = Environment.GetEnvironmentVariable("ADMIN_SENHA") ?? "admin123";
        var email = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@barbearia.local";

        db.Barbeiros.Add(new Barbeiro
        {
            Nome = "Administrador",
            Email = email,
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(senha, 12),
            Perfil = Perfil.Admin,
            Atende = false
        });

        // O barbeiro de exemplo existe para a agenda funcionar na primeira subida: ele
        // e quem carrega o expediente seg-sab, e e dele que o FuncionamentoDaBarbearia
        // copia a grade quando o primeiro funcionario de verdade e cadastrado. Por isso
        // continua ativo e atendendo.
        //
        // O que ele nao e: um login. Antes nascia como Gestor, com as tres permissoes e
        // PrecisaTrocarSenha, e a senha era um Guid aleatorio que nao e mostrado nem
        // guardado em lugar nenhum — ou seja, uma conta de gestao que aparecia na lista
        // de funcionarios e que ninguem nunca conseguiria abrir. Quem tentava entrar com
        // ela levava "E-mail ou senha invalidos" para sempre, sem nada na tela dizendo o
        // porque, e o unico login que funcionava era o do ADMIN_EMAIL.
        //
        // Agora ele nasce sem e-mail: o painel mostra "sem login" na lista, e quando uma
        // pessoa de verdade assumir essa cadeira o admin preenche e-mail e senha em
        // Funcionarios. A senha aleatoria fica so porque a coluna e obrigatoria; sem
        // e-mail ela nao e alcancavel por nenhum caminho de login.
        var barbeiro = new Barbeiro
        {
            Nome = "Joao Barbeiro",
            Email = string.Empty,
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), 12),
            Perfil = Perfil.Barbeiro
        };

        db.Barbeiros.Add(barbeiro);

        db.Servicos.AddRange(
            new Servico { Nome = "Corte", DuracaoMinutos = 30, PrecoCentavos = 4000 },
            new Servico { Nome = "Corte + Barba", DuracaoMinutos = 60, PrecoCentavos = 7000 });

        foreach (var dia in new[]
                 {
                     DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                     DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
                 })
        {
            db.Expedientes.Add(new Expediente
            {
                BarbeiroId = barbeiro.Id,
                DiaSemana = dia,
                HoraInicio = new TimeOnly(9, 0),
                HoraFim = new TimeOnly(12, 0)
            });

            db.Expedientes.Add(new Expediente
            {
                BarbeiroId = barbeiro.Id,
                DiaSemana = dia,
                HoraInicio = new TimeOnly(13, 0),
                HoraFim = new TimeOnly(19, 0)
            });
        }

        await db.SaveChangesAsync();
    }
}
