using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEffortTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_effort_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phrase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    percent = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_effort_terms", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_effort_terms_owner_id_phrase",
                table: "user_effort_terms",
                columns: new[] { "owner_id", "phrase" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_effort_terms");
        }
    }
}
