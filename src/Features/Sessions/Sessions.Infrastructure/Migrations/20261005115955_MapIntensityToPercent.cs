using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MapIntensityToPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_effort_scale_json",
                table: "session_preferences",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort_scale",
                table: "session_preferences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "tens");

            // Intensity moves from a 1-10 level to a percent, so every stored level becomes ten times itself. Session content and facts
            // are re-derived from the effort text afterwards by the startup backfill, which also corrects words the old table misread.
            migrationBuilder.Sql("UPDATE session_facts SET intensity = intensity * 10 WHERE intensity IS NOT NULL;");
            migrationBuilder.Sql("UPDATE timed_swims SET intensity = intensity * 10 WHERE intensity IS NOT NULL;");
            migrationBuilder.Sql("UPDATE session_preferences SET default_effort = default_effort * 10;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE session_facts SET intensity = GREATEST(1, LEAST(10, ROUND(intensity / 10.0))) WHERE intensity IS NOT NULL;");
            migrationBuilder.Sql("UPDATE timed_swims SET intensity = GREATEST(1, LEAST(10, ROUND(intensity / 10.0))) WHERE intensity IS NOT NULL;");
            migrationBuilder.Sql("UPDATE session_preferences SET default_effort = GREATEST(1, LEAST(10, ROUND(default_effort / 10.0)));");

            migrationBuilder.DropColumn(
                name: "custom_effort_scale_json",
                table: "session_preferences");

            migrationBuilder.DropColumn(
                name: "effort_scale",
                table: "session_preferences");
        }
    }
}
