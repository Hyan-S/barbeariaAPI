using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    public partial class FuncionarioPessoa : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClienteId",
                table: "barbeiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefone",
                table: "barbeiros",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_barbeiros_ClienteId",
                table: "barbeiros",
                column: "ClienteId");

            migrationBuilder.AddForeignKey(
                name: "FK_barbeiros_clientes_ClienteId",
                table: "barbeiros",
                column: "ClienteId",
                principalTable: "clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_barbeiros_clientes_ClienteId",
                table: "barbeiros");

            migrationBuilder.DropIndex(
                name: "IX_barbeiros_ClienteId",
                table: "barbeiros");

            migrationBuilder.DropColumn(
                name: "ClienteId",
                table: "barbeiros");

            migrationBuilder.DropColumn(
                name: "Telefone",
                table: "barbeiros");
        }
    }
}
