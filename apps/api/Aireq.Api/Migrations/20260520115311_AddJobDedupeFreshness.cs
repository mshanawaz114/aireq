using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aireq.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJobDedupeFreshness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "canonical_job_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                table: "jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_canonical_job_id",
                table: "jobs",
                column: "canonical_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_content_hash",
                table: "jobs",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_last_seen_at",
                table: "jobs",
                column: "last_seen_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_jobs_canonical_job_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_content_hash",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_last_seen_at",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "canonical_job_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "content_hash",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "jobs");
        }
    }
}
