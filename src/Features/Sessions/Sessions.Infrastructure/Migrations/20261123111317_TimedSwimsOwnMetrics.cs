using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimedSwimsOwnMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "timed_swim_resistances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timed_swim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: true),
                    width_cm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    length_cm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    tension_level = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timed_swim_resistances", x => x.id);
                    table.ForeignKey(
                        name: "fk_timed_swim_resistances_timed_swims_timed_swim_id",
                        column: x => x.timed_swim_id,
                        principalTable: "timed_swims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_timed_swim_resistances_kind",
                table: "timed_swim_resistances",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_timed_swim_resistances_timed_swim_id",
                table: "timed_swim_resistances",
                column: "timed_swim_id");

            // Free-text resistance becomes one "Other" row carrying the old wording, so nothing logged before this is lost.
            migrationBuilder.Sql(
                """
                INSERT INTO timed_swim_resistances (id, timed_swim_id, kind, note)
                SELECT gen_random_uuid(), id, 'Other',
                       CASE WHEN resistance_load IS NULL OR resistance_load = '' THEN resistance_type ELSE resistance_type || ' - ' || resistance_load END
                FROM timed_swims
                WHERE resistance_type IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "include_in_metrics",
                table: "timed_swims");

            migrationBuilder.DropColumn(
                name: "resistance_load",
                table: "timed_swims");

            migrationBuilder.DropColumn(
                name: "resistance_type",
                table: "timed_swims");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "include_in_metrics",
                table: "timed_swims",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "resistance_load",
                table: "timed_swims",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resistance_type",
                table: "timed_swims",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Best effort: only the first piece of gear survives, as text, since the old shape held one type and one load.
            migrationBuilder.Sql(
                """
                UPDATE timed_swims t
                SET resistance_type = LEFT(COALESCE(r.note, r.kind), 64)
                FROM (SELECT DISTINCT ON (timed_swim_id) timed_swim_id, kind, note FROM timed_swim_resistances ORDER BY timed_swim_id, id) r
                WHERE r.timed_swim_id = t.id;
                """);

            migrationBuilder.DropTable(
                name: "timed_swim_resistances");
        }
    }
}
