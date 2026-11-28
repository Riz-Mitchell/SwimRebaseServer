using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DefaultTimedSwimStartAndFinish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Swims logged without a start or finish take the same defaults new ones now get. Must run before the columns stop allowing null.
            // The values are the enum names as stored. Down cannot tell these rows from ones logged as Dive or Hand, so it leaves them.
            migrationBuilder.Sql("UPDATE timed_swims SET start_type = 'Dive' WHERE start_type IS NULL;");
            migrationBuilder.Sql("UPDATE timed_swims SET finish = 'Hand' WHERE finish IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "start_type",
                table: "timed_swims",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "finish",
                table: "timed_swims",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "start_type",
                table: "timed_swims",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "finish",
                table: "timed_swims",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);
        }
    }
}
