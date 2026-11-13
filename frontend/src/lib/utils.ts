export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}

// Formatação e matriz de status moram em lib/format.ts e lib/fiscal.ts.
// Re-exportadas aqui porque as páginas existentes importavam deste arquivo;
// a migração para o import direto acontece nas Tasks 16–21.
export {
  apenasDigitos,
  copiar,
  formatarCnpj,
  formatarData,
  formatarDataCurta,
  formatarDecimal,
  formatarXml,
} from './format'

export {
  STATUS_COLOR,
  STATUS_LABEL,
  STATUS_TERMINAIS,
  STATUS_TRANSITORIOS,
} from './fiscal'
