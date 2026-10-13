using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Rules
{
    /// <inheritdoc />
    public partial class AddRuleEvaluationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationRuns_TenantId_SubjectId_TemplateVersionId",
                schema: "rules",
                table: "RuleEvaluationRuns",
                columns: new[] { "TenantId", "SubjectId", "TemplateVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RuleEvaluationRuns_TenantId_SubjectId_TemplateVersionId",
                schema: "rules",
                table: "RuleEvaluationRuns");
        }
    }
}
