using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTimetable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expected_sessions");

            migrationBuilder.DropTable(
                name: "timetable_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "timetable_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    session_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timetable_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_timetable_entries_training_users_user_id",
                        column: x => x.user_id,
                        principalTable: "training_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expected_sessions",
                columns: table => new
                {
                    timetable_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    total_distance_meters = table.Column<int>(type: "integer", nullable: false),
                    primary_effort_zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expected_sessions", x => x.timetable_entry_id);
                    table.ForeignKey(
                        name: "fk_expected_sessions_timetable_entries_timetable_entry_id",
                        column: x => x.timetable_entry_id,
                        principalTable: "timetable_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_slot_id_effective_to",
                table: "timetable_entries",
                columns: new[] { "slot_id", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_timetable_entries_user_id_effective_from_effective_to",
                table: "timetable_entries",
                columns: new[] { "user_id", "effective_from", "effective_to" });
        }
    }
}
