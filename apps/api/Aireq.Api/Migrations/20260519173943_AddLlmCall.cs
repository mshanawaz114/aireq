using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aireq.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmCall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "llm_calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd_estimate = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    prompt_text = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    response_text = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_llm_calls", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_llm_calls_purpose_created_at",
                table: "llm_calls",
                columns: new[] { "purpose", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_llm_calls_tenant_id_model_created_at",
                table: "llm_calls",
                columns: new[] { "tenant_id", "model", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "llm_calls");
        }
    }
}
