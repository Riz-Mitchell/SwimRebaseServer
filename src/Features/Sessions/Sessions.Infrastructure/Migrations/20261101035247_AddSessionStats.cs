using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_stats",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_stats", x => x.owner_id);
                });

            // Count the sessions people already have, so existing users start with the right number. From here on every save and delete
            // adjusts the count in the same transaction.
            migrationBuilder.Sql(
                """
                INSERT INTO session_stats (owner_id, session_count, updated_at)
                SELECT owner_id, COUNT(*)::integer, now()
                FROM sessions
                GROUP BY owner_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_stats");
        }
    }
}
