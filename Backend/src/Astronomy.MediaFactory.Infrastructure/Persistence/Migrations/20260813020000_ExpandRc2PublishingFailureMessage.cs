using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260813020000_ExpandRc2PublishingFailureMessage")]
public partial class ExpandRc2PublishingFailureMessage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "FailureMessage",
            table: "rc2_publishing_publications",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(1024)",
            oldMaxLength: 1024,
            oldNullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "FailureMessage",
            table: "rc2_publishing_publications",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
}
