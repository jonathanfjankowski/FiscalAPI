// ==========================================================================
// Registro de tráfego HTTP do painel.
//
// Um "Network tab" dentro do produto: cada chamada que o painel faz à API
// fica registrada com método, rota, status, duração, headers e corpos. É o
// que o operador precisa quando o ERP diz "não chegou o webhook" ou "a
// emissão deu erro" — a resposta crua da API está aqui, sem abrir o DevTools.
//
// Fica só em memória (ring buffer), nunca vai para o backend nem para o
// localStorage: corpos de resposta podem conter dado fiscal sensível.
//
// SEGREDOS SÃO REDIGIDOS antes de entrar no buffer — Authorization, senha,
// CSC, webhook secret e chaves em claro nunca são guardados.
// ==========================================================================

export interface RegistroTrafego {
  id: string
  /** Epoch ms do início da chamada. */
  inicio: number
  metodo: string
  /** Path + query, sem o host. */
  rota: string
  /** null quando a chamada não chegou a receber resposta (rede caiu). */
  status: number | null
  ok: boolean
  duracaoMs: number
  /** Bytes do corpo de resposta (aproximado, antes de truncar). */
  bytes: number
  origem: 'admin' | 'tenant'
  requestHeaders: Record<string, string>
  responseHeaders: Record<string, string>
  requestBody: string | null
  responseBody: string | null
  /** Mensagem quando a chamada falhou antes de virar resposta HTTP. */
  erro: string | null
  /** Corpos maiores que o limite foram cortados. */
  truncado: boolean
}

/** Ring buffer: o painel polla, e sem teto a memória cresceria sem parar. */
const MAX_REGISTROS = 300
/** Corpos acima disso são cortados — XML de NF-e passa fácil de 100KB. */
const MAX_CORPO = 64 * 1024

let registros: RegistroTrafego[] = []
let gravando = true
let proximoId = 1
const ouvintes = new Set<() => void>()

/** Campos cujo valor nunca entra no registro. */
const CAMPOS_SENSIVEIS = new Set([
  'senha', 'password', 'csc', 'cscid', 'webhooksecret', 'secret',
  'chave', 'apikey', 'token', 'keyhash', 'pfx', 'senhacriptografada',
])

function notificar() {
  for (const fn of ouvintes) fn()
}

export function inscrever(fn: () => void): () => void {
  ouvintes.add(fn)
  return () => ouvintes.delete(fn)
}

/** Snapshot estável para useSyncExternalStore. */
export function obterRegistros(): RegistroTrafego[] {
  return registros
}

export function estaGravando(): boolean {
  return gravando
}

export function alternarGravacao(): void {
  gravando = !gravando
  notificar()
}

export function limparRegistros(): void {
  registros = []
  notificar()
}

/** Redige um valor de header de autorização, preservando o que é público. */
function redigirAuthorization(valor: string): string {
  if (/^ApiKey\s+/i.test(valor)) {
    const chave = valor.replace(/^ApiKey\s+/i, '')
    // O prefixo (12 chars) já é público — é o que o banco guarda para lookup.
    return `ApiKey ${chave.slice(0, 12)}… [redigido]`
  }
  if (/^Bearer\s+/i.test(valor)) return 'Bearer [redigido]'
  return '[redigido]'
}

function redigirHeaders(headers: Record<string, string>): Record<string, string> {
  const saida: Record<string, string> = {}
  for (const [chave, valor] of Object.entries(headers)) {
    saida[chave] = chave.toLowerCase() === 'authorization'
      ? redigirAuthorization(valor)
      : valor
  }
  return saida
}

/** Percorre o JSON redigindo campos sensíveis, sem mutar o original. */
function redigirValor(valor: unknown): unknown {
  if (Array.isArray(valor)) return valor.map(redigirValor)
  if (valor && typeof valor === 'object') {
    const saida: Record<string, unknown> = {}
    for (const [chave, v] of Object.entries(valor as Record<string, unknown>)) {
      saida[chave] = CAMPOS_SENSIVEIS.has(chave.toLowerCase()) ? '[redigido]' : redigirValor(v)
    }
    return saida
  }
  return valor
}

/** Serializa o corpo para exibição, redigindo segredos e truncando. */
export function prepararCorpo(bruto: string | null | undefined): { texto: string | null; truncado: boolean; bytes: number } {
  if (bruto === null || bruto === undefined || bruto === '') {
    return { texto: null, truncado: false, bytes: 0 }
  }

  const bytes = bruto.length
  let texto = bruto

  // JSON: pretty-print + redação. XML/texto: só truncamento.
  const pareceJson = /^\s*[{[]/.test(bruto)
  if (pareceJson) {
    try {
      texto = JSON.stringify(redigirValor(JSON.parse(bruto)), null, 2)
    } catch {
      texto = bruto
    }
  }

  const truncado = texto.length > MAX_CORPO
  if (truncado) texto = texto.slice(0, MAX_CORPO)

  return { texto, truncado, bytes }
}

export interface InicioRegistro {
  metodo: string
  rota: string
  headers: Record<string, string>
  corpo?: string | null
  origem: 'admin' | 'tenant'
}

/** Abre um registro e devolve a função que o fecha com a resposta. */
export function registrarChamada(inicio: InicioRegistro) {
  const id = `r${proximoId++}`
  const t0 = performance.now()
  const inicioEpoch = Date.now()
  const corpoReq = prepararCorpo(inicio.corpo)

  const finalizar = (dados: {
    status: number | null
    ok: boolean
    responseHeaders?: Record<string, string>
    corpo?: string | null
    erro?: string | null
  }) => {
    if (!gravando) return

    const corpoResp = prepararCorpo(dados.corpo)
    const registro: RegistroTrafego = {
      id,
      inicio: inicioEpoch,
      metodo: inicio.metodo,
      rota: inicio.rota,
      status: dados.status,
      ok: dados.ok,
      duracaoMs: Math.round(performance.now() - t0),
      bytes: corpoResp.bytes,
      origem: inicio.origem,
      requestHeaders: redigirHeaders(inicio.headers),
      responseHeaders: dados.responseHeaders ?? {},
      requestBody: corpoReq.texto,
      responseBody: corpoResp.texto,
      erro: dados.erro ?? null,
      truncado: corpoReq.truncado || corpoResp.truncado,
    }

    // Mais recente primeiro; o excedente cai do fim.
    registros = [registro, ...registros].slice(0, MAX_REGISTROS)
    notificar()
  }

  return { id, finalizar }
}

/** Reconstrói o cURL equivalente — para reproduzir a chamada no terminal. */
export function comoCurl(r: RegistroTrafego): string {
  const base = typeof window !== 'undefined' ? window.location.origin : ''
  const linhas = [`curl -X ${r.metodo} "${base}${r.rota}" \\`]
  for (const [chave, valor] of Object.entries(r.requestHeaders)) {
    linhas.push(`  -H "${chave}: ${valor}" \\`)
  }
  if (r.requestBody) {
    // Aspas simples no corpo quebrariam o quoting do shell.
    const corpo = r.requestBody.replace(/\n\s*/g, ' ').replace(/'/g, `'\\''`)
    linhas.push(`  -d '${corpo}'`)
  } else {
    linhas[linhas.length - 1] = linhas[linhas.length - 1].replace(/ \\$/, '')
  }
  return linhas.join('\n')
}
