using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "token_usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_creation_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_read_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    was_compaction_call = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_usage_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    raw_content_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_compacted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    covered_through_sequence_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_summaries_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tool_execution_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anthropic_tool_use_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    input_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    denial_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_execution_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_tool_execution_requests_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_id_is_compacted",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "is_compacted" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_id_sequence_number",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_summaries_conversation_id_covered_through_sequ",
                table: "conversation_summaries",
                columns: new[] { "conversation_id", "covered_through_sequence_number" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_id_last_message_at",
                table: "conversations",
                columns: new[] { "user_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_token_usage_records_user_id_created_at",
                table: "token_usage_records",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_requests_assistant_message_id",
                table: "tool_execution_requests",
                column: "assistant_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_requests_conversation_id",
                table: "tool_execution_requests",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_execution_requests_user_id_status",
                table: "tool_execution_requests",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_messages");

            migrationBuilder.DropTable(
                name: "conversation_summaries");

            migrationBuilder.DropTable(
                name: "token_usage_records");

            migrationBuilder.DropTable(
                name: "tool_execution_requests");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
