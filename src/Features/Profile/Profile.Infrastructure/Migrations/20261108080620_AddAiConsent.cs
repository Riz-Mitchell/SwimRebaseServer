using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ai_consent_at",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_consent_version",
                table: "user_profiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Pre-production: everyone, mid-onboarding or finished, goes through the consent step first.
            migrationBuilder.Sql(
                "UPDATE user_profiles SET current_step = 'AiConsent', first_session_path = NULL, " +
                "onboarding_completed_at = NULL, pro_trial_ends_at = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_consent_at",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "ai_consent_version",
                table: "user_profiles");
        }
    }
}
