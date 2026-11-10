using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPartDirectoryAndApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualityGrade",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TierLevel",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAtUtc",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedByUserId",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Decision",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "DecisionComment",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Classification",
                schema: "ppap",
                table: "PpapParts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<int>(
                name: "PpapLevel",
                schema: "ppap",
                table: "PpapParts",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSuppliers_TenantId_Region",
                schema: "ppap",
                table: "PpapSuppliers",
                columns: new[] { "TenantId", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_TenantId_Status_Decision",
                schema: "ppap",
                table: "PpapSubmissions",
                columns: new[] { "TenantId", "Status", "Decision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PpapSuppliers_TenantId_Region",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropIndex(
                name: "IX_PpapSubmissions_TenantId_Status_Decision",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "QualityGrade",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "Region",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "TierLevel",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "Decision",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "DecisionComment",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "Classification",
                schema: "ppap",
                table: "PpapParts");

            migrationBuilder.DropColumn(
                name: "PpapLevel",
                schema: "ppap",
                table: "PpapParts");
        }
    }
}
