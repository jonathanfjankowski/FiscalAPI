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
  PENDENTE: 'bg-sky-500/15 text-sky-300 border-sky-500/30',
  PROCESSANDO: 'bg-indigo-500/15 text-indigo-300 border-indigo-500/30',
  AUTORIZADA: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  REJEITADA: 'bg-red-500/15 text-red-300 border-red-500/30',
  CONTINGENCIA: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  CANCELAMENTO_PENDENTE: 'bg-orange-500/15 text-orange-300 border-orange-500/30',
  CANCELADA: 'bg-zinc-500/15 text-zinc-300 border-zinc-500/30',
  ERRO_CANCELAMENTO: 'bg-red-500/15 text-red-300 border-red-500/30',
  DENEGADA: 'bg-fuchsia-500/15 text-fuchsia-300 border-fuchsia-500/30',
  ERRO_INTERNO: 'bg-red-500/15 text-red-300 border-red-500/30',
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
