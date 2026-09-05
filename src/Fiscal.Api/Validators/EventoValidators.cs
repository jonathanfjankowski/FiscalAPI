using FluentValidation;
using Fiscal.Api.Contracts;

namespace Fiscal.Api.Validators;

public class CancelamentoRequestValidator : AbstractValidator<CancelamentoRequest>
{
    public CancelamentoRequestValidator()
    {
        RuleFor(x => x.Justificativa)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(1000)
            .WithMessage("Justificativa deve ter entre 15 e 1000 caracteres (regra SEFAZ).");
    }
}

public class CartaCorrecaoRequestValidator : AbstractValidator<CartaCorrecaoRequest>
{
    public CartaCorrecaoRequestValidator()
    {
        RuleFor(x => x.Correcao)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(1000)
            .WithMessage("Correção deve ter entre 15 e 1000 caracteres (regra SEFAZ).");
    }
}

public class InutilizacaoRequestValidator : AbstractValidator<InutilizacaoRequest>
{
    public InutilizacaoRequestValidator()
    {
        RuleFor(x => x.Ambiente)
            .NotEmpty()
            .Must(a => a == "producao" || a == "homologacao")
            .WithMessage("Use 'producao' ou 'homologacao'.");

        RuleFor(x => x.Modelo)
            .Must(m => m is 55 or 65)
            .WithMessage("Modelo deve ser 55 (NF-e) ou 65 (NFC-e).");

        RuleFor(x => x.Serie)
            .InclusiveBetween((short)1, (short)999);

        RuleFor(x => x.NumeroFinal)
            .GreaterThanOrEqualTo(x => x.NumeroInicial)
            .WithMessage("numeroFinal deve ser >= numeroInicial.");

        RuleFor(x => x.Justificativa)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(1000);
    }
}
