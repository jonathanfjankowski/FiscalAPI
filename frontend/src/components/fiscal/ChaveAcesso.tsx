import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { decomporChave } from '../../lib/fiscal'
import { copiar } from '../../lib/format'
import { cn } from '../../lib/utils'
import { Tooltip } from '../ui'

// ==========================================================================
// Anatomia da chave de acesso.
//
// A chave de 44 dígitos é a identidade do documento. Mostrada como string
// crua, ela é ilegível — o operador conta dígitos com o dedo para achar a
// série ou o CNPJ. Aqui ela aparece decomposta nos 9 campos que a SEFAZ
// define, com o rótulo de cada um embaixo: série, número, CNPJ e DV ficam
// legíveis de relance.
//
// É também o elemento de assinatura visual do painel — a chave em mono,
// separada por hairlines, é o que mais parece "documento fiscal".
// ==========================================================================

const CAMPOS: { chave: keyof ReturnType<typeof camposDe>; rotulo: string; titulo: string }[] = [
  { chave: 'uf', rotulo: 'cUF', titulo: 'Código da UF emitente' },
  { chave: 'aamm', rotulo: 'AAMM', titulo: 'Ano e mês de emissão' },
  { chave: 'cnpj', rotulo: 'CNPJ', titulo: 'CNPJ do emitente' },
  { chave: 'modelo', rotulo: 'mod', titulo: 'Modelo do documento (55 = NF-e, 65 = NFC-e)' },
  { chave: 'serie', rotulo: 'série', titulo: 'Série' },
  { chave: 'numero', rotulo: 'nNF', titulo: 'Número do documento' },
  { chave: 'tipoEmissao', rotulo: 'tpEmis', titulo: 'Forma de emissão (1 = normal)' },
  { chave: 'codigoNumerico', rotulo: 'cNF', titulo: 'Código numérico' },
  { chave: 'dv', rotulo: 'cDV', titulo: 'Dígito verificador' },
]

// Só para tipar o array acima sem importar o tipo.
function camposDe() {
  return decomporChave('0'.repeat(44))!
}

/** Código da UF → sigla. Só os que aparecem em documento fiscal. */
const UF_POR_CODIGO: Record<string, string> = {
  '11': 'RO', '12': 'AC', '13': 'AM', '14': 'RR', '15': 'PA', '16': 'AP', '17': 'TO',
  '21': 'MA', '22': 'PI', '23': 'CE', '24': 'RN', '25': 'PB', '26': 'PE', '27': 'AL',
  '28': 'SE', '29': 'BA', '31': 'MG', '32': 'ES', '33': 'RJ', '35': 'SP',
  '41': 'PR', '42': 'SC', '43': 'RS', '50': 'MS', '51': 'MT', '52': 'GO', '53': 'DF',
}

/** Ano/mês (AAMM) → "08/2024". */
function formatarAamm(aamm: string): string {
  if (aamm.length !== 4) return aamm
  return `${aamm.slice(2)}/${aamm.slice(0, 2)}`
}

export function ChaveAcesso({
  chave,
  compacta,
  className,
}: {
  chave: string | null | undefined
  /** Versão de uma linha — para listas densas. */
  compacta?: boolean
  className?: string
}) {
  const [copiado, setCopiado] = useState(false)
  const partes = decomporChave(chave)

  if (!chave) {
    return <span className={cn('text-xs text-ink-faint', className)}>—</span>
  }

  // Chave em formato inesperado: mostra crua, sem inventar campos.
  if (!partes) {
    return <span className={cn('font-mono text-xs break-all text-ink', className)}>{chave}</span>
  }

  async function copiarChave() {
    if (!chave) return
    if (await copiar(chave)) {
      setCopiado(true)
      setTimeout(() => setCopiado(false), 2000)
    }
  }

  const botaoCopiar = (
    <button
      type="button"
      onClick={copiarChave}
      aria-label={copiado ? 'Chave copiada' : 'Copiar chave de acesso'}
      className={cn(
        'shrink-0 rounded p-1 transition-colors',
        'text-ink-faint hover:bg-surface-sunken hover:text-ink',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-carimbo',
      )}
    >
      {copiado ? (
        <Check className="h-3.5 w-3.5 text-aprovado" aria-hidden />
      ) : (
        <Copy className="h-3.5 w-3.5" aria-hidden />
      )}
    </button>
  )

  if (compacta) {
    return (
      <span className={cn('inline-flex items-center gap-1', className)}>
        <Tooltip content={`Série ${partes.serie} · número ${partes.numero} · DV ${partes.dv}`}>
          <span className="font-mono text-[11px] tracking-tight text-ink-muted tabular">
            …{chave.slice(-8)}
          </span>
        </Tooltip>
        {botaoCopiar}
      </span>
    )
  }

  return (
    <div className={cn('rounded-lg border border-line bg-surface', className)}>
      <div className="flex items-center justify-between gap-2 border-b border-line px-3 py-1.5">
        <p className="text-[11px] font-medium text-ink-muted">
          Chave de acesso
          <span className="ml-1.5 font-normal text-ink-faint">44 dígitos</span>
        </p>
        <p className="font-mono text-[11px] text-ink-faint tabular">
          {UF_POR_CODIGO[partes.uf] ?? partes.uf} · {formatarAamm(partes.aamm)}
        </p>
        {botaoCopiar}
      </div>

      {/* Os 9 campos, separados por hairline — a chave decomposta. */}
      <div className="grid grid-cols-9">
        {CAMPOS.map((campo, i) => {
          const valor = partes[campo.chave]
          // UF e AAMM já aparecem no cabeçalho; aqui vão como código bruto.
          return (
            <div
              key={campo.chave}
              title={campo.titulo}
              className={cn(
                'min-w-0 px-2 py-2 text-center',
                i > 0 && 'border-l border-line',
                // cNF e cDV são os mais consultados — ganham fundo sutil.
                (campo.chave === 'numero' || campo.chave === 'dv') && 'bg-surface-sunken',
              )}
            >
              <p className="truncate font-mono text-xs text-ink tabular">{valor}</p>
              <p className="mt-0.5 truncate text-[9px] text-ink-faint">{campo.rotulo}</p>
            </div>
          )
        })}
      </div>
    </div>
  )
}