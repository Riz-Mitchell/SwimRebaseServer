using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultipleAnswersPerQuestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_profile_answers",
                table: "profile_answers");

            migrationBuilder.AddPrimaryKey(
                name: "pk_profile_answers",
                table: "profile_answers",
                columns: new[] { "user_id", "question_key", "answer_value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_profile_answers",
                table: "profile_answers");

            migrationBuilder.AddPrimaryKey(
                name: "pk_profile_answers",
                table: "profile_answers",
                columns: new[] { "user_id", "question_key" });
        }
    }
}
