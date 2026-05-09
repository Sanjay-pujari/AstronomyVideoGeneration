using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnsurePlatformPublicationRecordsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive: some environments were created via db/init scripts and can miss this index.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    -- EF Core migrations create quoted identifiers ("Platform", "ExternalPostId").
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'platform_publication_records'
                          AND column_name = 'Platform'
                    ) THEN
                        EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ix_platform_publication_records_platform_external_post_id
                                 ON platform_publication_records("Platform", "ExternalPostId");';
                    ELSIF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'platform_publication_records'
                          AND column_name = 'platform'
                    ) THEN
                        -- init.sql style (snake_case).
                        EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ix_platform_publication_records_platform_external_post_id
                                 ON platform_publication_records(platform, external_post_id);';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_platform_publication_records_platform_external_post_id;
                """);
        }
    }
}
