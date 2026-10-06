export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}

export function formatarData(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
}

export function formatarDataCurta(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('pt-BR')
}

export function formatarCnpj(cnpj: string | null | undefined): string {
  if (!cnpj || cnpj.length !== 14) return cnpj ?? '—'
  return `${cnpj.slice(0, 2)}.${cnpj.slice(2, 5)}.${cnpj.slice(5, 8)}/${cnpj.slice(8, 12)}-${cnpj.slice(12)}`
}

export function formatarDecimal(valor: number | null | undefined, casas = 2): string {
  if (valor === null || valor === undefined) return '—'
  return valor.toLocaleString('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas })
}

export function apenasDigitos(valor: string): string {
  return valor.replace(/\D/g, '')
}

export function formatarXml(xml: string | null | undefined): string {
  if (!xml) return ''
  return xml
    .replace(/></g, '>\n<')
    .replace(/>\s*\n\s*</g, '>\n<')
}

export async function copiar(texto: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(texto)
    return true
  } catch {
    return false
  }
}

// Cores de status (semânticas, nos dois temas). Literais — Tailwind gera
// classes por varredura de texto, nada de construir nomes dinamicamente.
export const STATUS_LABEL: Record<string, string> = {
  PENDENTE: 'Pendente',
  PROCESSANDO: 'Processando',
  AUTORIZADA: 'Autorizada',
  REJEITADA: 'Rejeitada',
  CONTINGENCIA: 'Contingência',
  CANCELAMENTO_PENDENTE: 'Cancelamento pendente',
  CANCELADA: 'Cancelada',
  ERRO_CANCELAMENTO: 'Erro no cancelamento',
  DENEGADA: 'Denegada',
  ERRO_INTERNO: 'Erro interno',
}

export const STATUS_COLOR: Record<string, string> = {
  PENDENTE: 'border-sky-200 bg-sky-50 text-sky-700 dark:border-sky-500/30 dark:bg-sky-500/10 dark:text-sky-300',
  PROCESSANDO:
    'border-indigo-200 bg-indigo-50 text-indigo-700 dark:border-indigo-500/30 dark:bg-indigo-500/10 dark:text-indigo-300',
  AUTORIZADA:
    'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300',
  REJEITADA: 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300',
  CONTINGENCIA:
    'border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300',
  CANCELAMENTO_PENDENTE:
    'border-orange-200 bg-orange-50 text-orange-700 dark:border-orange-500/30 dark:bg-orange-500/10 dark:text-orange-300',
  CANCELADA: 'border-zinc-200 bg-zinc-100 text-zinc-600 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-300',
  ERRO_CANCELAMENTO:
    'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300',
  DENEGADA:
    'border-fuchsia-200 bg-fuchsia-50 text-fuchsia-700 dark:border-fuchsia-500/30 dark:bg-fuchsia-500/10 dark:text-fuchsia-300',
  ERRO_INTERNO: 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300',
}

export const STATUS_TERMINAIS = [
  'AUTORIZADA',
  'REJEITADA',
  'DENEGADA',
  'CANCELADA',
  'ERRO_CANCELAMENTO',
  'ERRO_INTERNO',
]

export const STATUS_TRANSITORIOS = ['PENDENTE', 'PROCESSANDO', 'CONTINGENCIA', 'CANCELAMENTO_PENDENTE']
