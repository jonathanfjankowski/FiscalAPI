namespace Fiscal.Core.Entities;

public class SequenciaNumeracao
{
    public Guid TenantId { get; set; }
    public short Modelo { get; set; }
    public short Serie { get; set; }
    public short Ambiente { get; set; }
    public long UltimoNumero { get; set; }
}
