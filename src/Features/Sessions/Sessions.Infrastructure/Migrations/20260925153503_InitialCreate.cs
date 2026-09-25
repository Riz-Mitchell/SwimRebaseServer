using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    course = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_distance = table.Column<int>(type: "integer", nullable: false),
                    content_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "session_facts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stroke = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    equip = table.Column<string[]>(type: "text[]", nullable: false),
                    intensity = table.Column<int>(type: "integer", nullable: true),
                    distance = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    course = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_facts", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_facts_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_session_facts_equip",
                table: "session_facts",
                column: "equip")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_session_facts_owner_id_intensity_performed_on",
                table: "session_facts",
                columns: new[] { "owner_id", "intensity", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_session_facts_owner_id_performed_on",
                table: "session_facts",
                columns: new[] { "owner_id", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_session_facts_owner_id_stroke_performed_on",
                table: "session_facts",
                columns: new[] { "owner_id", "stroke", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_session_facts_session_id",
                table: "session_facts",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_owner_id_performed_on",
                table: "sessions",
                columns: new[] { "owner_id", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_source_draft_id",
                table: "sessions",
                column: "source_draft_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_facts");

            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
