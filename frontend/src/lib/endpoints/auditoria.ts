import { request } from '../http'
import type { AuditoriaResp } from '../types'

export interface ListarAuditoriaParams {
  tenantId?: string
  acao?: string
  recursoId?: string
  de?: string
  ate?: string
  page?: number
  pageSize?: number
}

export const auditoria = {
  /**
   * Trilha de auditoria. A tabela é append-only (trigger no banco) — o
   * endpoint é somente leitura.
   */
  listar: (params: ListarAuditoriaParams = {}) => {
    const qs = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
    }
    return request<AuditoriaResp>(`/v1/admin/auditoria?${qs.toString()}`)
  },
}
