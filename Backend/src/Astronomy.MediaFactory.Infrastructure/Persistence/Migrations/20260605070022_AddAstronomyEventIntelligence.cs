using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAstronomyEventIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "astronomy_event_intelligences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeakUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RegionId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LocationName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RecommendedCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SourcePipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    RarityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    VisibilityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    AudienceInterestScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TimingUrgencyScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ContentOpportunityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: true),
                    RulesAppliedJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_event_intelligences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_content_opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AstronomyEventIntelligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Angle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AudienceSegment = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PriorityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    VisualStrategyJson = table.Column<string>(type: "jsonb", nullable: true),
                    NarrationStrategyJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_content_opportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_content_opportunities_astronomy_event_intelligenc~",
                        column: x => x.AstronomyEventIntelligenceId,
                        principalTable: "astronomy_event_intelligences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_event_objects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AstronomyEventIntelligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ObjectRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CatalogId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Magnitude = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    VisibilityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_event_objects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_event_objects_astronomy_event_intelligences_Astro~",
                        column: x => x.AstronomyEventIntelligenceId,
                        principalTable: "astronomy_event_intelligences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_event_validations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AstronomyEventIntelligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ValidatorName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_event_validations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_event_validations_astronomy_event_intelligences_A~",
                        column: x => x.AstronomyEventIntelligenceId,
                        principalTable: "astronomy_event_intelligences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_reference_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AstronomyEventIntelligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Citation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PublishedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetrievedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_reference_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_reference_sources_astronomy_event_intelligences_A~",
                        column: x => x.AstronomyEventIntelligenceId,
                        principalTable: "astronomy_event_intelligences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_content_opportunities_AstronomyEventIntelligenceId",
                table: "astronomy_content_opportunities",
                column: "AstronomyEventIntelligenceId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_content_opportunities_ContentCategory",
                table: "astronomy_content_opportunities",
                column: "ContentCategory");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_content_opportunities_PriorityScore",
                table: "astronomy_content_opportunities",
                column: "PriorityScore");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_content_opportunities_Status",
                table: "astronomy_content_opportunities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_EventCode",
                table: "astronomy_event_intelligences",
                column: "EventCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_EventType",
                table: "astronomy_event_intelligences",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_PeakUtc",
                table: "astronomy_event_intelligences",
                column: "PeakUtc");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_RecommendedCategory",
                table: "astronomy_event_intelligences",
                column: "RecommendedCategory");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_RegionId",
                table: "astronomy_event_intelligences",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_StartUtc",
                table: "astronomy_event_intelligences",
                column: "StartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_Status",
                table: "astronomy_event_intelligences",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_objects_AstronomyEventIntelligenceId",
                table: "astronomy_event_objects",
                column: "AstronomyEventIntelligenceId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_objects_ObjectName",
                table: "astronomy_event_objects",
                column: "ObjectName");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_objects_ObjectType",
                table: "astronomy_event_objects",
                column: "ObjectType");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_validations_AstronomyEventIntelligenceId",
                table: "astronomy_event_validations",
                column: "AstronomyEventIntelligenceId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_reference_sources_AstronomyEventIntelligenceId",
                table: "astronomy_reference_sources",
                column: "AstronomyEventIntelligenceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "astronomy_content_opportunities");

            migrationBuilder.DropTable(
                name: "astronomy_event_objects");

            migrationBuilder.DropTable(
                name: "astronomy_event_validations");

            migrationBuilder.DropTable(
                name: "astronomy_reference_sources");

            migrationBuilder.DropTable(
                name: "astronomy_event_intelligences");
        }
    }
}
