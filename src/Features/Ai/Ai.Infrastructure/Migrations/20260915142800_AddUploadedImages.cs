using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "uploaded_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uploaded_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_uploaded_images_ai_users_user_id",
                        column: x => x.user_id,
                        principalTable: "ai_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_uploaded_images_object_key",
                table: "uploaded_images",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uploaded_images_user_id_id",
                table: "uploaded_images",
                columns: new[] { "user_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uploaded_images");
        }
    }
}
