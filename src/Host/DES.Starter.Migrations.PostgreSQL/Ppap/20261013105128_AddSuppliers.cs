using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                schema: "ppap",
                table: "PpapParts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PpapSuppliers",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapSuppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpapSupplierUsers",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapSupplierUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PpapSupplierUsers_PpapSuppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "ppap",
                        principalTable: "PpapSuppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapParts_SupplierId",
                schema: "ppap",
                table: "PpapParts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSuppliers_TenantId_Code",
                schema: "ppap",
                table: "PpapSuppliers",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSuppliers_TenantId_IsActive",
                schema: "ppap",
                table: "PpapSuppliers",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapSupplierUsers_SupplierId_UserId",
                schema: "ppap",
                table: "PpapSupplierUsers",
                columns: new[] { "SupplierId", "UserId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSupplierUsers_UserId",
                schema: "ppap",
                table: "PpapSupplierUsers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PpapParts_PpapSuppliers_SupplierId",
                schema: "ppap",
                table: "PpapParts",
                column: "SupplierId",
                principalSchema: "ppap",
                principalTable: "PpapSuppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PpapParts_PpapSuppliers_SupplierId",
                schema: "ppap",
                table: "PpapParts");

            migrationBuilder.DropTable(
                name: "PpapSupplierUsers",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "PpapSuppliers",
                schema: "ppap");

            migrationBuilder.DropIndex(
                name: "IX_PpapParts_SupplierId",
                schema: "ppap",
                table: "PpapParts");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                schema: "ppap",
                table: "PpapParts");
        }
    }
}
