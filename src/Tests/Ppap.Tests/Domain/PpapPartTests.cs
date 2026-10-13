using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapPartTests
{
    [Fact]
    public void Create_Should_NormalizeAndInitializeVersion_When_InputIsValid()
    {
        Guid ownerId = Guid.NewGuid();

        PpapPart part = PpapPart.Create(" tenant ", ownerId, " Part ", " PN-1 ", " A ");

        part.Id.Version.ShouldBe(7);
        part.TenantId.ShouldBe("tenant");
        part.OwnerUserId.ShouldBe(ownerId);
        part.PartName.ShouldBe("Part");
        part.PartNumber.ShouldBe("PN-1");
        part.DrawingRevision.ShouldBe("A");
        part.Version.ShouldBe(1);
        part.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void UpdateDetails_Should_IncrementVersion_When_ExpectedVersionMatches()
    {
        PpapPart part = CreatePart();

        part.UpdateDetails(" Updated ", " PN-2 ", " B ", 1);

        part.PartName.ShouldBe("Updated");
        part.Version.ShouldBe(2);
    }

    [Fact]
    public void UpdateDetails_Should_Conflict_When_ExpectedVersionIsStale()
    {
        PpapPart part = CreatePart();

        Should.Throw<CustomException>(() => part.UpdateDetails("Updated", "PN-2", "B", 2));
    }

    [Fact]
    public void AssignTemplate_Should_BeIdempotentForExactSnapshot_AndIncrementForChange()
    {
        PpapPart part = CreatePart();
        Guid templateId = Guid.NewGuid();
        Guid versionId = Guid.NewGuid();

        part.AssignTemplate(templateId, versionId, 1);
        part.AssignTemplate(templateId, versionId, 2);

        part.Version.ShouldBe(2);
        part.RuleTemplateId.ShouldBe(templateId);
        part.RuleTemplateVersionId.ShouldBe(versionId);

        part.AssignTemplate(templateId, Guid.NewGuid(), 2);
        part.Version.ShouldBe(3);
    }

    [Theory]
    [InlineData(201, 1, 1)]
    [InlineData(1, 101, 1)]
    [InlineData(1, 1, 51)]
    public void Create_Should_RejectValuesOverMaximumLength(int nameLength, int numberLength, int revisionLength)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PpapPart.Create(
            "tenant", Guid.NewGuid(), new string('a', nameLength), new string('b', numberLength), new string('c', revisionLength)));
    }

    private static PpapPart CreatePart() => PpapPart.Create("tenant", Guid.NewGuid(), "Part", "PN-1", "A");
}
