using System.Text;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Features;

public sealed class ExtractionRequestIdentityTests
{
    [Fact]
    public void BuildIdempotencyKey_Should_ReturnCanonicalValue_When_WithinLimit()
    {
        Guid submissionDocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string key = ExtractionRequestIdentity.BuildIdempotencyKey(
            "tenant",
            submissionDocumentId,
            new string('a', 64),
            "gemini",
            "model",
            "aspect-v1");

        key.ShouldBe($"tenant:{submissionDocumentId:D}:{new string('a', 64)}:gemini:model:aspect-v1");
    }

    [Fact]
    public void BuildIdempotencyKey_Should_ReturnStableLowercaseHash_When_ValueExceedsLimit()
    {
        Guid submissionDocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string first = ExtractionRequestIdentity.BuildIdempotencyKey(
            new string('t', 64), submissionDocumentId, new string('a', 64),
            new string('p', 100), new string('m', 200), new string('v', 100));
        string second = ExtractionRequestIdentity.BuildIdempotencyKey(
            new string('t', 64), submissionDocumentId, new string('a', 64),
            new string('p', 100), new string('m', 200), new string('v', 100));

        first.ShouldBe(second);
        first.Length.ShouldBe(64);
        first.Any(char.IsUpper).ShouldBeFalse();
    }

    [Fact]
    public async Task ComputeSha256Async_Should_HashContentAndEnforceBound()
    {
        byte[] content = Encoding.UTF8.GetBytes("document-content");
        await using MemoryStream stream = new(content);

        string hash = await ExtractionRequestIdentity.ComputeSha256Async(stream, content.Length, CancellationToken.None);

        hash.ShouldBe("f53a7ee9b0257605da71b5df4dfde3a12924bdf5ac7200c42b886ba55b53a517");

        await using MemoryStream oversized = new(content);
        await Should.ThrowAsync<InvalidDataException>(() =>
            ExtractionRequestIdentity.ComputeSha256Async(oversized, content.Length - 1, CancellationToken.None));
    }
}
