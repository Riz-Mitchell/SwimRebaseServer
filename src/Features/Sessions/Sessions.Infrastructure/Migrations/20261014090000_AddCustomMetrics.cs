using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_metrics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    strokes = table.Column<string[]>(type: "text[]", nullable: false),
                    modes = table.Column<string[]>(type: "text[]", nullable: false),
                    min_percent = table.Column<int>(type: "integer", nullable: true),
                    max_percent = table.Column<int>(type: "integer", nullable: true),
                    measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    window = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    group_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_metrics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_metrics_owner_id_position",
                table: "custom_metrics",
                columns: new[] { "owner_id", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_metrics");
        }
    }
}
