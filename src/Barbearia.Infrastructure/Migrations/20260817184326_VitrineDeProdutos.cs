using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VitrineDeProdutos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avaliacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nota = table.Column<int>(type: "integer", nullable: false),
                    Comentario = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Visivel = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OcultadaEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avaliacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_avaliacoes_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_produto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgendamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos_produto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedidos_produto_agendamentos_AgendamentoId",
                        column: x => x.AgendamentoId,
                        principalTable: "agendamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pedidos_produto_produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_avaliacoes_ProdutoId_Telefone",
                table: "avaliacoes",
                columns: new[] { "ProdutoId", "Telefone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_avaliacoes_ProdutoId_Visivel",
                table: "avaliacoes",
                columns: new[] { "ProdutoId", "Visivel" });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_produto_AgendamentoId_ProdutoId",
                table: "pedidos_produto",
                columns: new[] { "AgendamentoId", "ProdutoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_produto_ProdutoId",
                table: "pedidos_produto",
                column: "ProdutoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avaliacoes");

            migrationBuilder.DropTable(
                name: "pedidos_produto");
        }
    }
}
