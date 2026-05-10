using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Schema is applied by <see cref="AddSkyAlertSubscriptions"/>. This migration exists only to align
    /// <see cref="MediaFactoryDbContextModelSnapshot"/> with the current model (repo shipped snapshot drift).
    /// </remarks>
    public partial class SyncSkyAlertModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
