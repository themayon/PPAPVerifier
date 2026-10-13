using Integration.Tests.Infrastructure;
using Integration.Tests.Infrastructure.Extensions;

namespace Integration.Tests.Tests.Ppap;

[Collection(DesCollectionDefinition.Name)]
public sealed class PpapWorkflowTests
{
    private const string PpapBasePath = "/api/v1/ppap";

    private readonly DesWebApplicationFactory _factory;
    private readonly AuthHelper _auth;

    public PpapWorkflowTests(DesWebApplicationFactory factory)
    {
        _factory = factory;
        _auth = new AuthHelper(factory);
    }

    [Fact]
    public async Task CreateSubmission_Should_ReturnUnauthorized_When_NotAuthenticated()
    {
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("tenant", TestConstants.RootTenantId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{PpapBasePath}/submissions",
            new
            {
                partName = "Unauthenticated Part",
                partNumber = $"UNAUTH-{Guid.NewGuid():N}",
                drawingRevision = "A",
                ppapLevel = 3
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAndGetSubmission_Should_ReturnOwnerVisibleDraft()
    {
        using HttpClient client = await _auth.CreateRootAdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N");
        string partName = $"Workflow Part {suffix}";
        string partNumber = $"PPAP-{suffix}";
        const string drawingRevision = "B";
        const int ppapLevel = 3;

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"{PpapBasePath}/submissions",
            new
            {
                partName,
                partNumber,
                drawingRevision,
                ppapLevel
            });

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid submissionId = await createResponse.DeserializeAsync<Guid>();
        submissionId.ShouldNotBe(Guid.Empty);

        using HttpResponseMessage getResponse = await client.GetAsync(
            $"{PpapBasePath}/submissions/{submissionId}");

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        SubmissionResponse submission = await getResponse.DeserializeAsync<SubmissionResponse>();
        submission.Id.ShouldBe(submissionId);
        submission.PartName.ShouldBe(partName);
        submission.PartNumber.ShouldBe(partNumber);
        submission.DrawingRevision.ShouldBe(drawingRevision);
        submission.PpapLevel.ShouldBe(ppapLevel);
        submission.Status.ShouldBe("Draft");
        submission.Version.ShouldBe(0);
    }

    [Fact]
    public async Task ApproveCreditTopupTwice_Should_RemainIdempotent()
    {
        using HttpClient client = await _auth.CreateRootAdminClientAsync();
        string paymentReference = $"PPAP-TOPUP-{Guid.NewGuid():N}";
        const string decisionNote = "Approved by PPAP workflow integration test";

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"{PpapBasePath}/credits/topup-requests",
            new
            {
                creditsRequested = 50,
                paymentReference,
                note = "PPAP workflow integration test"
            });

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid topupRequestId = await createResponse.DeserializeAsync<Guid>();
        topupRequestId.ShouldNotBe(Guid.Empty);

        using HttpResponseMessage firstApprovalResponse = await client.PostAsJsonAsync(
            $"{PpapBasePath}/credits/topup-requests/{topupRequestId}/approve",
            new { decisionNote });
        firstApprovalResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpResponseMessage secondApprovalResponse = await client.PostAsJsonAsync(
            $"{PpapBasePath}/credits/topup-requests/{topupRequestId}/approve",
            new { decisionNote });
        secondApprovalResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpResponseMessage balanceResponse = await client.GetAsync(
            $"{PpapBasePath}/credits/balance");

        balanceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        CreditBalanceResponse balance = await balanceResponse.DeserializeAsync<CreditBalanceResponse>();
        balance.AvailableCredits.ShouldBe(50);
        balance.ReservedCredits.ShouldBe(0);
    }

    private sealed record SubmissionResponse(
        Guid Id,
        string PartName,
        string PartNumber,
        string DrawingRevision,
        int PpapLevel,
        string Status,
        int Version);

    private sealed record CreditBalanceResponse(
        int AvailableCredits,
        int ReservedCredits);
}
