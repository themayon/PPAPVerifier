using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class InitialPpap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ppap");

            migrationBuilder.CreateTable(
                name: "CreditAccounts",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableCredits = table.Column<int>(type: "integer", nullable: false),
                    ReservedCredits = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditTopupRequests",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditsRequested = table.Column<int>(type: "integer", nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTopupRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpapSubmissions",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DrawingRevision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PpapLevel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditTransactions",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditTransactions_CreditAccounts_CreditAccountId",
                        column: x => x.CreditAccountId,
                        principalSchema: "ppap",
                        principalTable: "CreditAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditAccounts_TenantId_OwnerUserId",
                schema: "ppap",
                table: "CreditAccounts",
                columns: new[] { "TenantId", "OwnerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditTopupRequests_TenantId_OwnerUserId_RequestedAt",
                schema: "ppap",
                table: "CreditTopupRequests",
                columns: new[] { "TenantId", "OwnerUserId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTopupRequests_TenantId_Status_RequestedAt",
                schema: "ppap",
                table: "CreditTopupRequests",
                columns: new[] { "TenantId", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_CreatedAtUtc",
                schema: "ppap",
                table: "CreditTransactions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_CreditAccountId_Kind_Reference",
                schema: "ppap",
                table: "CreditTransactions",
                columns: new[] { "CreditAccountId", "Kind", "Reference", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_TenantId",
                schema: "ppap",
                table: "CreditTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_CreatedAtUtc",
                schema: "ppap",
                table: "PpapSubmissions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_OwnerUserId",
                schema: "ppap",
                table: "PpapSubmissions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_Status",
                schema: "ppap",
                table: "PpapSubmissions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_TenantId_OwnerUserId_Status",
                schema: "ppap",
                table: "PpapSubmissions",
                columns: new[] { "TenantId", "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_TenantId_PartNumber",
                schema: "ppap",
                table: "PpapSubmissions",
                columns: new[] { "TenantId", "PartNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditTopupRequests",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "CreditTransactions",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "PpapSubmissions",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "CreditAccounts",
                schema: "ppap");
        }
    }
}
