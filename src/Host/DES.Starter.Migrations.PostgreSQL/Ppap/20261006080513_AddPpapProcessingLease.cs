using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapProcessingLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingLeaseExpiresAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingLeaseId",
                schema: "ppap",
                table: "PpapJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapJobs_TenantId_Status_ProcessingLeaseExpiresAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                columns: new[] { "TenantId", "Status", "ProcessingLeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PpapJobs_TenantId_Status_ProcessingLeaseExpiresAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "ProcessingLeaseExpiresAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "ProcessingLeaseId",
                schema: "ppap",
                table: "PpapJobs");
        }
    }
}
