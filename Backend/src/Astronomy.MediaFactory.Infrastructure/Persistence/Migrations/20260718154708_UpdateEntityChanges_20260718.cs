using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEntityChanges_20260718 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificationDashboardPath",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationDecision",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationReportPath",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationSummaryPath",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationDecision",
                table: "pipeline_runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificationDashboardPath",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "CertificationDecision",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "CertificationReportPath",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "CertificationSummaryPath",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "PublicationDecision",
                table: "pipeline_runs");
        }
    }
}
