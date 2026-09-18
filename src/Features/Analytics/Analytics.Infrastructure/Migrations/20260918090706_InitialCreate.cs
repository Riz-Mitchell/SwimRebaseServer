using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_logs_event_type_occurred_at",
                table: "event_logs",
                columns: new[] { "event_type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_logs_occurred_at",
                table: "event_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_event_logs_user_id_occurred_at",
                table: "event_logs",
                columns: new[] { "user_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_logs");
        }
    }
}
