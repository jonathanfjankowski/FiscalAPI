// ==========================================================================
// Transporte HTTP do painel.
//
// Auth do operador via JWT (localStorage); o Playground usa API key de
// tenant direto (header ApiKey), exercitando o mesmo caminho de um
// integrador real.
//
// Esta camada cuida só de transporte: headers, erro, 401 global. Os
// endpoints vivem em lib/endpoints/* e os tipos em lib/types.ts.
// ==========================================================================

import { registrarChamada } from './trafego'

export interface AuthInfo {
  token: string
  expiraEm: string
  email: string
}

const AUTH_KEY = 'fiscal.admin.auth'
const API_BASE = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')

export class ApiError extends Error {
  status: number
  detail?: string

  constructor(status: number, title: string, detail?: string) {
    super(title)
    this.status = status
    this.detail = detail
  }
}

export function getAuth(): AuthInfo | null {
  try {
    const raw = localStorage.getItem(AUTH_KEY)
    if (!raw) return null
    const auth = JSON.parse(raw) as AuthInfo
    if (!auth.token || new Date(auth.expiraEm).getTime() < Date.now()) {
      localStorage.removeItem(AUTH_KEY)
      return null
    }
    return auth
  } catch {
    return null
  }
}

export function setAuth(auth: AuthInfo): void {
  localStorage.setItem(AUTH_KEY, JSON.stringify(auth))
}

export function clearAuth(): void {
  localStorage.removeItem(AUTH_KEY)
}

// Disparado quando a API responde 401 para o operador logado — o AuthProvider
// escuta e desloga de verdade (antes, só o localStorage era limpo e a UI
// continuava "logada" até um F5).
export const UNAUTHORIZED_EVENT = 'fiscal:nao-autorizado'

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  formData?: FormData
  idempotencyKey?: string
  apiKey?: string
}

/** Header de Authorization com API key de tenant (Playground). */
export function apiKeyHeader(apiKey: string): Record<string, string> {
  return { Authorization: `ApiKey ${apiKey}` }
}

/** Header de Authorization com o JWT do operador. */
export function bearerHeader(token: string): Record<string, string> {
  return { Authorization: `Bearer ${token}` }
}

export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {}
  const auth = getAuth()

  if (opts.apiKey) headers['Authorization'] = `ApiKey ${opts.apiKey}`
  else if (auth) headers['Authorization'] = `Bearer ${auth.token}`
  if (opts.idempotencyKey) headers['Idempotency-Key'] = opts.idempotencyKey
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json'

  const metodo = opts.method ?? (opts.body !== undefined || opts.formData ? 'POST' : 'GET')
  const corpoJson = opts.body !== undefined ? JSON.stringify(opts.body) : undefined

  // Registro de tráfego: o inspetor mostra a chamada crua. FormData não é
  // serializável (upload de .pfx) — registra só o marcador.
  const registro = registrarChamada({
    metodo,
    rota: path,
    headers,
    corpo: corpoJson ?? (opts.formData ? '[multipart/form-data]' : null),
    origem: opts.apiKey ? 'tenant' : 'admin',
  })

  let resp: Response
  try {
    resp = await fetch(API_BASE + path, {
      method: metodo,
      headers,
      body: opts.formData ?? corpoJson,
    })
  } catch {
    registro.finalizar({ status: null, ok: false, erro: 'Falha de rede — a API não respondeu.' })
    throw new ApiError(0, 'Não foi possível conectar à API.', 'Verifique se a API está rodando.')
  }

  const headersResp = Object.fromEntries(resp.headers.entries())

  if (resp.status === 204) {
    registro.finalizar({ status: 204, ok: true, responseHeaders: headersResp })
    return undefined as T
  }

  const text = await resp.text()
  registro.finalizar({
    status: resp.status,
    ok: resp.ok,
    responseHeaders: headersResp,
    corpo: text,
  })

  let data: unknown = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    data = text
  }

  if (!resp.ok) {
    if (resp.status === 401 && auth && !opts.apiKey) {
      clearAuth()
      window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT))
    }
    const problem = data as { title?: string; detail?: string } | null
    throw new ApiError(resp.status, problem?.title ?? `Erro ${resp.status}`, problem?.detail)
  }

  return data as T
}

/**
 * Baixa um arquivo binário autenticado (DANFE em PDF).
 *
 * Não dá para abrir a rota direto numa nova aba: o header `Authorization`
 * não acompanha navegação, e a API responde 401. Então busca com o token
 * e devolve um Blob para o painel abrir numa object URL.
 */
export async function requestBlob(path: string, opts: RequestOptions = {}): Promise<Blob> {
  const headers: Record<string, string> = {}
  const auth = getAuth()

  if (opts.apiKey) headers['Authorization'] = `ApiKey ${opts.apiKey}`
  else if (auth) headers['Authorization'] = `Bearer ${auth.token}`
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json'

  const metodo = opts.method ?? (opts.body !== undefined ? 'POST' : 'GET')
  const corpoJson = opts.body !== undefined ? JSON.stringify(opts.body) : undefined

  const registro = registrarChamada({
    metodo,
    rota: path,
    headers,
    corpo: corpoJson,
    origem: opts.apiKey ? 'tenant' : 'admin',
  })

  let resp: Response
  try {
    resp = await fetch(API_BASE + path, { method: metodo, headers, body: corpoJson })
  } catch {
    registro.finalizar({ status: null, ok: false, erro: 'Falha de rede — a API não respondeu.' })
    throw new ApiError(0, 'Não foi possível conectar à API.', 'Verifique se a API está rodando.')
  }

  const headersResp = Object.fromEntries(resp.headers.entries())

  if (!resp.ok) {
    // O corpo do erro é problem+json (RFC 7807) mesmo numa rota de arquivo.
    const textoErro = await resp.text().catch(() => '')
    registro.finalizar({
      status: resp.status, ok: false, responseHeaders: headersResp, corpo: textoErro,
    })
    if (resp.status === 401 && auth && !opts.apiKey) {
      clearAuth()
      window.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT))
    }
    let problem: { title?: string; detail?: string } | null = null
    try {
      problem = JSON.parse(textoErro) as { title?: string; detail?: string }
    } catch {
      problem = null
    }
    throw new ApiError(resp.status, problem?.title ?? `Erro ${resp.status}`, problem?.detail)
  }

  const blob = await resp.blob()
  // Binário não vai para o registro — só o tamanho e o content-type.
  registro.finalizar({
    status: resp.status,
    ok: true,
    responseHeaders: headersResp,
    corpo: `[binário ${blob.type || 'desconhecido'}, ${blob.size} bytes]`,
  })
  return blob
}
