using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProdutosEPermissoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PodeGerenciarClientes",
                table: "barbeiros",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PodeGerenciarProdutos",
                table: "barbeiros",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PodeGerenciarServicos",
                table: "barbeiros",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrecisaTrocarSenha",
                table: "barbeiros",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "produtos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    PrecoCentavos = table.Column<int>(type: "integer", nullable: false),
                    Estoque = table.Column<int>(type: "integer", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_produtos", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "produtos");

            migrationBuilder.DropColumn(
                name: "PodeGerenciarClientes",
                table: "barbeiros");

            migrationBuilder.DropColumn(
                name: "PodeGerenciarProdutos",
                table: "barbeiros");

            migrationBuilder.DropColumn(
                name: "PodeGerenciarServicos",
                table: "barbeiros");

            migrationBuilder.DropColumn(
                name: "PrecisaTrocarSenha",
                table: "barbeiros");
        }
    }
}
