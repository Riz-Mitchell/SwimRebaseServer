using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_step = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_session_path = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reminders_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reminder_lead_minutes = table.Column<int>(type: "integer", nullable: false),
                    onboarding_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pro_trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.id);
                });

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
                    table.PrimaryKey("pk_profile_answers", x => new { x.user_id, x.question_key });
                    table.ForeignKey(
                        name: "fk_profile_answers_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Users who existed before onboarding shipped skip it.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('users') IS NOT NULL THEN
                        INSERT INTO user_profiles (id, current_step, reminders_enabled, reminder_lead_minutes, onboarding_completed_at, created_at, updated_at)
                        SELECT id, 'Completed', false, 60, now(), now(), now() FROM users
                        ON CONFLICT (id) DO NOTHING;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_answers");

            migrationBuilder.DropTable(
                name: "user_profiles");
        }
    }
}
