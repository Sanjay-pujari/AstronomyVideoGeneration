using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260812000000_AddRc2PublishingApprovals")]
public partial class AddRc2PublishingApprovals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rc2_publishing_approvals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                PublishingPackageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Phase20AuthorityChecksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Decision = table.Column<int>(type: "integer", nullable: false),
                DecisionUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DecisionSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_rc2_publishing_approvals", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_rc2_publishing_approvals_PlanId_Phase20AuthorityChecksum_PublishingPackageId",
            table: "rc2_publishing_approvals",
            columns: new[] { "PlanId", "Phase20AuthorityChecksum", "PublishingPackageId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("rc2_publishing_approvals");
}
