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

// A matriz de status mora em lib/fiscal.ts (com a anatomia da chave e o
// trilho). Re-exportada aqui porque as páginas existentes importavam deste
// arquivo; a migração para o import direto acontece nas Tasks 16–21.
export {
  STATUS_COLOR,
  STATUS_LABEL,
  STATUS_TERMINAIS,
  STATUS_TRANSITORIOS,
} from './fiscal'
