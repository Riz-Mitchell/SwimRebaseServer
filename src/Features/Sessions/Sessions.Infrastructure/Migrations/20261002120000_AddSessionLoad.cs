using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionLoad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "load",
                table: "sessions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "unrated_distance",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "session_preferences",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_effort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_preferences", x => x.owner_id);
                });

            // Existing sessions get their load from the facts they already have (and counted timed swims), at the built-in default effort
            // of level 5 - nobody can have chosen another yet. (effort / 100)^2 with effort = level x 10 is (level / 10)^2.
            migrationBuilder.Sql(
                """
                UPDATE sessions AS s
                SET load = t.load, unrated_distance = t.unrated
                FROM (
                    SELECT session_id,
                           SUM(distance * POWER(COALESCE(intensity, 5) / 10.0, 2)) / 1000.0 AS load,
                           SUM(CASE WHEN intensity IS NULL THEN distance ELSE 0 END) AS unrated
                    FROM (
                        SELECT session_id, distance, intensity FROM session_facts
                        UNION ALL
                        SELECT session_id, dist, intensity FROM timed_swims WHERE include_in_metrics AND session_id IS NOT NULL
                    ) AS parts
                    GROUP BY session_id
                ) AS t
                WHERE s.id = t.session_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_preferences");

            migrationBuilder.DropColumn(
                name: "load",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "unrated_distance",
                table: "sessions");
        }
    }
}
