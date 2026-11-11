// Cliente HTTP do painel. Auth do operador via JWT (localStorage); o
// Playground usa API key de tenant direto (header ApiKey), exercitando o
// mesmo caminho de um integrador real.
//
// O transporte (fetch, headers, 401 global) mora em lib/http.ts; os tipos
// em lib/types.ts. Este arquivo só re-exporta, para as páginas antigas
// continuarem importando daqui durante a migração.

export {
  ApiError,
  UNAUTHORIZED_EVENT,
  clearAuth,
  getAuth,
  request,
  setAuth,
  type AuthInfo,
  type RequestOptions,
} from './http'

import { request, type AuthInfo } from './http'

// ---------- Tipos ----------

export interface Tenant {
  id: string
  cnpj: string
  razaoSocial: string
  uf: string
  codigoMunicipioIbge?: string | null
  regimeTributario: number
  ambientePadrao: 'producao' | 'homologacao'
  inscricaoEstadual?: string | null
  logradouro?: string | null
  numero?: string | null
  complemento?: string | null
  bairro?: string | null
  cep?: string | null
  nomeMunicipio?: string | null
  webhookUrl?: string | null
  sandbox: boolean
  cscId?: string | null
  ativo: boolean
  criadoEm: string
  apiKeysAtivas: number
  certificadosAtivos: number
}

export interface TenantInput {
  cnpj: string
  razaoSocial: string
  uf: string
  codigoMunicipioIbge?: string
  regimeTributario?: number
  ambientePadrao?: string
  inscricaoEstadual?: string
  logradouro?: string
  numero?: string
  complemento?: string
  bairro?: string
  cep?: string
  nomeMunicipio?: string
  webhookUrl?: string
  webhookSecret?: string
  ativo?: boolean
  sandbox?: boolean
  cscId?: string
  csc?: string
}

export interface ApiKey {
  id: string
  prefixo: string
  descricao?: string | null
  ambiente: string
  ativa: boolean
  criadoEm: string
}

export interface ApiKeyCriada extends ApiKey {
  chave: string
  aviso: string
}

export interface Certificado {
  id: string
  thumbprint: string
  validoAte: string
  ativo: boolean
  criadoEm: string
}

export interface DocListItem {
  id: string
  tenantId: string
  tenantRazaoSocial: string
  tipo: string
  status: string
  ambiente: string
  modelo: number | null
  serie: number | null
  numero: number | null
  chaveAcesso: string | null
  motivoStatus: string | null
  criadoEm: string
  atualizadoEm: string
}

export interface DocListResp {
  total: number
  page: number
  pageSize: number
  itens: DocListItem[]
}

export interface DocDetail extends DocListItem {
  protocoloAutorizacao: string | null
  reciboLote: string | null
  xmlAssinado: string | null
  xmlRetornoSefaz: string | null
  tentativas: number
  modoContingencia: string | null
  proximaTentativaEm: string | null
}

export interface EventoResp {
  documentoId: string
  eventoId: string
  tipo: string
  status: string
  criadoEm: string
}

export interface Dashboard {
  documentosHoje: number
  documentos7Dias: number
  emContingencia: number
  porStatus: { status: string; total: number }[]
  tenantsAtivos: number
  apiKeysAtivas: number
  certificadosVencendo30Dias: number
}

export interface WebhookEntrega {
  id: string
  tipoEvento: string
  status: 'PENDENTE' | 'ENTREGANDO' | 'ENTREGUE' | 'FALHA'
  documentoId: string | null
  tentativas: number
  ultimoStatusCode: number | null
  ultimoErro: string | null
  proximaTentativaEm: string | null
  entregueEm: string | null
  criadoEm: string
}

export interface WebhookEntregasResp {
  page: number
  pageSize: number
  itens: WebhookEntrega[]
}

export interface BootstrapKeyInfo {
  prefixo?: string
  criadoEm?: string
  configurada?: boolean
}

export interface BootstrapKeyRotacionada {
  id: string
  chave: string
  prefixo: string
  aviso: string
}

export interface EmissaoResponse {
  id: string
  tipo: string
  status: string
  ambiente: string
  serie: number | null
  numero: number | null
  chaveAcesso: string | null
  protocoloAutorizacao: string | null
  xmlAssinado: string | null
  xmlRetornoSefaz: string | null
  motivoStatus: string | null
  criadoEm: string
  atualizadoEm: string
}

