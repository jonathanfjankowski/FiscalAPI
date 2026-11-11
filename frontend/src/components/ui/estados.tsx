import { CircleAlert, Inbox, Loader2, RefreshCw, TriangleAlert } from 'lucide-react'
import { type ReactNode } from 'react'
import { cn } from '../../lib/utils'
import { Button } from './button'

// ==========================================================================
// Estados de carregamento, vazio e erro.
//
// O `ErrorState` é a peça que a versão anterior não tinha: quando a API
// caía, a lista de documentos mostrava "Nenhum documento com os filtros
// atuais" — o operador acreditava que tinha filtrado e não achado nada,
// quando na verdade a API estava fora. Aqui erro e vazio são coisas
// diferentes, e o erro diz o que houve e oferece repetir.
// ==========================================================================

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={cn('h-4 w-4 animate-spin text-ink-faint', className)} aria-hidden />
}

export function PageLoading({ mensagem = 'Carregando…' }: { mensagem?: string }) {
  return (
    <div className="flex items-center justify-center gap-2.5 py-20 text-[13px] text-ink-muted">
      <Spinner />
      {mensagem}
    </div>
  )
}

/** Esqueleto para carregamento — sem salto de layout quando o dado chega. */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('animate-pulse rounded bg-surface-sunken', className)} />
}

export interface EmptyStateProps {
  titulo: string
  descricao?: string
  acao?: ReactNode
  className?: string
}

/** Não há nada — e isso é uma informação, não um erro. */
export function EmptyState({ titulo, descricao, acao, className }: EmptyStateProps) {
  return (
    <div className={cn('flex flex-col items-center gap-2 py-14 text-center', className)}>
      <Inbox className="h-6 w-6 text-ink-faint" aria-hidden />
      <p className="text-[13px] font-medium text-ink">{titulo}</p>
      {descricao && <p className="max-w-md text-xs leading-relaxed text-ink-muted">{descricao}</p>}
      {acao && <div className="mt-2">{acao}</div>}
    </div>
  )
}

export interface ErrorStateProps {
  /** Motivo — vem do `detail` do problem+json da API. */
  titulo?: string
  detalhe?: string
  /** Ação para repetir a requisição. */
  aoTentarNovamente?: () => void
  className?: string
}

/**
 * Algo falhou. A tela diz o que falhou e oferece repetir — nunca
 * "nenhum resultado", que é o que a versão anterior mostrava.
 */
export function ErrorState({
  titulo = 'Não foi possível carregar',
  detalhe,
  aoTentarNovamente,
  className,
}: ErrorStateProps) {
  return (
    <div
      role="alert"
      className={cn(
        'flex flex-col items-center gap-2.5 rounded-lg border border-rejeitado/30 bg-rejeitado-soft/40 py-10 text-center',
        className,
      )}
    >
      <CircleAlert className="h-6 w-6 text-rejeitado" aria-hidden />
      <p className="text-[13px] font-medium text-ink">{titulo}</p>
      {detalhe && <p className="max-w-md text-xs leading-relaxed text-ink-muted">{detalhe}</p>}
      {aoTentarNovamente && (
        <Button className="mt-2" onClick={aoTentarNovamente}>
          <RefreshCw className="h-3.5 w-3.5" aria-hidden />
          Tentar de novo
        </Button>
      )}
    </div>
  )
}

/** Aviso não bloqueante — contingência, certificado vencendo, etc. */
export function WarningState({
  titulo,
  children,
  className,
}: {
  titulo?: string
  children: ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'flex items-start gap-2.5 rounded-lg border border-contingencia/30 bg-contingencia-soft/50 px-4 py-3 text-xs leading-relaxed text-ink',
        className,
      )}
    >
      <TriangleAlert className="mt-px h-4 w-4 shrink-0 text-contingencia" aria-hidden />
      <div className="min-w-0">
        {titulo && <p className="mb-0.5 font-medium text-ink">{titulo}</p>}
        <div className="text-ink-muted">{children}</div>
      </div>
    </div>
  )
}

/** Cabeçalho de página — título, descrição e ações. */
export function PageHeader({
  titulo,
  descricao,
  acoes,
  className,
}: {
  titulo: string
  descricao?: string
  acoes?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('mb-5 flex flex-wrap items-start justify-between gap-3', className)}>
      <div className="min-w-0">
        <h1 className="text-lg font-semibold tracking-tight text-ink">{titulo}</h1>
        {descricao && <p className="mt-0.5 text-xs leading-relaxed text-ink-muted">{descricao}</p>}
      </div>
      {acoes && <div className="flex shrink-0 items-center gap-2">{acoes}</div>}
    </div>
  )
}
