using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260508000000_Phase4APipelineState")]
    public partial class Phase4APipelineState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE pipeline_runs
                    ADD COLUMN IF NOT EXISTS "outputFolder" text;

                ALTER TABLE pipeline_runs
                    ADD COLUMN IF NOT EXISTS "resumeSupported" boolean NOT NULL DEFAULT false;

                ALTER TABLE pipeline_stage_executions
                    ADD COLUMN IF NOT EXISTS "AttemptCount" integer NOT NULL DEFAULT 0;

                ALTER TABLE pipeline_stage_executions
                    ADD COLUMN IF NOT EXISTS "MaxAttempts" integer NOT NULL DEFAULT 1;

                ALTER TABLE pipeline_stage_executions
                    ADD COLUMN IF NOT EXISTS "OutputPath" text;

                ALTER TABLE pipeline_stage_executions
                    ADD COLUMN IF NOT EXISTS "DiagnosticPath" text;

                CREATE INDEX IF NOT EXISTS "IX_pipeline_stage_executions_PipelineRunId_StageName_CreatedUtc"
                    ON pipeline_stage_executions ("PipelineRunId", "StageName", "CreatedUtc");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_pipeline_stage_executions_PipelineRunId_StageName_CreatedUtc";

                ALTER TABLE pipeline_runs
                    DROP COLUMN IF EXISTS "outputFolder",
                    DROP COLUMN IF EXISTS "resumeSupported";

                ALTER TABLE pipeline_stage_executions
                    DROP COLUMN IF EXISTS "AttemptCount",
                    DROP COLUMN IF EXISTS "MaxAttempts",
                    DROP COLUMN IF EXISTS "OutputPath",
                    DROP COLUMN IF EXISTS "DiagnosticPath";
                """);
        }
    }
}
