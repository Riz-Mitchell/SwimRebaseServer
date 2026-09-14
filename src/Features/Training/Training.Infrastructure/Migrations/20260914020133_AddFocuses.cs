using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFocuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "focuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    original_user_request = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    total_days = table.Column<int>(type: "integer", nullable: false),
                    days_completed = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_at = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_focuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_focuses_training_users_user_id",
                        column: x => x.user_id,
                        principalTable: "training_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comparison_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    focus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    polarity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    tag_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparison_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_comparison_points_focuses_focus_id",
                        column: x => x.focus_id,
                        principalTable: "focuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "focus_check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    focus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_focus_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "fk_focus_check_ins_focuses_focus_id",
                        column: x => x.focus_id,
                        principalTable: "focuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comparison_points_focus_id_polarity_order",
                table: "comparison_points",
                columns: new[] { "focus_id", "polarity", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_focus_check_ins_focus_id_date",
                table: "focus_check_ins",
                columns: new[] { "focus_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_focuses_user_id_priority",
                table: "focuses",
                columns: new[] { "user_id", "priority" },
                unique: true,
                filter: "status = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comparison_points");

            migrationBuilder.DropTable(
                name: "focus_check_ins");

            migrationBuilder.DropTable(
                name: "focuses");
        }
    }
}
