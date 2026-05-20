using System;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260520120001_AddContentVarietyRules")]
    public partial class AddContentVarietyRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_variety_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentCategoryCode = table.Column<string>(type: "text", nullable: false),
                    RuleType = table.Column<string>(type: "text", nullable: false),
                    RuleKey = table.Column<string>(type: "text", nullable: false),
                    CooldownDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxUsagePerWeek = table.Column<int>(type: "integer", nullable: true),
                    MaxUsagePerMonth = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_variety_rules", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO content_variety_rules ("Id", "ContentCategoryCode", "RuleType", "RuleKey", "CooldownDays", "MaxUsagePerWeek", "MaxUsagePerMonth", "Priority", "Enabled", "CreatedUtc", "UpdatedUtc") VALUES
                ('14000000-0000-0000-0000-000000000001', 'DailySkyGuide', 'CelestialObject', 'Moon', 2, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000002', 'DailySkyGuide', 'CelestialObject', 'Mars', 4, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000003', 'DailySkyGuide', 'CelestialObject', 'Jupiter', 4, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000004', 'DailySkyGuide', 'CelestialObject', 'Saturn', 5, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000005', 'DailySkyGuide', 'CelestialObject', 'Venus', 3, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000006', 'CosmicStoryShort', 'CelestialObject', 'BlackHole', 5, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000007', 'CosmicStoryShort', 'CelestialObject', 'Galaxy', 4, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000008', 'CosmicStoryShort', 'CelestialObject', 'Nebula', 4, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000009', 'MythologySkyStory', 'HookStyle', 'Mythological', 3, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000010', 'AstroPhotographyGuide', 'ThumbnailStyle', 'MoonDominant', 4, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                ('14000000-0000-0000-0000-000000000011', 'AstroPhotographyGuide', 'ThumbnailStyle', 'Minimal', 2, NULL, NULL, 100, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_content_variety_rules_ContentCategoryCode_RuleType_RuleKey",
                table: "content_variety_rules",
                columns: new[] { "ContentCategoryCode", "RuleType", "RuleKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "content_variety_rules");
        }
    }
}
