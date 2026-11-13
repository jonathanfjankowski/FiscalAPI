import * as TabsPrimitive from '@radix-ui/react-tabs'
import { cn } from '../../lib/utils'

// ==========================================================================
// Abas
//
// Sobre o Radix Tabs: roving tabindex, `aria-selected`, navegação por
// setas. A versão anterior tipava `onChange` como `(id: never) => void` —
// um cast que desligava a checagem de tipo justamente no ponto onde o
// estado da aba é mais fácil de errar.
// ==========================================================================

export interface TabsProps {
  abas: { id: string; label: string; contagem?: number }[]
  ativa: string
  onChange: (id: string) => void
  className?: string
}

export function Tabs({ abas, ativa, onChange, className }: TabsProps) {
  return (
    <TabsPrimitive.Root
      value={ativa}
      onValueChange={onChange}
      className={cn('border-b border-line', className)}
    >
      <TabsPrimitive.List className="flex gap-1" aria-label="Seções">
        {abas.map((aba) => (
          <TabsPrimitive.Trigger
            key={aba.id}
            value={aba.id}
            className={cn(
              '-mb-px inline-flex items-center gap-1.5 border-b-2 px-3 py-2 text-[13px] transition-colors',
              'focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-carimbo',
              aba.id === ativa
                ? 'border-carimbo font-medium text-ink'
                : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
            )}
          >
            {aba.label}
            {aba.contagem !== undefined && aba.contagem > 0 && (
              <span className="tabular rounded-full bg-carimbo-soft px-1.5 text-[10px] font-medium text-carimbo">
                {aba.contagem}
              </span>
            )}
          </TabsPrimitive.Trigger>
        ))}
      </TabsPrimitive.List>
    </TabsPrimitive.Root>
  )
}
