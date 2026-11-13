import { request } from '../http'
import type { BootstrapKeyInfo, BootstrapKeyRotacionada } from '../types'

export const bootstrap = {
  /** Chave master de provisionamento — a API só devolve o prefixo. */
  getBootstrapKey: () => request<BootstrapKeyInfo>('/v1/admin/bootstrap-key'),
  /** Rotaciona a chave master. A chave em claro aparece só nesta resposta. */
  rotacionarBootstrapKey: () =>
    request<BootstrapKeyRotacionada>('/v1/admin/bootstrap-key/rotacionar', { method: 'POST' }),
}
