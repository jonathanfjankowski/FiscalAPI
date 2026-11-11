// ==========================================================================
// Formatação pt-BR — datas, CNPJ, decimal.
//
// Lógica pura, sem React nem DOM (exceto `copiar`, que depende da área de
// transferência do browser e trata a ausência dela).
// ==========================================================================

/** Data e hora no formato pt-BR. Travessão quando não há data. */
export function formatarData(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
}

/** Só a data, em pt-BR. Travessão quando não há data. */
export function formatarDataCurta(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleDateString('pt-BR')
}

/** CNPJ de 14 dígitos no padrão 00.000.000/0000-00. */
export function formatarCnpj(cnpj: string | null | undefined): string {
  if (!cnpj || cnpj.length !== 14) return cnpj ?? '—'
  return `${cnpj.slice(0, 2)}.${cnpj.slice(2, 5)}.${cnpj.slice(5, 8)}/${cnpj.slice(8, 12)}-${cnpj.slice(12)}`
}

/** Decimal no formato pt-BR (1.234,50). Travessão quando não há número. */
export function formatarDecimal(valor: number | null | undefined, casas = 2): string {
  if (valor === null || valor === undefined) return '—'
  return valor.toLocaleString('pt-BR', {
    minimumFractionDigits: casas,
    maximumFractionDigits: casas,
  })
}

/** Só os dígitos de uma string — para CNPJ/CEP digitados. */
export function apenasDigitos(valor: string): string {
  return valor.replace(/\D/g, '')
}

/**
 * Copia texto para a área de transferência. Devolve `false` quando não
 * conseguiu (sem permissão, contexto inseguro, browser sem suporte) — quem
 * chama decide o que fazer; nunca lança.
 */
export async function copiar(texto: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(texto)
    return true
  } catch {
    return false
  }
}
