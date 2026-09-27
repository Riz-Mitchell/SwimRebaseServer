using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimedSwims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "timed_swims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    course = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    dist = table.Column<int>(type: "integer", nullable: false),
                    stroke = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    seconds = table.Column<double>(type: "double precision", nullable: false),
                    effort = table.Column<string>(type: "text", nullable: true),
                    intensity = table.Column<int>(type: "integer", nullable: true),
                    start_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    finish = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    resistance_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resistance_load = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    splits_json = table.Column<string>(type: "text", nullable: false),
                    stroke_rate_cycles_per_minute = table.Column<double>(type: "double precision", nullable: true),
                    include_in_metrics = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timed_swims", x => x.id);
                    table.ForeignKey(
                        name: "fk_timed_swims_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_timed_swims_owner_id_dist_stroke_performed_on",
                table: "timed_swims",
                columns: new[] { "owner_id", "dist", "stroke", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_timed_swims_owner_id_performed_on",
                table: "timed_swims",
                columns: new[] { "owner_id", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_timed_swims_session_id",
                table: "timed_swims",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "timed_swims");
        }
    }
}
