namespace Fiscal.Core.Exceptions;

/// <summary>
/// Erro que nova tentativa não resolve: falha de mapeamento (dados obrigatórios
/// ausentes no payload/tenant), configuração faltante (ex.: CSC não cadastrado
/// para NFC-e), certificado inválido. O ProcessarDocumentoJob marca o documento
/// como ERRO_INTERNO em vez de entrar em CONTINGENCIA com retry.
/// </summary>
public class ErroNaoRecuperavelException : Exception
{
    public ErroNaoRecuperavelException(string mensagem) : base(mensagem) { }

    public ErroNaoRecuperavelException(string mensagem, Exception inner) : base(mensagem, inner) { }
}
