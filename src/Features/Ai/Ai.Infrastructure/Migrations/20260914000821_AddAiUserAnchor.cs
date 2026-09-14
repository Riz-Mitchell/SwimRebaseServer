using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUserAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_users", x => x.id);
                });

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_ai_users_user_id",
                table: "conversations",
                column: "user_id",
                principalTable: "ai_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_token_usage_records_ai_users_user_id",
                table: "token_usage_records",
                column: "user_id",
                principalTable: "ai_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tool_execution_requests_ai_users_user_id",
                table: "tool_execution_requests",
                column: "user_id",
                principalTable: "ai_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversations_ai_users_user_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_token_usage_records_ai_users_user_id",
                table: "token_usage_records");

            migrationBuilder.DropForeignKey(
                name: "fk_tool_execution_requests_ai_users_user_id",
                table: "tool_execution_requests");

            migrationBuilder.DropTable(
                name: "ai_users");
        }
    }
}
