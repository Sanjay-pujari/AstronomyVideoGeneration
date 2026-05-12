using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260512153000_RecreateAstronomyEvents")]
    public partial class RecreateAstronomyEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "decisionType" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "injectedIntoDailyGuide" boolean NOT NULL DEFAULT false;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "specialEventGuideGenerated" boolean NOT NULL DEFAULT false;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "decisionType" text;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "injectedIntoDailyGuide" boolean NOT NULL DEFAULT false;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "specialEventGuideGenerated" boolean NOT NULL DEFAULT false;
                ALTER TABLE platform_content_analytics ADD COLUMN IF NOT EXISTS "eventId" text;
                ALTER TABLE platform_content_analytics ADD COLUMN IF NOT EXISTS "eventType" text;
                ALTER TABLE platform_content_analytics ADD COLUMN IF NOT EXISTS "eventTitle" text;
                ALTER TABLE platform_content_analytics ADD COLUMN IF NOT EXISTS "decisionType" text;
                ALTER TABLE platform_content_analytics ADD COLUMN IF NOT EXISTS "injectedIntoDailyGuide" boolean;
                ALTER TABLE platform_content_analytics ADD COLUMN IF NOT EXISTS "specialEventGuideGenerated" boolean;

                CREATE TABLE IF NOT EXISTS astronomy_events (
                    "Id" uuid NOT NULL,
                    "eventId" text NOT NULL,
                    "eventType" text NOT NULL,
                    "title" text NOT NULL,
                    "description" text NOT NULL,
                    "startUtc" timestamp with time zone NOT NULL,
                    "peakUtc" timestamp with time zone NULL,
                    "endUtc" timestamp with time zone NOT NULL,
                    "targetDate" date NOT NULL,
                    "regionId" text NULL,
                    "locationName" text NULL,
                    "Latitude" double precision NULL,
                    "Longitude" double precision NULL,
                    "timezone" text NULL,
                    "globalVisibility" boolean NOT NULL,
                    "visibilityRegions" text NOT NULL DEFAULT '[]',
                    "relatedObjects" text NOT NULL DEFAULT '[]',
                    "source" text NOT NULL,
                    "confidenceScore" double precision NOT NULL,
                    "rarityScore" double precision NOT NULL,
                    "visibilityScore" double precision NOT NULL,
                    "audienceInterestScore" double precision NOT NULL,
                    "timingUrgencyScore" double precision NOT NULL,
                    "contentOpportunityScore" double precision NOT NULL,
                    "recommendedContentType" text NOT NULL,
                    "status" text NOT NULL,
                    "createdUtc" timestamp with time zone NOT NULL,
                    "updatedUtc" timestamp with time zone NULL,
                    CONSTRAINT "PK_astronomy_events" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_astronomy_events_eventId" ON astronomy_events ("eventId");
                CREATE INDEX IF NOT EXISTS "IX_astronomy_events_targetDate" ON astronomy_events ("targetDate");
                CREATE INDEX IF NOT EXISTS "IX_astronomy_events_regionId" ON astronomy_events ("regionId");
                CREATE INDEX IF NOT EXISTS "IX_astronomy_events_eventType" ON astronomy_events ("eventType");
                CREATE INDEX IF NOT EXISTS "IX_astronomy_events_contentOpportunityScore" ON astronomy_events ("contentOpportunityScore");

                CREATE TABLE IF NOT EXISTS astronomy_event_generation_history (
                    "Id" uuid NOT NULL,
                    "astronomyEventId" uuid NOT NULL,
                    "pipelineRunId" uuid NOT NULL,
                    "regionId" text NOT NULL,
                    "targetDate" date NOT NULL,
                    "contentType" text NOT NULL,
                    "generationMode" text NOT NULL,
                    "createdUtc" timestamp with time zone NOT NULL,
                    "updatedUtc" timestamp with time zone NULL,
                    CONSTRAINT "PK_astronomy_event_generation_history" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_astronomy_event_generation_history_unique"
                    ON astronomy_event_generation_history ("astronomyEventId", "regionId", "targetDate", "contentType");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS astronomy_event_generation_history;
                DROP TABLE IF EXISTS astronomy_events;
                """);
        }
    }
}
