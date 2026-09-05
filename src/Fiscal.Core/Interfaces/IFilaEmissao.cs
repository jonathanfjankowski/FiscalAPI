namespace Fiscal.Core.Interfaces;

public interface IFilaEmissao
{
    Task EnfileirarAsync(Guid documentoId, CancellationToken cancellationToken);
}