// ---------- Endpoints ----------

export const api = {
  login: (email: string, senha: string) =>
    request<AuthInfo>('/v1/admin/auth/login', { body: { email, senha } }),

  dashboard: () => request<Dashboard>('/v1/admin/dashboard'),

  listTenants: () => request<Tenant[]>('/v1/admin/tenants'),
  getTenant: (id: string) => request<Tenant>(`/v1/admin/tenants/${id}`),
  createTenant: (body: TenantInput) => request<string>('/v1/admin/tenants', { body }),
  updateTenant: (id: string, body: Partial<TenantInput>) =>
    request<void>(`/v1/admin/tenants/${id}`, { method: 'PUT', body }),
  disableTenant: (id: string) => request<void>(`/v1/admin/tenants/${id}`, { method: 'DELETE' }),

  listApiKeys: (tenantId: string) => request<ApiKey[]>(`/v1/admin/tenants/${tenantId}/api-keys`),
  createApiKey: (tenantId: string, body: { descricao?: string; ambiente: string }) =>
    request<ApiKeyCriada>(`/v1/admin/tenants/${tenantId}/api-keys`, { body }),
  revokeApiKey: (tenantId: string, keyId: string) =>
    request<void>(`/v1/admin/tenants/${tenantId}/api-keys/${keyId}`, { method: 'DELETE' }),

  listCertificados: (tenantId: string) =>
    request<Certificado[]>(`/v1/admin/tenants/${tenantId}/certificados`),
  uploadCertificado: (tenantId: string, pfx: File, senha: string) => {
    const fd = new FormData()
    fd.append('pfx', pfx)
    fd.append('senha', senha)
    return request<Certificado>(`/v1/admin/tenants/${tenantId}/certificados`, { formData: fd })
  },

  listDocumentos: (params: {
    tenantId?: string
    status?: string
    modelo?: string
    de?: string
    ate?: string
    page?: number
    pageSize?: number
  }) => {
    const qs = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
    }
    return request<DocListResp>(`/v1/admin/documentos-fiscais?${qs.toString()}`)
  },
  getDocumento: (id: string) => request<DocDetail>(`/v1/admin/documentos-fiscais/${id}`),
  cancelarDocumento: (id: string, justificativa: string) =>
    request<EventoResp>(`/v1/admin/documentos-fiscais/${id}/cancelamento`, {
      body: { justificativa },
    }),
  cartaCorrecao: (id: string, correcao: string) =>
    request<EventoResp>(`/v1/admin/documentos-fiscais/${id}/carta-correcao`, {
      body: { correcao },
    }),

  // Webhooks (visão admin, por tenant) — outbox + reenvio manual.
  listWebhookEntregas: (tenantId: string, params: { page?: number; pageSize?: number; status?: string }) => {
    const qs = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
    }
    return request<WebhookEntregasResp>(
      `/v1/admin/tenants/${tenantId}/webhooks/entregas?${qs.toString()}`,
    )
  },
  reenviarWebhookEntrega: (tenantId: string, entregaId: string) =>
    request<unknown>(`/v1/admin/tenants/${tenantId}/webhooks/entregas/${entregaId}/reenviar`, {
      method: 'POST',
    }),

  // Chave bootstrap ("master") — usada pelo ERP em POST /v1/empresas.
  getBootstrapKey: () => request<BootstrapKeyInfo>('/v1/admin/bootstrap-key'),
  rotacionarBootstrapKey: () =>
    request<BootstrapKeyRotacionada>('/v1/admin/bootstrap-key/rotacionar', { method: 'POST' }),

  // Playground — caminho real de um integrador (API key de tenant).
  emitir: (modelo: 'nfe' | 'nfce', apiKey: string, body: unknown, idempotencyKey: string) =>
    request<EmissaoResponse | { id: string; status: string; links?: { consulta?: string } }>(
      `/v1/documentos-fiscais/${modelo}`,
      { body, apiKey, idempotencyKey },
    ),
  consultarComoTenant: (id: string, apiKey: string) =>
    request<EmissaoResponse>(`/v1/documentos-fiscais/${id}`, { apiKey }),
  validarApiKey: (apiKey: string) =>
    request<ApiKey[]>('/v1/api-keys', { apiKey }),
}
