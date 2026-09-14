using FluentValidation;
using DES.Modules.Catalog.Contracts.v1.Products;

namespace DES.Modules.Catalog.Features.v1.Products.ChangeProductPrice;

public sealed class ChangeProductPriceCommandValidator : AbstractValidator<ChangeProductPriceCommand>
{
    public ChangeProductPriceCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
