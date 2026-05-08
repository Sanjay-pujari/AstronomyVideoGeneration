using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    public partial class Phase4APipelineState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outputFolder",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "resumeSupported",
                table: "pipeline_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "pipeline_stage_executions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "pipeline_stage_executions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "OutputPath",
                table: "pipeline_stage_executions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosticPath",
                table: "pipeline_stage_executions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stage_executions_PipelineRunId_StageName_CreatedUtc",
                table: "pipeline_stage_executions",
                columns: new[] { "PipelineRunId", "StageName", "CreatedUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pipeline_stage_executions_PipelineRunId_StageName_CreatedUtc",
                table: "pipeline_stage_executions");

            migrationBuilder.DropColumn(name: "outputFolder", table: "pipeline_runs");
            migrationBuilder.DropColumn(name: "resumeSupported", table: "pipeline_runs");
            migrationBuilder.DropColumn(name: "AttemptCount", table: "pipeline_stage_executions");
            migrationBuilder.DropColumn(name: "MaxAttempts", table: "pipeline_stage_executions");
            migrationBuilder.DropColumn(name: "OutputPath", table: "pipeline_stage_executions");
            migrationBuilder.DropColumn(name: "DiagnosticPath", table: "pipeline_stage_executions");
        }
    }
}
