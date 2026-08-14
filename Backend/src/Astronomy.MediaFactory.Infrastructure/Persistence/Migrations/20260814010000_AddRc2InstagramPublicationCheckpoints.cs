using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260814010000_AddRc2InstagramPublicationCheckpoints")]
public partial class AddRc2InstagramPublicationCheckpoints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("ContainerReady", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("MediaPrepared", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("PublicMediaStaged", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("PublishRequested", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("PublicMediaBlobName", "rc2_publishing_publications", "character varying(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("PublicMediaExpiresUtc", "rc2_publishing_publications", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("RemoteContainerId", "rc2_publishing_publications", "character varying(256)", maxLength: 256, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ContainerReady", "rc2_publishing_publications");
        migrationBuilder.DropColumn("MediaPrepared", "rc2_publishing_publications");
        migrationBuilder.DropColumn("PublicMediaStaged", "rc2_publishing_publications");
        migrationBuilder.DropColumn("PublishRequested", "rc2_publishing_publications");
        migrationBuilder.DropColumn("PublicMediaBlobName", "rc2_publishing_publications");
        migrationBuilder.DropColumn("PublicMediaExpiresUtc", "rc2_publishing_publications");
        migrationBuilder.DropColumn("RemoteContainerId", "rc2_publishing_publications");
    }
}
