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

        var barbeiro = new Barbeiro
        {
            Nome = "Joao Barbeiro",
            Email = "joao@barbearia.local",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), 12),
            PrecisaTrocarSenha = true,
            Perfil = Perfil.Gestor,
            PodeGerenciarServicos = true,
            PodeGerenciarProdutos = true,
            PodeGerenciarClientes = true
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
