import * as SwitchPrimitive from '@radix-ui/react-switch'
import { cn } from '../../lib/utils'

// ==========================================================================
// Interruptor
//
// Sobre o Radix Switch: `role="switch"` + `aria-checked` + navegação por
// espaço. A versão anterior usava um button com `translate-x-4.5`, que
// estourava 2px da trilha — o knob ficava desalinhado do trilho.
// ==========================================================================

export interface SwitchProps {
  checked: boolean
  onChange: (v: boolean) => void
  label: string
  disabled?: boolean
  className?: string
}

export function Switch({ checked, onChange, label, disabled, className }: SwitchProps) {
  return (
    <SwitchPrimitive.Root
      checked={checked}
      onCheckedChange={onChange}
      disabled={disabled}
      aria-label={label}
      className={cn(
        'group relative h-5 w-9 shrink-0 rounded-full transition-colors',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-carimbo',
        'disabled:cursor-not-allowed disabled:opacity-50',
        checked ? 'bg-carimbo' : 'bg-line-strong',
        className,
      )}
    >
      <SwitchPrimitive.Thumb
        className={cn(
          'block h-4 w-4 rounded-full bg-white shadow transition-transform',
          // O Radix põe data-state no elemento raiz (o button), não no
          // thumb — então o seletor precisa mirar o descendente.
          // Duas classes translate-x em conflito resolvem por ordem no CSS,
          // não no atributo, e o knob ficava invisível.
          'group-data-[state=unchecked]:translate-x-0.5',
          'group-data-[state=checked]:translate-x-[1.125rem]',
        )}
      />
    </SwitchPrimitive.Root>
  )
}
