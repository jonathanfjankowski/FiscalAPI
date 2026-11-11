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
} from './format'

export {
  STATUS_COLOR,
  STATUS_LABEL,
  STATUS_TERMINAIS,
  STATUS_TRANSITORIOS,
} from './fiscal'

// `formatarXml` ainda vive aqui: é usado só pelo visualizador de XML, que
// entra na Task 15 (components/fiscal/XmlViewer.tsx).
export function formatarXml(xml: string | null | undefined): string {
  if (!xml) return ''
  return xml
    .replace(/></g, '>\n<')
    .replace(/>\s*\n\s*</g, '>\n<')
}
