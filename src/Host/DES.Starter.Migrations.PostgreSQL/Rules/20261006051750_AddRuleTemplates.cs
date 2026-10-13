using System;
using DES.Modules.Rules.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace DES.Starter.Migrations.PostgreSQL.Rules;

/// <inheritdoc />
[DbContext(typeof(RulesDbContext))]
[Migration("20261006051750_AddRuleTemplates")]
public class AddRuleTemplates : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable("RuleTemplates", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid");
			int? maxLength = 64;
			OperationBuilder<AddColumnOperation> tenantId = table.Column<string>("character varying(64)", null, maxLength);
			maxLength = 64;
			OperationBuilder<AddColumnOperation> stableCode = table.Column<string>("character varying(64)", null, maxLength);
			maxLength = 200;
			OperationBuilder<AddColumnOperation> name = table.Column<string>("character varying(200)", null, maxLength);
			maxLength = 2000;
			OperationBuilder<AddColumnOperation> description = table.Column<string>("character varying(2000)", null, maxLength, rowVersion: false, null, nullable: true);
			maxLength = 16;
			return new
			{
				Id = id,
				TenantId = tenantId,
				StableCode = stableCode,
				Name = name,
				Description = description,
				Lifecycle = table.Column<string>("character varying(16)", null, maxLength),
				CurrentPublishedVersionId = table.Column<Guid>("uuid", null, null, rowVersion: false, null, nullable: true),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone"),
				UpdatedAtUtc = table.Column<DateTime>("timestamp with time zone"),
				Version = table.Column<long>("bigint")
			};
		}, "rules", table =>
		{
			table.PrimaryKey("PK_RuleTemplates", x => x.Id);
		});
		migrationBuilder.CreateTable("RuleTemplateVersions", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid");
			OperationBuilder<AddColumnOperation> ruleTemplateId = table.Column<Guid>("uuid");
			OperationBuilder<AddColumnOperation> versionNumber = table.Column<int>("integer");
			int? maxLength = 16;
			return new
			{
				Id = id,
				RuleTemplateId = ruleTemplateId,
				VersionNumber = versionNumber,
				Status = table.Column<string>("character varying(16)", null, maxLength),
				CreatedByUserId = table.Column<Guid>("uuid"),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone"),
				PublishedAtUtc = table.Column<DateTime>("timestamp with time zone", null, null, rowVersion: false, null, nullable: true),
				RetiredAtUtc = table.Column<DateTime>("timestamp with time zone", null, null, rowVersion: false, null, nullable: true),
				TenantId = table.Column<string>("text")
			};
		}, "rules", table =>
		{
			table.PrimaryKey("PK_RuleTemplateVersions", x => x.Id);
			table.ForeignKey("FK_RuleTemplateVersions_RuleTemplates_RuleTemplateId", x => x.RuleTemplateId, "RuleTemplates", "Id", "rules", ReferentialAction.NoAction, ReferentialAction.Cascade);
		});
		migrationBuilder.CreateTable("TemplateDocumentRequirements", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid");
			OperationBuilder<AddColumnOperation> templateVersionId = table.Column<Guid>("uuid");
			int? maxLength = 32;
			return new
			{
				Id = id,
				TemplateVersionId = templateVersionId,
				DocumentCode = table.Column<string>("character varying(32)", null, maxLength),
				IsRequired = table.Column<bool>("boolean"),
				SortOrder = table.Column<int>("integer"),
				TenantId = table.Column<string>("text")
			};
		}, "rules", table =>
		{
			table.PrimaryKey("PK_TemplateDocumentRequirements", x => x.Id);
			table.ForeignKey("FK_TemplateDocumentRequirements_RuleTemplateVersions_TemplateV~", x => x.TemplateVersionId, "RuleTemplateVersions", "Id", "rules", ReferentialAction.NoAction, ReferentialAction.Cascade);
		});
		migrationBuilder.CreateTable("TemplateRuleAssignments", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid");
			OperationBuilder<AddColumnOperation> templateVersionId = table.Column<Guid>("uuid");
			OperationBuilder<AddColumnOperation> ruleVersionId = table.Column<Guid>("uuid");
			int? maxLength = 32;
			return new
			{
				Id = id,
				TemplateVersionId = templateVersionId,
				RuleVersionId = ruleVersionId,
				DocumentCode = table.Column<string>("character varying(32)", null, maxLength, rowVersion: false, null, nullable: true),
				ParametersJson = table.Column<string>("jsonb"),
				TenantId = table.Column<string>("text")
			};
		}, "rules", table =>
		{
			table.PrimaryKey("PK_TemplateRuleAssignments", x => x.Id);
			table.ForeignKey("FK_TemplateRuleAssignments_RuleTemplateVersions_TemplateVersio~", x => x.TemplateVersionId, "RuleTemplateVersions", "Id", "rules", ReferentialAction.NoAction, ReferentialAction.Cascade);
			table.ForeignKey("FK_TemplateRuleAssignments_RuleVersions_RuleVersionId", x => x.RuleVersionId, "RuleVersions", "Id", "rules", ReferentialAction.NoAction, ReferentialAction.Restrict);
		});
		migrationBuilder.CreateIndex("IX_RuleTemplates_TenantId_StableCode", "RuleTemplates", new string[2] { "TenantId", "StableCode" }, "rules", unique: true);
		migrationBuilder.CreateIndex("IX_RuleTemplateVersions_RuleTemplateId_VersionNumber", "RuleTemplateVersions", new string[3] { "RuleTemplateId", "VersionNumber", "TenantId" }, "rules", unique: true);
		migrationBuilder.CreateIndex("IX_TemplateDocumentRequirements_TemplateVersionId_DocumentCode", "TemplateDocumentRequirements", new string[3] { "TemplateVersionId", "DocumentCode", "TenantId" }, "rules", unique: true);
		migrationBuilder.CreateIndex("IX_TemplateRuleAssignments_RuleVersionId", "TemplateRuleAssignments", "RuleVersionId", "rules");
		migrationBuilder.CreateIndex("IX_TemplateRuleAssignments_TemplateVersionId_RuleVersionId_Doc~", "TemplateRuleAssignments", new string[4] { "TemplateVersionId", "RuleVersionId", "DocumentCode", "TenantId" }, "rules", unique: true).Annotation("Npgsql:NullsDistinct", false);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable("TemplateDocumentRequirements", "rules");
		migrationBuilder.DropTable("TemplateRuleAssignments", "rules");
		migrationBuilder.DropTable("RuleTemplateVersions", "rules");
		migrationBuilder.DropTable("RuleTemplates", "rules");
	}

	/// <inheritdoc />
	protected override void BuildTargetModel(ModelBuilder modelBuilder)
	{
		modelBuilder.HasDefaultSchema("rules").HasAnnotation("ProductVersion", "10.0.8").HasAnnotation("Relational:MaxIdentifierLength", 63);
		modelBuilder.UseIdentityByDefaultColumns();
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleDefinition", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
			b.Property<string>("Category").IsRequired().HasMaxLength(32)
				.HasColumnType("character varying(32)");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<Guid?>("CurrentPublishedVersionId").HasColumnType("uuid");
			b.Property<string>("Description").HasMaxLength(2000).HasColumnType("character varying(2000)");
			b.Property<string>("Lifecycle").IsRequired().HasMaxLength(16)
				.HasColumnType("character varying(16)");
			b.Property<string>("Name").IsRequired().HasMaxLength(200)
				.HasColumnType("character varying(200)");
			b.Property<string>("StableCode").IsRequired().HasMaxLength(64)
				.HasColumnType("character varying(64)");
			b.Property<string>("TenantId").IsRequired().HasMaxLength(64)
				.HasColumnType("character varying(64)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<long>("Version").IsConcurrencyToken().HasColumnType("bigint");
			b.HasKey("Id");
			b.HasIndex("TenantId", "StableCode").IsUnique();
			b.ToTable("RuleDefinitions", "rules");
			b.HasAnnotation("Finbuckle:MultiTenant", true);
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleTemplate", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<Guid?>("CurrentPublishedVersionId").HasColumnType("uuid");
			b.Property<string>("Description").HasMaxLength(2000).HasColumnType("character varying(2000)");
			b.Property<string>("Lifecycle").IsRequired().HasMaxLength(16)
				.HasColumnType("character varying(16)");
			b.Property<string>("Name").IsRequired().HasMaxLength(200)
				.HasColumnType("character varying(200)");
			b.Property<string>("StableCode").IsRequired().HasMaxLength(64)
				.HasColumnType("character varying(64)");
			b.Property<string>("TenantId").IsRequired().HasMaxLength(64)
				.HasColumnType("character varying(64)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<long>("Version").IsConcurrencyToken().HasColumnType("bigint");
			b.HasKey("Id");
			b.HasIndex("TenantId", "StableCode").IsUnique();
			b.ToTable("RuleTemplates", "rules");
			b.HasAnnotation("Finbuckle:MultiTenant", true);
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleTemplateVersion", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("uuid");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<Guid>("CreatedByUserId").HasColumnType("uuid");
			b.Property<DateTime?>("PublishedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<DateTime?>("RetiredAtUtc").HasColumnType("timestamp with time zone");
			b.Property<Guid>("RuleTemplateId").HasColumnType("uuid");
			b.Property<string>("Status").IsRequired().HasMaxLength(16)
				.HasColumnType("character varying(16)");
			b.Property<string>("TenantId").IsRequired().HasColumnType("text");
			b.Property<int>("VersionNumber").HasColumnType("integer");
			b.HasKey("Id");
			b.HasIndex("RuleTemplateId", "VersionNumber", "TenantId").IsUnique().HasDatabaseName("IX_RuleTemplateVersions_RuleTemplateId_VersionNumber");
			b.ToTable("RuleTemplateVersions", "rules");
			b.HasAnnotation("Finbuckle:MultiTenant", true);
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleVersion", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("uuid");
			b.Property<string>("AstJson").IsRequired().HasColumnType("jsonb");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<Guid>("CreatedByUserId").HasColumnType("uuid");
			b.Property<string>("OutcomeJson").IsRequired().HasColumnType("jsonb");
			b.Property<DateTime?>("PublishedAtUtc").HasColumnType("timestamp with time zone");
			b.Property<DateTime?>("RetiredAtUtc").HasColumnType("timestamp with time zone");
			b.Property<Guid>("RuleDefinitionId").HasColumnType("uuid");
			b.Property<string>("Status").IsRequired().HasMaxLength(16)
				.HasColumnType("character varying(16)");
			b.Property<string>("TenantId").IsRequired().HasColumnType("text");
			b.Property<int>("VersionNumber").HasColumnType("integer");
			b.HasKey("Id");
			b.HasIndex("RuleDefinitionId", "VersionNumber", "TenantId").IsUnique().HasDatabaseName("IX_RuleVersions_RuleDefinitionId_VersionNumber");
			b.ToTable("RuleVersions", "rules");
			b.HasAnnotation("Finbuckle:MultiTenant", true);
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.TemplateDocumentRequirement", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("uuid");
			b.Property<string>("DocumentCode").IsRequired().HasMaxLength(32)
				.HasColumnType("character varying(32)");
			b.Property<bool>("IsRequired").HasColumnType("boolean");
			b.Property<int>("SortOrder").HasColumnType("integer");
			b.Property<Guid>("TemplateVersionId").HasColumnType("uuid");
			b.Property<string>("TenantId").IsRequired().HasColumnType("text");
			b.HasKey("Id");
			b.HasIndex("TemplateVersionId", "DocumentCode", "TenantId").IsUnique().HasDatabaseName("IX_TemplateDocumentRequirements_TemplateVersionId_DocumentCode");
			b.ToTable("TemplateDocumentRequirements", "rules");
			b.HasAnnotation("Finbuckle:MultiTenant", true);
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.TemplateRuleAssignment", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("uuid");
			b.Property<string>("DocumentCode").HasMaxLength(32).HasColumnType("character varying(32)");
			b.Property<string>("ParametersJson").IsRequired().HasColumnType("jsonb");
			b.Property<Guid>("RuleVersionId").HasColumnType("uuid");
			b.Property<Guid>("TemplateVersionId").HasColumnType("uuid");
			b.Property<string>("TenantId").IsRequired().HasColumnType("text");
			b.HasKey("Id");
			b.HasIndex("RuleVersionId");
			b.HasIndex("TemplateVersionId", "RuleVersionId", "DocumentCode", "TenantId").IsUnique().HasDatabaseName("IX_TemplateRuleAssignments_TemplateVersionId_RuleVersionId_Doc~");
			b.HasIndex("TemplateVersionId", "RuleVersionId", "DocumentCode", "TenantId").AreNullsDistinct(nullsDistinct: false);
			b.ToTable("TemplateRuleAssignments", "rules");
			b.HasAnnotation("Finbuckle:MultiTenant", true);
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleTemplateVersion", (EntityTypeBuilder b) =>
		{
			b.HasOne("DES.Modules.Rules.Domain.RuleTemplate", null).WithMany("Versions").HasForeignKey("RuleTemplateId")
				.OnDelete(DeleteBehavior.Cascade)
				.IsRequired();
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleVersion", (EntityTypeBuilder b) =>
		{
			b.HasOne("DES.Modules.Rules.Domain.RuleDefinition", null).WithMany("Versions").HasForeignKey("RuleDefinitionId")
				.OnDelete(DeleteBehavior.Cascade)
				.IsRequired();
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.TemplateDocumentRequirement", (EntityTypeBuilder b) =>
		{
			b.HasOne("DES.Modules.Rules.Domain.RuleTemplateVersion", null).WithMany("DocumentRequirements").HasForeignKey("TemplateVersionId")
				.OnDelete(DeleteBehavior.Cascade)
				.IsRequired();
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.TemplateRuleAssignment", (EntityTypeBuilder b) =>
		{
			b.HasOne("DES.Modules.Rules.Domain.RuleVersion", null).WithMany().HasForeignKey("RuleVersionId")
				.OnDelete(DeleteBehavior.Restrict)
				.IsRequired();
			b.HasOne("DES.Modules.Rules.Domain.RuleTemplateVersion", null).WithMany("RuleAssignments").HasForeignKey("TemplateVersionId")
				.OnDelete(DeleteBehavior.Cascade)
				.IsRequired();
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleDefinition", (EntityTypeBuilder b) =>
		{
			b.Navigation("Versions");
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleTemplate", (EntityTypeBuilder b) =>
		{
			b.Navigation("Versions");
		});
		modelBuilder.Entity("DES.Modules.Rules.Domain.RuleTemplateVersion", (EntityTypeBuilder b) =>
		{
			b.Navigation("DocumentRequirements");
			b.Navigation("RuleAssignments");
		});
	}
}
