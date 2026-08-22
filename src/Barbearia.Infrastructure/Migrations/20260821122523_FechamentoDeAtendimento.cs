using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FechamentoDeAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrecoCentavosNaVenda",
                table: "pedidos_produto",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantidade",
                table: "pedidos_produto",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Vendido",
                table: "pedidos_produto",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechadoEmUtc",
                table: "agendamentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FechadoPorId",
                table: "agendamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FormaPagamento",
                table: "agendamentos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValorCobradoCentavos",
                table: "agendamentos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_FechadoEmUtc",
                table: "agendamentos",
                column: "FechadoEmUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agendamentos_FechadoEmUtc",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "PrecoCentavosNaVenda",
                table: "pedidos_produto");

            migrationBuilder.DropColumn(
                name: "Quantidade",
                table: "pedidos_produto");

            migrationBuilder.DropColumn(
                name: "Vendido",
                table: "pedidos_produto");

            migrationBuilder.DropColumn(
                name: "FechadoEmUtc",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "FechadoPorId",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "FormaPagamento",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "ValorCobradoCentavos",
                table: "agendamentos");
        }
    }
}
