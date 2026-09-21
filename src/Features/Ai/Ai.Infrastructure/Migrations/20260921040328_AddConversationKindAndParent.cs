using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationKindAndParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "conversations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "chat");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_tool_execution_request_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_parent_tool_execution_request_id",
                table: "conversations",
                column: "parent_tool_execution_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_parent_tool_execution_request_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "parent_tool_execution_request_id",
                table: "conversations");
        }
    }
}
