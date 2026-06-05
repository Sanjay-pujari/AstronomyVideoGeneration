using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEntities_20260605_3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "astronomy_asset_production_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentGenerationPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    AstronomyContentOpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AstronomyEventIntelligenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SceneNumber = table.Column<int>(type: "integer", nullable: false),
                    SceneName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AssetPurpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PlannedProvider = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ObjectNamesJson = table.Column<string>(type: "jsonb", nullable: true),
                    PromptOrInstruction = table.Column<string>(type: "text", nullable: true),
                    ExpectedOutputType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AssetPriority = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssetExecutionGroup = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OutputPath = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    StartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_asset_production_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_asset_production_jobs_astronomy_content_opportuni~",
                        column: x => x.AstronomyContentOpportunityId,
                        principalTable: "astronomy_content_opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_astronomy_asset_production_jobs_astronomy_event_intelligenc~",
                        column: x => x.AstronomyEventIntelligenceId,
                        principalTable: "astronomy_event_intelligences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_astronomy_asset_production_jobs_content_generation_plans_Co~",
                        column: x => x.ContentGenerationPlanId,
                        principalTable: "content_generation_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_AssetPriority",
                table: "astronomy_asset_production_jobs",
                column: "AssetPriority");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_AssetType",
                table: "astronomy_asset_production_jobs",
                column: "AssetType");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_AstronomyContentOpportunity~",
                table: "astronomy_asset_production_jobs",
                column: "AstronomyContentOpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_AstronomyEventIntelligenceId",
                table: "astronomy_asset_production_jobs",
                column: "AstronomyEventIntelligenceId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_ContentGenerationPlanId",
                table: "astronomy_asset_production_jobs",
                column: "ContentGenerationPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_PlannedProvider",
                table: "astronomy_asset_production_jobs",
                column: "PlannedProvider");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_asset_production_jobs_Status",
                table: "astronomy_asset_production_jobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "astronomy_asset_production_jobs");
        }
    }
}
