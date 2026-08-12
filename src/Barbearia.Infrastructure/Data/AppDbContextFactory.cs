using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Barbearia.Infrastructure.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conexao = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                      ?? "Host=localhost;Database=barbearia;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conexao)
            .Options;

        return new AppDbContext(options);
    }
}
