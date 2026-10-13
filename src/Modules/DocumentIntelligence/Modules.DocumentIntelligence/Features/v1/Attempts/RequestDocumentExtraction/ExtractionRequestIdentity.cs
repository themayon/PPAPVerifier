using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;

internal static class ExtractionRequestIdentity
{
    internal const long AbsoluteMaxSourceBytes = 25L * 1024 * 1024;

    public static string BuildIdempotencyKey(
        string tenantId,
        Guid submissionDocumentId,
        string sourceSha256,
        string provider,
        string model,
        string extractorVersion)
    {
        string value = $"{tenantId}:{submissionDocumentId:D}:{sourceSha256}:{provider}:{model}:{extractorVersion}";
        if (value.Length <= Domain.ExtractionAttempt.IdempotencyKeyMaxLength)
        {
            return value;
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static async Task<string> ComputeSha256Async(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return Convert.ToHexStringLower(hash.GetHashAndReset());
                }

                total = checked(total + read);
                if (total > maxBytes)
                {
                    throw new InvalidDataException("The source document exceeds the extraction size limit.");
                }
                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
