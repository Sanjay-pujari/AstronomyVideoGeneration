using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260817010000_AddRc2FacebookPhotoCheckpoint")]
public partial class AddRc2FacebookPhotoCheckpoint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("RemotePostId", "rc2_publishing_publications",
            "character varying(256)", maxLength: 256, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("RemotePostId", "rc2_publishing_publications");
}
