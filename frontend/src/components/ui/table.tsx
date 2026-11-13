import { type ReactNode } from 'react'
import { cn } from '../../lib/utils'

// ==========================================================================
// Tabela densa
//
// Estilo de console fiscal: linhas separadas por hairline, sem zebra, sem
// sombra. O cabeçalho NÃO usa uppercase com tracking largo — era o maior
// "tell" de template da versão anterior. A hierarquia vem do peso da fonte
// e do espaçamento, não de caixa alta.
// ==========================================================================

export function Table({
  children,
  className,
  label,
}: {
  children: ReactNode
  className?: string
  /** Nome da tabela para leitores de tela. */
  label?: string
}) {
  return (
    <div className="overflow-x-auto">
      <table aria-label={label} className={cn('w-full text-left text-[13px]', className)}>
        {children}
      </table>
    </div>
  )
}

export function Th({
  children,
  className,
  ...rest
}: React.ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      scope="col"
      className={cn(
        'border-b border-line px-3 py-2 text-[11px] font-semibold text-ink-muted',
        className,
      )}
      {...rest}
    >
      {children}
    </th>
  )
}

export function Td({
  children,
  className,
  title,
  ...rest
}: React.TdHTMLAttributes<HTMLTableCellElement>) {
  return (
    <td title={title} className={cn('border-b border-line/60 px-3 py-2 text-ink', className)} {...rest}>
      {children}
    </td>
  )
}

/** Corpo com estado vazio ou de carregamento. */
export function TableBody({ children }: { children: ReactNode }) {
  return <tbody className="divide-y divide-line/60">{children}</tbody>
}
