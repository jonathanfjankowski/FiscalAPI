import * as PopoverPrimitive from '@radix-ui/react-popover'
import { cn } from '../../lib/utils'

// ==========================================================================
// Popover
//
// Sobre o Radix Popover: posiciona, fecha ao clicar fora, devolve o foco.
// Usado pelo seletor de empresa e pelas ações contextuais.
// ==========================================================================

export const Popover = PopoverPrimitive.Root
export const PopoverTrigger = PopoverPrimitive.Trigger
export const PopoverAnchor = PopoverPrimitive.Anchor

export function PopoverContent({
  className,
  align = 'start',
  sideOffset = 6,
  ...rest
}: React.ComponentPropsWithoutRef<typeof PopoverPrimitive.Content>) {
  return (
    <PopoverPrimitive.Portal>
      <PopoverPrimitive.Content
        align={align}
        sideOffset={sideOffset}
        className={cn(
          'z-50 rounded-lg border border-line bg-surface p-1 shadow-overlay',
          'focus:outline-none',
          className,
        )}
        {...rest}
      />
    </PopoverPrimitive.Portal>
  )
}
