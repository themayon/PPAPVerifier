using FluentValidation;
using DES.Modules.Files.Contracts.v1.Commands;

namespace DES.Modules.Files.Features.v1.DeleteFile;

public sealed class DeleteFileCommandValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileCommandValidator()
    {
        RuleFor(x => x.FileAssetId).NotEmpty();
    }
}
