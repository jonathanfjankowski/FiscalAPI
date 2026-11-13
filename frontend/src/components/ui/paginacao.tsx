import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '../../lib/utils'
import { Button, Select } from '../ui'

// ==========================================================================
// Paginação.
//
// "1–50 de 312" diz onde você está; os botões navegam. O tamanho de página
// é escolha do operador (50 é o padrão, 200 é o teto da API) — a versão
// anterior fixava 50 sem opção.
// ==========================================================================

const TAMANHOS_PAGINA = [25, 50, 100, 200] as const

export function Paginacao({
  total,
  pagina,
  pageSize,
  aoMudarPagina,
  aoMudarTamanho,
  className,
}: {
  total: number
  pagina: number
  pageSize: number
  aoMudarPagina: (n: number) => void
  aoMudarTamanho?: (n: number) => void
  className?: string
}) {
  const totalPaginas = Math.max(1, Math.ceil(total / pageSize))
  const inicio = total === 0 ? 0 : (pagina - 1) * pageSize + 1
  const fim = Math.min(total, pagina * pageSize)

  return (
    <div className={cn('flex flex-wrap items-center gap-3 px-3 py-2.5', className)}>
      <p className="text-xs text-ink-muted tabular">
        {total === 0 ? (
          'Nenhum registro'
        ) : (
          <>
            <span className="text-ink">
              {inicio}–{fim}
            </span>{' '}
            de <span className="text-ink">{total}</span>
          </>
        )}
      </p>

      {aoMudarTamanho && (
        <label className="ml-auto flex items-center gap-1.5 text-xs text-ink-muted">
          <span className="sr-only">Registros por página</span>
          <Select
            value={pageSize}
            onChange={(e) => aoMudarTamanho(Number(e.target.value))}
            className="h-7 w-20 text-xs"
            aria-label="Registros por página"
          >
            {TAMANHOS_PAGINA.map((t) => (
              <option key={t} value={t}>
                {t} / pág
              </option>
            ))}
          </Select>
        </label>
      )}

      <div className={cn('flex items-center gap-1', !aoMudarTamanho && 'ml-auto')}>
        <Button
          size="sm"
          variant="ghost"
          disabled={pagina <= 1}
          onClick={() => aoMudarPagina(pagina - 1)}
          aria-label="Página anterior"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden />
        </Button>
        <span className="min-w-16 text-center text-xs text-ink-muted tabular">
          {pagina} / {totalPaginas}
        </span>
        <Button
          size="sm"
          variant="ghost"
          disabled={pagina >= totalPaginas}
          onClick={() => aoMudarPagina(pagina + 1)}
          aria-label="Próxima página"
        >
          <ChevronRight className="h-3.5 w-3.5" aria-hidden />
        </Button>
      </div>
    </div>
  )
}