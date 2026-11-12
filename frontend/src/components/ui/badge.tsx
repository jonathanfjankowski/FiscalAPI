import { type ReactNode } from 'react'
import { cn } from '../../lib/utils'
import { STATUS_COLOR, STATUS_LABEL, STATUS_PONTO } from '../../lib/fiscal'

// ==========================================================================
// Etiquetas e ponto de status
//
// O status é o dado mais consultado do painel — por isso tem duas
// representações: o ponto colorido (para o trilho e para listas densas,
// onde a pílula de texto pesaria) e a etiqueta (para linhas de detalhe,
// onde o texto explica sozinho).
// ==========================================================================

export function Badge({
  children,
  className,
}: {
  children: ReactNode
  /**
   * Quando informado, SUBSTITUI o tom neutro (não se soma a ele): duas
   * classes de cor no mesmo atributo resolvem por ordem no CSS, não por
   * ordem no atributo — somar deixaria o badge sempre neutro.
   */
  className?: string
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded border px-1.5 py-0.5 text-[11px] font-medium whitespace-nowrap',
        className ?? 'border-line-strong bg-surface-sunken text-ink-muted',
      )}
    >
      {children}
    </span>
  )
}

/** Etiqueta de status com a cor semântica do desfecho. */
export function StatusBadge({ status }: { status: string }) {
  return (
    <Badge className={cn('border', STATUS_COLOR[status] ?? 'border-line-strong bg-surface-sunken text-ink-muted')}>
      {STATUS_LABEL[status] ?? status}
    </Badge>
  )
}

/**
 * Ponto de status — a representação compacta. O texto vai no `title` e no
 * `aria-label`, então o leitor de tela anuncia o status mesmo com o
 * ponto puro na tela.
 */
export function StatusDot({
  status,
  className,
  withLabel,
}: {
  status: string
  className?: string
  /** Mostra o rótulo ao lado do ponto (em linhas de detalhe). */
  withLabel?: boolean
}) {
  const rotulo = STATUS_LABEL[status] ?? status

  return (
    <span className={cn('inline-flex items-center gap-1.5', className)} title={rotulo}>
      <span
        className={cn('h-1.5 w-1.5 shrink-0 rounded-full', STATUS_PONTO[status] ?? 'bg-neutro')}
        aria-hidden
      />
      {withLabel ? (
        <span className="text-[13px] text-ink">{rotulo}</span>
      ) : (
        <span className="sr-only">{rotulo}</span>
      )}
    </span>
  )
}

/**
 * Ambiente (homologação/produção). Produção é sempre vermelha — nunca
 * neutra: confundir homologação com produção emite documento errado.
 */
export function AmbienteBadge({ ambiente }: { ambiente: string }) {
  const producao = ambiente === 'producao'
  return (
    <Badge
      className={
        producao
          ? 'border-rejeitado/30 bg-rejeitado-soft text-rejeitado'
          : 'border-carimbo/25 bg-carimbo-soft text-carimbo'
      }
    >
      {producao ? 'Produção' : 'Homologação'}
    </Badge>
  )
}
