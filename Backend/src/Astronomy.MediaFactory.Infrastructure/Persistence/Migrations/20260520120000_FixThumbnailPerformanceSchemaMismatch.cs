using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260520120000_FixThumbnailPerformanceSchemaMismatch")]
    public partial class FixThumbnailPerformanceSchemaMismatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE IF EXISTS public.thumbnail_performance ADD COLUMN IF NOT EXISTS "ThumbnailPath" text NOT NULL DEFAULT '';
                ALTER TABLE IF EXISTS public.thumbnail_performance ADD COLUMN IF NOT EXISTS "ThumbnailType" text NOT NULL DEFAULT '';
                ALTER TABLE IF EXISTS public.thumbnail_performance ADD COLUMN IF NOT EXISTS "ContentType" text NOT NULL DEFAULT '';
                ALTER TABLE IF EXISTS public.thumbnail_performance ADD COLUMN IF NOT EXISTS "PublishedAtUtc" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '1970-01-01 00:00:00+00';
                ALTER TABLE IF EXISTS public.thumbnail_performance ADD COLUMN IF NOT EXISTS "UpdatedUtc" timestamp with time zone NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left empty to avoid destructive schema rollbacks in analytics tables.
        }
    }
}
