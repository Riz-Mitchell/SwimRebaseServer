using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedImageUsedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "used_at",
                table: "uploaded_images",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "used_at",
                table: "uploaded_images");
        }
    }
}
