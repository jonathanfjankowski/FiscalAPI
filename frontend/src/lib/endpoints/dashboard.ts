import { request } from '../http'
import type { Dashboard } from '../types'

export const dashboard = {
  /**
   * Visão geral da plataforma. Além das contagens, traz as listas de
   * atenção (fila de emissão, certificados vencendo, webhooks em falha,
   * rejeições recentes) para o painel mostrar o que precisa de ação, e não
   * só quantos documentos existem.
   */
  resumo: () => request<Dashboard>('/v1/admin/dashboard'),
}
