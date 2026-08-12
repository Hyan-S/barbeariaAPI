using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barbearia.Infrastructure.Migrations
{
    public partial class Inicial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "barbeiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SenhaHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Administrador = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_barbeiros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Bloqueado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mensagens_processadas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessadaEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensagens_processadas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "servicos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DuracaoMinutos = table.Column<int>(type: "integer", nullable: false),
                    PrecoCentavos = table.Column<int>(type: "integer", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_servicos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bloqueios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BarbeiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    InicioUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FimUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloqueios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bloqueios_barbeiros_BarbeiroId",
                        column: x => x.BarbeiroId,
                        principalTable: "barbeiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expedientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BarbeiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiaSemana = table.Column<int>(type: "integer", nullable: false),
                    HoraInicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    HoraFim = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expedientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expedientes_barbeiros_BarbeiroId",
                        column: x => x.BarbeiroId,
                        principalTable: "barbeiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversa_estados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropostaInicioUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PropostaBarbeiroId = table.Column<Guid>(type: "uuid", nullable: true),
                    PropostaServicoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiraEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversa_estados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversa_estados_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "magic_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiraEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_magic_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_magic_links_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agendamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BarbeiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    InicioUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FimUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<int>(type: "integer", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CanceladoEmUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agendamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agendamentos_barbeiros_BarbeiroId",
                        column: x => x.BarbeiroId,
                        principalTable: "barbeiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agendamentos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agendamentos_servicos_ServicoId",
                        column: x => x.ServicoId,
                        principalTable: "servicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_BarbeiroId_InicioUtc",
                table: "agendamentos",
                columns: new[] { "BarbeiroId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_ClienteId_InicioUtc",
                table: "agendamentos",
                columns: new[] { "ClienteId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_ServicoId",
                table: "agendamentos",
                column: "ServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_barbeiros_Email",
                table: "barbeiros",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bloqueios_BarbeiroId_InicioUtc_FimUtc",
                table: "bloqueios",
                columns: new[] { "BarbeiroId", "InicioUtc", "FimUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_Telefone",
                table: "clientes",
                column: "Telefone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversa_estados_ClienteId",
                table: "conversa_estados",
                column: "ClienteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expedientes_BarbeiroId_DiaSemana",
                table: "expedientes",
                columns: new[] { "BarbeiroId", "DiaSemana" });

            migrationBuilder.CreateIndex(
                name: "IX_magic_links_ClienteId",
                table: "magic_links",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_magic_links_TokenHash",
                table: "magic_links",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mensagens_processadas_ProcessadaEmUtc",
                table: "mensagens_processadas",
                column: "ProcessadaEmUtc");

            migrationBuilder.Sql("""
                ALTER TABLE agendamentos
                ADD CONSTRAINT ck_agendamentos_sem_sobreposicao
                EXCLUDE USING gist (
                    "BarbeiroId" WITH =,
                    tstzrange("InicioUtc", "FimUtc") WITH &&
                ) WHERE ("Status" <> 2);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agendamentos");

            migrationBuilder.DropTable(
                name: "bloqueios");

            migrationBuilder.DropTable(
                name: "conversa_estados");

            migrationBuilder.DropTable(
                name: "expedientes");

            migrationBuilder.DropTable(
                name: "magic_links");

            migrationBuilder.DropTable(
                name: "mensagens_processadas");

            migrationBuilder.DropTable(
                name: "servicos");

            migrationBuilder.DropTable(
                name: "barbeiros");

            migrationBuilder.DropTable(
                name: "clientes");
        }
    }
}
