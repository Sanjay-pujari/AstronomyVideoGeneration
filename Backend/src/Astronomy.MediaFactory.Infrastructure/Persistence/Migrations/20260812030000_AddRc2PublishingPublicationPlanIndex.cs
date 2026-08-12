using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260812030000_AddRc2PublishingPublicationPlanIndex")]
public partial class AddRc2PublishingPublicationPlanIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_rc2_publishing_publications_PlanId",
            table: "rc2_publishing_publications",
            column: "PlanId");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(
            name: "IX_rc2_publishing_publications_PlanId",
            table: "rc2_publishing_publications");
}
