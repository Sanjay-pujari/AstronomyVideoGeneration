using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEntities_20260605_4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "astronomy_question_answer_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AstronomyEventIntelligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    GeneratedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_question_answer_sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_question_answer_sets_astronomy_event_intelligence~",
                        column: x => x.AstronomyEventIntelligenceId,
                        principalTable: "astronomy_event_intelligences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_question_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    QuestionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TemplateText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_question_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_question_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionAnswerSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    AnswerText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_question_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_astronomy_question_answers_astronomy_question_answer_sets_Q~",
                        column: x => x.QuestionAnswerSetId,
                        principalTable: "astronomy_question_answer_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answer_sets_AstronomyEventIntelligenceId",
                table: "astronomy_question_answer_sets",
                column: "AstronomyEventIntelligenceId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answer_sets_Language",
                table: "astronomy_question_answer_sets",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answer_sets_RegionId",
                table: "astronomy_question_answer_sets",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answer_sets_Status",
                table: "astronomy_question_answer_sets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answers_DisplayOrder",
                table: "astronomy_question_answers",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answers_QuestionAnswerSetId",
                table: "astronomy_question_answers",
                column: "QuestionAnswerSetId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_answers_QuestionType",
                table: "astronomy_question_answers",
                column: "QuestionType");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_templates_EventType",
                table: "astronomy_question_templates",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_templates_IsActive",
                table: "astronomy_question_templates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_templates_Language",
                table: "astronomy_question_templates",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_question_templates_QuestionType",
                table: "astronomy_question_templates",
                column: "QuestionType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "astronomy_question_answers");

            migrationBuilder.DropTable(
                name: "astronomy_question_templates");

            migrationBuilder.DropTable(
                name: "astronomy_question_answer_sets");
        }
    }
}
