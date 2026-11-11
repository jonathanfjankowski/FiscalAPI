import { request } from '../http'
import type {
  ApiKey,
  ApiKeyCriada,
  Certificado,
  Tenant,
  TenantCriadoComApiKey,
  TenantInput,
  WebhookEntregasResp,
} from '../types'

export const empresas = {
  listTenants: () => request<Tenant[]>('/v1/admin/tenants'),
  getTenant: (id: string) => request<Tenant>(`/v1/admin/tenants/${id}`),

  /**
   * Cria a empresa. Com `criarApiKey` informado, a primeira API key é
   * gerada na mesma transação e a chave em claro vem na resposta — aparece
   * uma única vez.
   */
  createTenant: (body: TenantInput) =>
    request<string | TenantCriadoComApiKey>('/v1/admin/tenants', { body }),

  /** Atualização parcial: campos nulos não são alterados. */
  updateTenant: (id: string, body: Partial<TenantInput>) =>
    request<void>(`/v1/admin/tenants/${id}`, { method: 'PUT', body }),

  /** Soft-delete: a empresa fica inativa, sem apagar dados nem revogar chaves. */
  disableTenant: (id: string) => request<void>(`/v1/admin/tenants/${id}`, { method: 'DELETE' }),

  // ---------- API keys ----------

  listApiKeys: (tenantId: string) => request<ApiKey[]>(`/v1/admin/tenants/${tenantId}/api-keys`),
  createApiKey: (tenantId: string, body: { descricao?: string; ambiente: string }) =>
    request<ApiKeyCriada>(`/v1/admin/tenants/${tenantId}/api-keys`, { body }),
  revokeApiKey: (tenantId: string, keyId: string) =>
    request<void>(`/v1/admin/tenants/${tenantId}/api-keys/${keyId}`, { method: 'DELETE' }),

  // ---------- Certificados ----------

  listCertificados: (tenantId: string) =>
    request<Certificado[]>(`/v1/admin/tenants/${tenantId}/certificados`),

  uploadCertificado: (tenantId: string, pfx: File, senha: string) => {
    const fd = new FormData()
    fd.append('pfx', pfx)
    fd.append('senha', senha)
    return request<Certificado>(`/v1/admin/tenants/${tenantId}/certificados`, { formData: fd })
  },

  /** Desativa o certificado (soft — some dos ativos, o registro fica). */
  disableCertificado: (tenantId: string, certificadoId: string) =>
    request<void>(`/v1/admin/tenants/${tenantId}/certificados/${certificadoId}`, { method: 'DELETE' }),

  /** Reativa o certificado e desativa os outros ativos da empresa. */
  ativarCertificado: (tenantId: string, certificadoId: string) =>
    request<Certificado>(
      `/v1/admin/tenants/${tenantId}/certificados/${certificadoId}/ativar`,
      { method: 'POST' },
    ),

  // ---------- Webhooks (outbox) ----------

  listWebhookEntregas: (
    tenantId: string,
    params: { page?: number; pageSize?: number; status?: string },
  ) => {
    const qs = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
    }
    return request<WebhookEntregasResp>(
      `/v1/admin/tenants/${tenantId}/webhooks/entregas?${qs.toString()}`,
    )
  },

  reenviarWebhookEntrega: (tenantId: string, entregaId: string) =>
    request<unknown>(
      `/v1/admin/tenants/${tenantId}/webhooks/entregas/${entregaId}/reenviar`,
      { method: 'POST' },
    ),
}
