using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aireq.Api.Migrations
{
    /// <inheritdoc />
    public partial class Add401GmailInbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_message_id",
                table: "messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_match_id",
                table: "email_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gmail_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    refresh_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    access_token = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    access_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gmail_accounts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_provider_message_id",
                table: "messages",
                column: "provider_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_logs_to_address",
                table: "email_logs",
                column: "to_address");

            migrationBuilder.CreateIndex(
                name: "ix_gmail_accounts_tenant_id",
                table: "gmail_accounts",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gmail_accounts");

            migrationBuilder.DropIndex(
                name: "ix_messages_provider_message_id",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "ix_email_logs_to_address",
                table: "email_logs");

            migrationBuilder.DropColumn(
                name: "provider_message_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "correlation_match_id",
                table: "email_logs");
        }
    }
}
