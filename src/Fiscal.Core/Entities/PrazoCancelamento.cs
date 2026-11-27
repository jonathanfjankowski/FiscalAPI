namespace Fiscal.Core.Entities;

/// <summary>
/// Sobrescrita do prazo de cancelamento (em minutos) por UF e modelo fiscal
/// (55 = NF-e, 65 = NFC-e). Quando não há registro para a combinação, vale a
/// tabela padrão SEFAZ em <see cref="Services.TabelaPrazosCancelamentoSefaz"/>.
/// </summary>
public class PrazoCancelamento
{
    public string Uf { get; set; } = string.Empty;
    public short Modelo { get; set; }
    public int PrazoMinutos { get; set; }
    public string? Observacao { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}
