using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveScheduleStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE user_profiles SET current_step = 'ProTrial' WHERE current_step = 'Schedule'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
