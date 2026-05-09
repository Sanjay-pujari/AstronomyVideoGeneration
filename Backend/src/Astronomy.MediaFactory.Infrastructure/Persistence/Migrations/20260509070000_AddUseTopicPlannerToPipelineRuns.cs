using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509070000_AddUseTopicPlannerToPipelineRuns")]
    public partial class AddUseTopicPlannerToPipelineRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Support both EF-style quoted identifiers and init.sql snake_case setups.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'pipeline_runs'
                          AND column_name = 'UseTopicPlanner'
                    ) THEN
                        -- already present
                        NULL;
                    ELSIF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'pipeline_runs'
                          AND column_name = 'run_date'
                    ) THEN
                        -- init.sql style
                        EXECUTE 'ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS use_topic_planner boolean NOT NULL DEFAULT false;';
                    ELSE
                        -- EF Core style
                        EXECUTE 'ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "UseTopicPlanner" boolean NOT NULL DEFAULT false;';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "UseTopicPlanner";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS use_topic_planner;
                """);
        }
    }
}
