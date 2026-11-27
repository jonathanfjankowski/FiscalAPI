import { request } from '../http'
import type { PrazoCancelamentoItem } from '../types'

export const prazosCancelamento = {
  listar: () =>
    request<{ itens: PrazoCancelamentoItem[] }>('/v1/admin/prazos-cancelamento'),

  atualizar: (uf: string, modelo: 55 | 65, payload: { prazoMinutos: number; observacao?: string | null }) =>
    request<PrazoCancelamentoItem>(`/v1/admin/prazos-cancelamento/${uf}/${modelo}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),

  restaurarPadrao: (uf: string, modelo: 55 | 65) =>
    request<PrazoCancelamentoItem>(`/v1/admin/prazos-cancelamento/${uf}/${modelo}`, {
      method: 'DELETE',
    }),
}
