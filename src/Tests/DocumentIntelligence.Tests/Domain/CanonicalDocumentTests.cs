using System.Text.Json;
using DES.Modules.DocumentIntelligence.Domain;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Domain;

public sealed class CanonicalDocumentTests
{
    [Fact]
    public void Create_Should_PreserveValidatedJson_When_InputIsValid()
    {
        const string json = "{\"documentCode\":\"PSW\",\"fields\":{}}";

        CanonicalDocument document = CanonicalDocument.Create(
            " tenant-1 ", Guid.NewGuid(), Guid.NewGuid(), " v1 ", json, Guid.NewGuid(), Guid.NewGuid());

        document.Id.ShouldNotBe(Guid.Empty);
        document.TenantId.ShouldBe("tenant-1");
        document.SchemaVersion.ShouldBe("v1");
        document.CanonicalJson.ShouldBe(json);
        document.CreatedAtUtc.ShouldNotBe(default);
    }

    [Fact]
    public void Create_Should_Throw_When_JsonIsInvalid()
    {
        Should.Throw<JsonException>(() => CanonicalDocument.Create(
            "tenant-1", Guid.NewGuid(), Guid.NewGuid(), "v1", "{invalid"));
    }

    [Fact]
    public void Create_Should_Throw_When_SchemaVersionIsTooLong()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CanonicalDocument.Create(
            "tenant-1", Guid.NewGuid(), Guid.NewGuid(), new string('v', 33), "{}"));
    }
}
