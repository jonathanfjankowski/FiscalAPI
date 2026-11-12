import { request } from '../http'
import type { ManifestacaoResp, NotasRecebidasResp } from '../types'

export interface ListarNotasParams {
  tenantId?: string
  page?: number
  pageSize?: number
}

export const notas = {
  /** Notas recebidas pela distribuição DFe (cross-tenant). */
  listNotasRecebidas: (params: ListarNotasParams = {}) => {
    const qs = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
    }
    return request<NotasRecebidasResp>(`/v1/admin/notas-recebidas?${qs.toString()}`)
  },

  /**
   * XML completo (procNFe). 409 quando a distribuição devolveu só o resumo
   * — o XML chega em ciclos posteriores de sincronização.
   */
  xmlCompleto: (id: string) =>
    request<string>(`/v1/admin/notas-recebidas/${id}/xml-completo`),

  /**
   * Manifesta a nota: 210200 (confirmação), 210210 (ciência),
   * 210220 (desconhecimento) ou 210240 (não realização — exige
   * justificativa de 15 a 1000 caracteres).
   */
  manifestar: (id: string, tipo: string, justificativa?: string, idempotencyKey?: string) =>
    request<ManifestacaoResp>(`/v1/admin/notas-recebidas/${id}/manifestacao`, {
      body: { tipo, justificativa },
      idempotencyKey,
    }),
}
