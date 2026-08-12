using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    public partial class PerfisEConfiguracao : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Administrador",
                table: "barbeiros",
                newName: "Atende");

            migrationBuilder.AddColumn<int>(
                name: "Perfil",
                table: "barbeiros",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "configuracoes",
                columns: table => new
                {
                    Chave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Valor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Secreto = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracoes", x => x.Chave);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracoes");

            migrationBuilder.DropColumn(
                name: "Perfil",
                table: "barbeiros");

            migrationBuilder.RenameColumn(
                name: "Atende",
                table: "barbeiros",
                newName: "Administrador");
        }
    }
}
