using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260512130100_EnsurePublishedVideosEventMetadata")]
    public partial class EnsurePublishedVideosEventMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE published_videos ADD COLUMN IF NOT EXISTS "EventId" text;
                ALTER TABLE published_videos ADD COLUMN IF NOT EXISTS "EventType" text;
                ALTER TABLE published_videos ADD COLUMN IF NOT EXISTS "EventTitle" text;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE published_videos DROP COLUMN IF EXISTS "EventTitle";
                ALTER TABLE published_videos DROP COLUMN IF EXISTS "EventType";
                ALTER TABLE published_videos DROP COLUMN IF EXISTS "EventId";
                """);
        }
    }
}
