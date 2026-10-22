using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Infrastructure.Migrations
{
    // The Training feature (focuses) no longer has a DbContext of its own, so this migration drops its tables and leftovers.
    [DbContext(typeof(AnalyticsDbContext))]
    [Migration("20261022000000_RemoveFocusData")]
    public partial class RemoveFocusData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS focus_check_ins, comparison_points, focuses, training_users CASCADE;
                DELETE FROM event_logs WHERE event_type IN ('focus_created', 'focus_edited', 'focus_deleted');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
