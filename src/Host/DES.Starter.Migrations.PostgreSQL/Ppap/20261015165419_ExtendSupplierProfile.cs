using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class ExtendSupplierProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactJobTitle",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DunsNumber",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteAddress",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteName",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SiteSameAsHeadquarters",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TaxId",
                schema: "ppap",
                table: "PpapSuppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PpapSupplierCertificates",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    CertificateType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapSupplierCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PpapSupplierCertificates_PpapSuppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "ppap",
                        principalTable: "PpapSuppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapSupplierCertificates_FileAssetId",
                schema: "ppap",
                table: "PpapSupplierCertificates",
                column: "FileAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSupplierCertificates_SupplierId_FileAssetId",
                schema: "ppap",
                table: "PpapSupplierCertificates",
                columns: new[] { "SupplierId", "FileAssetId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapSupplierCertificates",
                schema: "ppap");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "ContactJobTitle",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "DunsNumber",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "SiteAddress",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "SiteName",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "SiteSameAsHeadquarters",
                schema: "ppap",
                table: "PpapSuppliers");

            migrationBuilder.DropColumn(
                name: "TaxId",
                schema: "ppap",
                table: "PpapSuppliers");
        }
    }
}
