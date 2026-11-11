import { request } from '../http'
import type { StatusServicoResp } from '../types'

export const statusServico = {
  /**
   * Status do serviço SEFAZ para a UF da empresa. O admin não tem API key
   * de tenant, então o tenant vem por query — o backend resolve a UF dele.
   */
  consultar: (tenantId: string, modelo: 55 | 65 = 55, ambiente?: string) => {
    const qs = new URLSearchParams({ tenantId, modelo: String(modelo) })
    if (ambiente) qs.set('ambiente', ambiente)
    return request<StatusServicoResp>(`/v1/admin/status-servico?${qs.toString()}`)
  },
}
