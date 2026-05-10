using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260510000000_AddSkyAlertSubscriptions")]
    public partial class AddSkyAlertSubscriptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    preferredChannel = table.Column<string>(type: "text", nullable: false),
                    regionId = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false),
                    createdUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_alert_subscribers", x => x.Id));

            migrationBuilder.CreateTable(
                name: "alert_preferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    eventTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    preferredAlertTimeLocal = table.Column<string>(type: "text", nullable: false),
                    minimumEventScore = table.Column<double>(type: "double precision", nullable: false),
                    dailySkyGuideReminderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    specialEventAlertsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_preferences", x => x.Id);
                    table.ForeignKey("FK_alert_preferences_alert_subscribers_subscriberId", x => x.subscriberId, "alert_subscribers", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alert_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriberId = table.Column<Guid>(type: "uuid", nullable: false),
                    eventId = table.Column<string>(type: "text", nullable: true),
                    regionId = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    scheduledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sentUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_alert_notifications", x => x.Id));

            migrationBuilder.CreateIndex("IX_alert_subscribers_email_regionId_isActive", "alert_subscribers", new[] { "email", "regionId", "isActive" });
            migrationBuilder.CreateIndex("IX_alert_preferences_subscriberId", "alert_preferences", "subscriberId", unique: true);
            migrationBuilder.CreateIndex("IX_alert_notifications_subscriberId_eventId_regionId", "alert_notifications", new[] { "subscriberId", "eventId", "regionId" }, unique: true);
            migrationBuilder.CreateIndex("IX_alert_notifications_status_scheduledUtc", "alert_notifications", new[] { "status", "scheduledUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "alert_notifications");
            migrationBuilder.DropTable(name: "alert_preferences");
            migrationBuilder.DropTable(name: "alert_subscribers");
        }
    }
}
