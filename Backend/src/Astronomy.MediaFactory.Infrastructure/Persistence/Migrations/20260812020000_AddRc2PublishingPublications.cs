using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260812020000_AddRc2PublishingPublications")]
public partial class AddRc2PublishingPublications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rc2_publishing_publications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                PublishingPackageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Phase20AuthorityChecksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Target = table.Column<int>(type: "integer", nullable: false),
                RoleOrMediaType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LastAttemptUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RemotePublicationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                RemoteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                FailureMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rc2_publishing_publications", x => x.Id);
                table.ForeignKey("FK_rc2_publishing_publications_content_generation_plans_PlanId", x => x.PlanId,
                    "content_generation_plans", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_rc2_publishing_publications_IdempotencyKey", "rc2_publishing_publications",
            "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex("IX_rc2_publishing_publications_PlanId_Target", "rc2_publishing_publications",
            new[] { "PlanId", "Target" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "rc2_publishing_publications");
}
