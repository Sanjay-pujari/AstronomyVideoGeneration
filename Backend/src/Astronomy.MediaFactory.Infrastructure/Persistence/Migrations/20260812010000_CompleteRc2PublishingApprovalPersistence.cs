using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260812010000_CompleteRc2PublishingApprovalPersistence")]
public partial class CompleteRc2PublishingApprovalPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CreatedUtc",
            table: "rc2_publishing_approvals",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedUtc",
            table: "rc2_publishing_approvals",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE rc2_publishing_approvals SET \"CreatedUtc\" = \"DecisionUtc\", \"UpdatedUtc\" = \"DecisionUtc\"");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "CreatedUtc",
            table: "rc2_publishing_approvals",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "UpdatedUtc",
            table: "rc2_publishing_approvals",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_rc2_publishing_approvals_PlanId",
            table: "rc2_publishing_approvals",
            column: "PlanId");

        migrationBuilder.AddForeignKey(
            name: "FK_rc2_publishing_approvals_content_generation_plans_PlanId",
            table: "rc2_publishing_approvals",
            column: "PlanId",
            principalTable: "content_generation_plans",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_rc2_publishing_approvals_content_generation_plans_PlanId",
            table: "rc2_publishing_approvals");
        migrationBuilder.DropIndex(
            name: "IX_rc2_publishing_approvals_PlanId",
            table: "rc2_publishing_approvals");
        migrationBuilder.DropColumn(name: "CreatedUtc", table: "rc2_publishing_approvals");
        migrationBuilder.DropColumn(name: "UpdatedUtc", table: "rc2_publishing_approvals");
    }
}
