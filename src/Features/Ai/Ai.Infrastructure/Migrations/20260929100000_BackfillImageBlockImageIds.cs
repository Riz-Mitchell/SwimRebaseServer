using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ai.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillImageBlockImageIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE conversation_messages m
                SET raw_content_json = (
                    SELECT jsonb_agg(
                        CASE WHEN u.id IS NOT NULL
                            THEN t.block || jsonb_build_object('ImageId', u.id)
                            ELSE t.block END
                        ORDER BY t.ord)
                    FROM jsonb_array_elements(m.raw_content_json::jsonb) WITH ORDINALITY AS t(block, ord)
                    LEFT JOIN uploaded_images u
                        ON t.block ->> '$type' = 'image' AND u.object_key = t.block ->> 'ObjectKey'
                )::text
                WHERE m.raw_content_json LIKE '%"$type":"image"%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
