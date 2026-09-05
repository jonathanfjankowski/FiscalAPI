using FluentValidation;
using Fiscal.Core.Contracts;

namespace Fiscal.Api.Validators;

public class EmissaoRequestValidator : AbstractValidator<EmissaoRequest>
{
    public EmissaoRequestValidator()
    {
        RuleFor(x => x.Ambiente)
            .NotEmpty()
            .Must(a => a == "producao" || a == "homologacao")
            .WithMessage("Use 'producao' ou 'homologacao'.");

        RuleFor(x => x.Serie)
            .InclusiveBetween((short)1, (short)999);

        RuleFor(x => x.Itens)
            .NotEmpty()
            .WithMessage("A nota deve ter ao menos um item.");

        RuleForEach(x => x.Itens).SetValidator(new ItemValidator());

        RuleFor(x => x.Totais)
            .NotNull();

        RuleFor(x => x.Totais.ValorNota)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.Totais.ValorProdutos)
            .GreaterThanOrEqualTo(0);
    }
}

public class ItemValidator : AbstractValidator<ItemDto>
{
    public ItemValidator()
    {
        RuleFor(x => x.Codigo).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Descricao).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Ncm).MaximumLength(8);
        RuleFor(x => x.Cfop).MaximumLength(4);
        RuleFor(x => x.Quantidade).GreaterThan(0);
        RuleFor(x => x.ValorUnitario).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ValorTotal).GreaterThanOrEqualTo(0);
    }
}
