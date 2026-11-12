import { request, requestBlob } from '../http'
import type { ApiKey, EmissaoResponse } from '../types'

export type ModeloEmissao = 'nfe' | 'nfce' | 'nfse'

export const playground = {
  /**
   * Emite um documento pelo caminho real do integrador: API key de tenant no
   * header `Authorization: ApiKey …` e `Idempotency-Key` obrigatório.
   */
  emitir: (modelo: ModeloEmissao, apiKey: string, body: unknown, idempotencyKey: string) =>
    request<EmissaoResponse | { id: string; status: string; links?: { consulta?: string } }>(
      // NFS-e usa a rota DPS (transmissão real, layout 1.01). A rota legada
      // `/nfse` aceita EmissaoRequest e só funciona em sandbox — fora dele o
      // mapper lança e o documento morre em ERRO_INTERNO.
      modelo === 'nfse' ? '/v1/documentos-fiscais/nfse/dps' : `/v1/documentos-fiscais/${modelo}`,
      { body, apiKey, idempotencyKey },
    ),

  consultarComoTenant: (id: string, apiKey: string) =>
    request<EmissaoResponse>(`/v1/documentos-fiscais/${id}`, { apiKey }),

  /** Lista as API keys do tenant — valida a chave colada no Playground. */
  validarApiKey: (apiKey: string) => request<ApiKey[]>('/v1/api-keys', { apiKey }),

  /**
   * Pré-visualiza o DANFE/DANFCe a partir do payload de emissão, **sem
   * transmitir, sem reservar número e sem persistir nada**. O ERP chama
   * antes do POST de emissão para conferir o PDF.
   */
  previewPdf: (apiKey: string, tipo: 'NFE' | 'NFCE', payload: unknown) =>
    requestBlob('/v1/documentos-fiscais/preview-pdf', {
      method: 'POST',
      body: { tipo, payload },
      apiKey,
    }),
}
