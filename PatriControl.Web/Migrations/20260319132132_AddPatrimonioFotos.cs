using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatriControl.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPatrimonioFotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatrimonioFotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatrimonioId = table.Column<int>(type: "INTEGER", nullable: false),
                    CaminhoArquivo = table.Column<string>(type: "TEXT", nullable: false),
                    NomeOriginal = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrimonioFotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrimonioFotos_Patrimonios_PatrimonioId",
                        column: x => x.PatrimonioId,
                        principalTable: "Patrimonios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatrimonioFotos_PatrimonioId",
                table: "PatrimonioFotos",
                column: "PatrimonioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatrimonioFotos");
        }
    }
}
