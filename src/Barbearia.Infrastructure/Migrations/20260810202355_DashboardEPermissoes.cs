using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    public partial class DashboardEPermissoes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PodeVerDashboard",
                table: "barbeiros",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PrecoCentavos",
                table: "agendamentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE agendamentos a
                SET "PrecoCentavos" = s."PrecoCentavos"
                FROM servicos s
                WHERE s."Id" = a."ServicoId";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PodeVerDashboard",
                table: "barbeiros");

            migrationBuilder.DropColumn(
                name: "PrecoCentavos",
                table: "agendamentos");
        }
    }
}
