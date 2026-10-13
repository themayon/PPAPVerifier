using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapRuleEvaluationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RuleEvaluationCompletedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RuleEvaluationQueuedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuleEvaluationRunId",
                schema: "ppap",
                table: "PpapJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RuleEvaluationStartedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuleEvaluationCompletedAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "RuleEvaluationQueuedAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "RuleEvaluationRunId",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "RuleEvaluationStartedAtUtc",
                schema: "ppap",
                table: "PpapJobs");
        }
    }
}
