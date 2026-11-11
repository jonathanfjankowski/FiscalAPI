import * as DropdownMenuPrimitive from '@radix-ui/react-dropdown-menu'
import { cn } from '../../lib/utils'

// ==========================================================================
// Menu de ações
//
// Sobre o Radix DropdownMenu: navegação por teclado, foco preso no menu,
// `role="menu"`. Substitui as ações em linha que disputavam espaço na
// tabela.
// ==========================================================================

export const DropdownMenu = DropdownMenuPrimitive.Root
export const DropdownMenuTrigger = DropdownMenuPrimitive.Trigger

export function DropdownMenuContent({
  className,
  align = 'end',
  sideOffset = 4,
  ...rest
}: React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Content>) {
  return (
    <DropdownMenuPrimitive.Portal>
      <DropdownMenuPrimitive.Content
        align={align}
        sideOffset={sideOffset}
        className={cn(
          'z-50 min-w-44 rounded-lg border border-line bg-surface p-1 shadow-overlay',
          'focus:outline-none',
          className,
        )}
        {...rest}
      />
    </DropdownMenuPrimitive.Portal>
  )
}

export function DropdownMenuItem({
  className,
  destructive,
  ...rest
}: React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Item> & {
  destructive?: boolean
}) {
  return (
    <DropdownMenuPrimitive.Item
      className={cn(
        'flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-[13px] outline-none select-none',
        'focus:bg-surface-sunken',
        'data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        destructive ? 'text-rejeitado focus:bg-rejeitado-soft' : 'text-ink',
        className,
      )}
      {...rest}
    />
  )
}

export function DropdownMenuSeparator() {
  return <DropdownMenuPrimitive.Separator className="my-1 h-px bg-line" />
}

export function DropdownMenuLabel({ className, ...rest }: React.ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Label>) {
  return (
    <DropdownMenuPrimitive.Label
      className={cn('px-2 py-1.5 text-[11px] text-ink-faint', className)}
      {...rest}
    />
  )
}
