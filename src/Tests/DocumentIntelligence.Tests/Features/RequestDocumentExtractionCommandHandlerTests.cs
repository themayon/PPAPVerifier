using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Features;

public sealed class RequestDocumentExtractionCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDocumentExtractionRequestService _requestService =
        Substitute.For<IDocumentExtractionRequestService>();

    #region Happy Path

    [Fact]
    public async Task Handle_Should_DelegateTrustedRequest_When_OwnerMatchesCurrentUser()
    {
        Guid ownerUserId = Guid.CreateVersion7();
        RequestDocumentExtractionCommand command = CreateCommand(ownerUserId);
        Guid expectedAttemptId = Guid.CreateVersion7();
        using CancellationTokenSource cancellation = new();
        _currentUser.GetUserId().Returns(ownerUserId);
        _requestService.RequestAsync(
            command.OwnerUserId,
            command.SubmissionDocumentId,
            command.SourceFileAssetId,
            command.DocumentCode,
            cancellation.Token).Returns(expectedAttemptId);
        RequestDocumentExtractionCommandHandler sut = new(_currentUser, _requestService);

        Guid result = await sut.Handle(command, cancellation.Token);

        result.ShouldBe(expectedAttemptId);
        await _requestService.Received(1).RequestAsync(
            command.OwnerUserId,
            command.SubmissionDocumentId,
            command.SourceFileAssetId,
            command.DocumentCode,
            cancellation.Token);
    }

    #endregion

    #region Authorization

    [Fact]
    public async Task Handle_Should_ThrowUnauthorized_When_CurrentUserIdIsEmpty()
    {
        RequestDocumentExtractionCommand command = CreateCommand(Guid.CreateVersion7());
        _currentUser.GetUserId().Returns(Guid.Empty);
        RequestDocumentExtractionCommandHandler sut = new(_currentUser, _requestService);

        await Should.ThrowAsync<UnauthorizedException>(() => sut.Handle(command, CancellationToken.None).AsTask());

        await _requestService.DidNotReceive().RequestAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ThrowForbidden_When_CurrentUserDoesNotOwnRequest()
    {
        RequestDocumentExtractionCommand command = CreateCommand(Guid.CreateVersion7());
        _currentUser.GetUserId().Returns(Guid.CreateVersion7());
        RequestDocumentExtractionCommandHandler sut = new(_currentUser, _requestService);

        await Should.ThrowAsync<ForbiddenException>(() => sut.Handle(command, CancellationToken.None).AsTask());

        await _requestService.DidNotReceive().RequestAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    private static RequestDocumentExtractionCommand CreateCommand(Guid ownerUserId) => new(
        ownerUserId,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "PSW");
}
