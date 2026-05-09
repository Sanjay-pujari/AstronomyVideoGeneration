using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Model no longer maps AstronomyEvent to Postgres; table was unused by DbContext.
            // Other schema updates from this sync were already applied by ordered migrations
            // (AddSpecialEventGuideMetadata, AddRegionSchedulingFields, AddLocalizationFields, etc.).
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS astronomy_events;

                ALTER TABLE pipeline_jobs ADD COLUMN IF NOT EXISTS "Language" text NOT NULL DEFAULT 'en';
                ALTER TABLE pipeline_jobs ADD COLUMN IF NOT EXISTS "RegionId" text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE pipeline_jobs DROP COLUMN IF EXISTS "RegionId";
                ALTER TABLE pipeline_jobs DROP COLUMN IF EXISTS "Language";
                """);

            migrationBuilder.CreateTable(
                name: "astronomy_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ObjectName = table.Column<string>(type: "text", nullable: false),
                    ObservationTool = table.Column<string>(type: "text", nullable: false),
                    RankScore = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VisibilityWindow = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_events", x => x.Id);
                });
        }
    }
}
