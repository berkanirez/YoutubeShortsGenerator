using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortsGenerator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JobPendingWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InterruptionCount",
                table: "VideoJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PendingWork",
                table: "VideoJobs",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterruptionCount",
                table: "VideoJobs");

            migrationBuilder.DropColumn(
                name: "PendingWork",
                table: "VideoJobs");
        }
    }
}
