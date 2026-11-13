import { estagioTrilho, STATUS_LABEL, type CorTrilho } from '../../lib/fiscal'
import { cn } from '../../lib/utils'

// ==========================================================================
// Trilho de status.
//
// Quatro estações: criado → fila → transmissão → destino. O destino carrega a
// cor do desfecho (autorizada, rejeitada, contingência, cancelada). É a única
// animação do produto: o trilho avança quando o status muda, respondendo ao
// que o operador fez — e respeita `prefers-reduced-motion` (o CSS global
// zera as transições).
//
// A pílula de status de texto continua existindo nas listas; aqui o status é
// um ponto sobre uma régua, que se lê de relance.
// ==========================================================================

const ESTACOES = ['Criado', 'Na fila', 'Transmissão', 'Destino'] as const

const COR_CLASSE: Record<CorTrilho, { ponto: string; linha: string; texto: string }> = {
  neutro: { ponto: 'bg-neutro', linha: 'bg-neutro', texto: 'text-ink' },
  carimbo: { ponto: 'bg-carimbo', linha: 'bg-carimbo', texto: 'text-ink' },
  aprovado: { ponto: 'bg-aprovado', linha: 'bg-aprovado', texto: 'text-ink' },
  rejeitado: { ponto: 'bg-rejeitado', linha: 'bg-rejeitado', texto: 'text-ink' },
  contingencia: { ponto: 'bg-contingencia', linha: 'bg-contingencia', texto: 'text-ink' },
}

export function TrilhoStatus({ status, className }: { status: string; className?: string }) {
  const estagio = estagioTrilho(status)
  const cores = COR_CLASSE[estagio.cor]
  const rotulo = STATUS_LABEL[status] ?? status

  return (
    <div className={cn('min-w-0', className)}>
      <div className="flex items-baseline justify-between gap-2">
        <p className="text-[11px] font-medium text-ink-muted">Situação</p>
        <p className={cn('text-[13px] font-medium', cores.texto)}>{rotulo}</p>
      </div>

      <ol
        className="mt-2.5 flex items-start"
        aria-label={`Situação do documento: ${estagio.rotulo}`}
      >
        {ESTACOES.map((nome, i) => {
          const atual = i === estagio.posicao
          const passado = i < estagio.posicao
          // Estações já percorridas usam a cor do desfecho; a atual é o
          // desfecho com anel; as futuras ficam neutras e discretas.
          const corPonto = passado ? cores.ponto : atual ? cores.ponto : 'bg-line-strong'
          const corLinha = passado ? cores.linha : 'bg-line'

          return (
            <li key={nome} className="flex min-w-0 flex-1 flex-col items-center">
              <div className="flex w-full items-center">
                {/* Trecho antes do ponto (some na primeira estação). */}
                <span
                  className={cn('h-px flex-1', i === 0 ? 'bg-transparent' : corLinha)}
                  aria-hidden
                />
                <span
                  className={cn(
                    'shrink-0 rounded-full transition-all duration-500',
                    atual
                      ? 'h-2.5 w-2.5 ring-2 ring-offset-2 ring-offset-surface'
                      : passado
                        ? 'h-2 w-2'
                        : 'h-1.5 w-1.5',
                    corPonto,
                    atual && estagio.cor === 'rejeitado' && 'ring-rejeitado/25',
                    atual && estagio.cor === 'aprovado' && 'ring-aprovado/25',
                    atual && estagio.cor === 'contingencia' && 'ring-contingencia/25',
                    atual && estagio.cor === 'carimbo' && 'ring-carimbo/25',
                    atual && estagio.cor === 'neutro' && 'ring-neutro/25',
                  )}
                  aria-hidden
                />
                {/* Trecho depois do ponto (some na última estação). */}
                <span
                  className={cn('h-px flex-1', i === ESTACOES.length - 1 ? 'bg-transparent' : corLinha)}
                  aria-hidden
                />
              </div>
              <p
                className={cn(
                  'mt-1.5 truncate text-[10px]',
                  atual ? 'font-medium text-ink' : 'text-ink-faint',
                )}
              >
                {nome}
              </p>
            </li>
          )
        })}
      </ol>
    </div>
  )
}