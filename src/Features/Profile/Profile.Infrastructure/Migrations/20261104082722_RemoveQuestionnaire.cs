using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveQuestionnaire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_answers");

            // Pre-production: restart every user's onboarding at the first remaining step.
            migrationBuilder.Sql(
                "UPDATE user_profiles SET current_step = 'FirstSession', first_session_path = NULL, " +
                "onboarding_completed_at = NULL, pro_trial_ends_at = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profile_answers",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    answer_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_answers", x => new { x.user_id, x.question_key, x.answer_value });
                    table.ForeignKey(
                        name: "fk_profile_answers_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
