namespace Fiscal.Core;

/// <summary>
/// Códigos de "modelo" usados internamente para numeração/consultas. 55 e 65
/// são os modelos oficiais SEFAZ; 115 é uma escolha interna da FiscalAPI para
/// agrupar a numeração de NFS-e Nacional (DPS), que não usa modelo SEFAZ.
/// </summary>
public static class ModelosDocumento
{
    public const short NFSeNacional = 115;
}
