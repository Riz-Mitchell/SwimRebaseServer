using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_user_id_last_message_at",
                table: "conversations");

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_id_is_archived_last_message_at",
                table: "conversations",
                columns: new[] { "user_id", "is_archived", "last_message_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_user_id_is_archived_last_message_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "conversations");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_id_last_message_at",
                table: "conversations",
                columns: new[] { "user_id", "last_message_at" });
        }
    }
}
