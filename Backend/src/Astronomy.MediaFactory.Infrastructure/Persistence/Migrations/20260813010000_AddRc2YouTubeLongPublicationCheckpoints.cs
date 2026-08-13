using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaFactoryDbContext))]
[Migration("20260813010000_AddRc2YouTubeLongPublicationCheckpoints")]
public partial class AddRc2YouTubeLongPublicationCheckpoints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("CaptionCompleted", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("LastCompletedStep", "rc2_publishing_publications", "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>("RemoteVerificationCompleted", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("ThumbnailCompleted", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset>("VideoCreatedUtc", "rc2_publishing_publications", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<bool>("VideoUploadCompleted", "rc2_publishing_publications", "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("CaptionCompleted", "rc2_publishing_publications");
        migrationBuilder.DropColumn("LastCompletedStep", "rc2_publishing_publications");
        migrationBuilder.DropColumn("RemoteVerificationCompleted", "rc2_publishing_publications");
        migrationBuilder.DropColumn("ThumbnailCompleted", "rc2_publishing_publications");
        migrationBuilder.DropColumn("VideoCreatedUtc", "rc2_publishing_publications");
        migrationBuilder.DropColumn("VideoUploadCompleted", "rc2_publishing_publications");
    }
}
