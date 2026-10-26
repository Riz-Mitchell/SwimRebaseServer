using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDescendTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "descend_target",
                table: "session_preferences",
                type: "integer",
                nullable: false,
                defaultValue: 85);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "descend_target",
                table: "session_preferences");
        }
    }
}
