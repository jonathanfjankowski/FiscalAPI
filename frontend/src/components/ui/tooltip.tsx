import * as TooltipPrimitive from '@radix-ui/react-tooltip'
import { cn } from '../../lib/utils'

// ==========================================================================
// Dica
//
// Sobre o Radix Tooltip: aparece no hover/foco, com o conteúdo associado
// por aria-describedby. É o que permite explicar um botão só-ícone sem
// repetir texto que já cabe na tela.
// ==========================================================================

export function TooltipProvider({ children }: { children: React.ReactNode }) {
  return (
    <TooltipPrimitive.Provider delayDuration={250} skipDelayDuration={300}>
      {children}
    </TooltipPrimitive.Provider>
  )
}

export function Tooltip({
  content,
  children,
  side = 'top',
}: {
  content: React.ReactNode
  children: React.ReactNode
  side?: 'top' | 'right' | 'bottom' | 'left'
}) {
  if (!content) return <>{children}</>

  return (
    <TooltipPrimitive.Root>
      <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
      <TooltipPrimitive.Portal>
        <TooltipPrimitive.Content
          side={side}
          sideOffset={5}
          className={cn(
            'z-50 max-w-64 rounded border border-line bg-ink px-2 py-1 text-[11px] leading-snug text-paper shadow-overlay',
          )}
        >
          {content}
        </TooltipPrimitive.Content>
      </TooltipPrimitive.Portal>
    </TooltipPrimitive.Root>
  )
}
