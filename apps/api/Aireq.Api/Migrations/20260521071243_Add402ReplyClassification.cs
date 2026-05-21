using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aireq.Api.Migrations
{
    /// <inheritdoc />
    public partial class Add402ReplyClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_classified_at",
                table: "recruiter_threads",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_classified_at",
                table: "recruiter_threads");
        }
    }
}
