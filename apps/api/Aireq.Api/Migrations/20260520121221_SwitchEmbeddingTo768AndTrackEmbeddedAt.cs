using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Aireq.Api.Migrations
{
    /// <inheritdoc />
    public partial class SwitchEmbeddingTo768AndTrackEmbeddedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "resumes",
                type: "vector(768)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "embedded_at",
                table: "resumes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "jobs",
                type: "vector(768)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "embedded_at",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedded_at",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "embedded_at",
                table: "jobs");

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "resumes",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(768)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "jobs",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(768)",
                oldNullable: true);
        }
    }
}
