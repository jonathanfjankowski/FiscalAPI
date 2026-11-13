import { useMemo, useState } from 'react'
import { Check, Copy, Search, WrapText } from 'lucide-react'
import { copiar, formatarXml } from '../../lib/format'
import { cn } from '../../lib/utils'
import { Button } from '../ui'

// ==========================================================================
// Visualizador de XML.
//
// O painel mostrava o XML num <pre> cru — sem busca, sem destaque, sem
// quebra. O operador que precisa achar `<nNF>` no meio de 400 linhas rolava
// a página procurando. Aqui: destaque de tag/valor, busca com contagem,
// quebra de linha alternável e cópia.
// ==========================================================================

interface Token {
  texto: string
  tipo: 'tag' | 'atributo' | 'valor' | 'texto'
}

/**
 * Tokeniza uma linha de XML para colorir sem biblioteca de highlight.
 * Heurística suficiente para leitura: `<tag attr="valor">conteúdo</tag>`.
 */
function tokenizarLinha(linha: string): Token[] {
  const tokens: Token[] = []
  const regex = /(<\/?[\w:.-]+)|(\s[\w:.-]+="[^"]*")|(>)|([^<]+)/g
  let m: RegExpExecArray | null

  while ((m = regex.exec(linha)) !== null) {
    if (m[1]) tokens.push({ texto: m[1], tipo: 'tag' })
    else if (m[2]) tokens.push({ texto: m[2], tipo: 'atributo' })
    else if (m[3]) tokens.push({ texto: m[3], tipo: 'tag' })
    else if (m[4]) tokens.push({ texto: m[4], tipo: 'texto' })
  }

  return tokens.length > 0 ? tokens : [{ texto: linha, tipo: 'texto' }]
}

export function XmlViewer({
  xml,
  nomeArquivo,
  className,
}: {
  xml: string | null | undefined
  nomeArquivo?: string
  className?: string
}) {
  const [busca, setBusca] = useState('')
  const [quebrarLinha, setQuebrarLinha] = useState(false)
  const [copiado, setCopiado] = useState(false)

  const linhas = useMemo(() => (xml ? formatarXml(xml).split('\n') : []), [xml])

  const termo = busca.trim().toLowerCase()
  const correspondentes = useMemo(
    () => (termo ? linhas.filter((l) => l.toLowerCase().includes(termo)).length : 0),
    [linhas, termo],
  )

  if (!xml) {
    return (
      <p className={cn('py-8 text-center text-xs text-ink-faint', className)}>
        XML não disponível.
      </p>
    )
  }

  return (
    <div className={cn('flex min-h-0 flex-col', className)}>
      <div className="flex flex-wrap items-center gap-2 border-b border-line px-3 py-1.5">
        {nomeArquivo && (
          <span className="font-mono text-[11px] text-ink-muted">{nomeArquivo}</span>
        )}
        <span className="text-[10px] text-ink-faint">{linhas.length} linhas</span>

        <div className="ml-auto flex items-center gap-1">
          <div className="flex items-center gap-1 rounded border border-line px-1.5">
            <Search className="h-3 w-3 shrink-0 text-ink-faint" aria-hidden />
            <input
              value={busca}
              onChange={(e) => setBusca(e.target.value)}
              placeholder="Buscar no XML…"
              aria-label="Buscar no XML"
              className="h-6 w-32 bg-transparent text-[11px] text-ink placeholder:text-ink-faint focus:outline-none"
            />
            {termo && (
              <span
                className={cn(
                  'text-[10px] tabular',
                  correspondentes > 0 ? 'text-ink-muted' : 'text-rejeitado',
                )}
              >
                {correspondentes}
              </span>
            )}
          </div>

          <Button
            variant={quebrarLinha ? 'secondary' : 'ghost'}
            size="sm"
            onClick={() => setQuebrarLinha((v) => !v)}
            aria-pressed={quebrarLinha}
            aria-label="Quebrar linhas longas"
            title="Quebrar linhas longas"
          >
            <WrapText className="h-3.5 w-3.5" aria-hidden />
          </Button>

          <Button
            variant="ghost"
            size="sm"
            onClick={async () => {
              if (await copiar(xml)) {
                setCopiado(true)
                setTimeout(() => setCopiado(false), 2000)
              }
            }}
            aria-label="Copiar XML"
            title="Copiar XML"
          >
            {copiado ? (
              <Check className="h-3.5 w-3.5 text-aprovado" aria-hidden />
            ) : (
              <Copy className="h-3.5 w-3.5" aria-hidden />
            )}
          </Button>
        </div>
      </div>

      <div className="max-h-[28rem] overflow-auto bg-surface-sunken">
        <pre className="p-3 font-mono text-[11px] leading-relaxed">
          <code>
            {linhas.map((linha, i) => {
              const casa = termo !== '' && linha.toLowerCase().includes(termo)
              return (
                <div
                  key={i}
                  className={cn(
                    'flex gap-3',
                    quebrarLinha ? 'whitespace-pre-wrap break-all' : 'whitespace-pre',
                    casa && 'bg-contingencia-soft/60',
                  )}
                >
                  <span
                    className="w-9 shrink-0 text-right text-ink-faint/60 select-none tabular"
                    aria-hidden
                  >
                    {i + 1}
                  </span>
                  <span className="min-w-0 text-ink">
                    {tokenizarLinha(linha).map((tk, j) => (
                      <span
                        key={j}
                        className={
                          tk.tipo === 'tag'
                            ? 'text-carimbo'
                            : tk.tipo === 'atributo'
                              ? 'text-contingencia'
                              : 'text-ink-muted'
                        }
                      >
                        {tk.texto}
                      </span>
                    ))}
                  </span>
                </div>
              )
            })}
          </code>
        </pre>
      </div>
    </div>
  )
}