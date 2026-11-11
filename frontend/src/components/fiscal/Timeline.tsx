import { Check, Circle, Loader2, X } from 'lucide-react'
import { STATUS_LABEL } from '../../lib/fiscal'
import { formatarData } from '../../lib/format'
import { cn } from '../../lib/utils'

// ==========================================================================
// Linha do tempo de transições.
//
// O Playground já mostrava as transições ao vivo; o detalhe do documento não.
// Aqui cada mudança de estado tem hora e motivo — "quando foi rejeitada e por
// quê" é a pergunta que o operador faz ao abrir o documento.
// ==========================================================================

export interface Transicao {
  status: string
  em: string
  motivo?: string | null
}

/** Ícone do nó conforme o desfecho daquela transição. */
function icone(status: string) {
  if (status === 'AUTORIZADA') return { Comp: Check, classe: 'text-aprovado' }
  if (status === 'PENDENTE' || status === 'PROCESSANDO') {
    return { Comp: Loader2, classe: 'text-carimbo animate-spin' }
  }
  if (status === 'REJEITADA' || status === 'DENEGADA' || status === 'ERRO_INTERNO' ||
      status === 'ERRO_CANCELAMENTO' || status === 'FALHA_EMISSAO') {
    return { Comp: X, classe: 'text-rejeitado' }
  }
  return { Comp: Circle, classe: 'text-ink-faint' }
}

export function Timeline({
  transicoes,
  vazio = 'Nenhuma transição registrada.',
  className,
}: {
  transicoes: Transicao[]
  vazio?: string
  className?: string
}) {
  if (transicoes.length === 0) {
    return <p className={cn('py-4 text-center text-xs text-ink-faint', className)}>{vazio}</p>
  }

  return (
    <ol className={cn('space-y-0', className)} aria-label="Transições de status">
      {transicoes.map((t, i) => {
        const { Comp, classe } = icone(t.status)
        const ultima = i === transicoes.length - 1

        return (
          <li key={`${t.status}-${t.em}-${i}`} className="relative flex gap-3 pb-4 last:pb-0">
            {/* Linha vertical entre os nós. */}
            {!ultima && (
              <span
                className="absolute top-5 left-[7px] h-full w-px bg-line"
                aria-hidden
              />
            )}
            <span className="relative z-10 mt-0.5 flex h-3.5 w-3.5 shrink-0 items-center justify-center bg-paper">
              <Comp className={cn('h-3 w-3', classe)} aria-hidden />
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-baseline justify-between gap-x-2">
                <p className="text-[13px] text-ink">{STATUS_LABEL[t.status] ?? t.status}</p>
                <p className="font-mono text-[10px] text-ink-faint tabular">{formatarData(t.em)}</p>
              </div>
              {t.motivo && (
                <p className="mt-0.5 text-[11px] leading-snug text-ink-muted">{t.motivo}</p>
              )}
            </div>
          </li>
        )
      })}
    </ol>
  )
}