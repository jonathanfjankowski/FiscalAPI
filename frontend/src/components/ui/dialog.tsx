import * as DialogPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { type ReactNode } from 'react'
import { cn } from '../../lib/utils'
import { Button } from './button'

// ==========================================================================
// Diálogo
//
// Sobre o Radix Dialog: focus trap, `role="dialog"`, `aria-labelledby`,
// ESC para fechar, scroll do body travado e retorno do foco ao fechar —
// o comportamento acessível que a versão anterior (createPortal manual)
// não tinha.
//
// O `ConfirmDialog` é a variante para ação destrutiva: a mensagem diz o que
// acontece, e o botão de confirmação nomeia a ação ("Revogar
// definitivamente"), não só "Confirmar".
// ==========================================================================

export interface DialogProps {
  open: boolean
  onClose: () => void
  titulo: string
  descricao?: string
  children: ReactNode
  rodape?: ReactNode
  largura?: 'sm' | 'md' | 'lg' | 'xl'
}

const LARGURAS = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-2xl',
  xl: 'max-w-4xl',
} as const

export function Dialog({
  open,
  onClose,
  titulo,
  descricao,
  children,
  rodape,
  largura = 'md',
}: DialogProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-ink/45 backdrop-blur-[1px]" />
        <DialogPrimitive.Content
          className={cn(
            'fixed top-1/2 left-1/2 z-50 w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2',
            'max-h-[calc(100vh-4rem)] overflow-hidden rounded-lg border border-line bg-surface shadow-overlay',
            'focus:outline-none',
            LARGURAS[largura],
          )}
        >
          <header className="flex items-start justify-between gap-4 border-b border-line px-5 py-3.5">
            <div className="min-w-0">
              <DialogPrimitive.Title className="text-sm font-semibold text-ink">
                {titulo}
              </DialogPrimitive.Title>
              {descricao && (
                <DialogPrimitive.Description className="mt-0.5 text-xs leading-relaxed text-ink-muted">
                  {descricao}
                </DialogPrimitive.Description>
              )}
            </div>
            <DialogPrimitive.Close asChild>
              <Button variant="ghost" size="sm" aria-label="Fechar" className="-mt-1 -mr-1.5">
                <X className="h-3.5 w-3.5" />
              </Button>
            </DialogPrimitive.Close>
          </header>

          <div className="max-h-[calc(100vh-12rem)] overflow-y-auto px-5 py-4">{children}</div>

          {rodape && (
            <footer className="flex items-center justify-end gap-2 border-t border-line bg-surface-sunken px-5 py-3">
              {rodape}
            </footer>
          )}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}

export interface ConfirmDialogProps {
  open: boolean
  onClose: () => void
  onConfirm: () => void
  titulo: string
  mensagem: string
  /** Nome da ação — "Revogar definitivamente" é melhor que "Confirmar". */
  confirmarLabel?: string
  cancelarLabel?: string
  perigoso?: boolean
  loading?: boolean
}

export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  titulo,
  mensagem,
  confirmarLabel = 'Confirmar',
  cancelarLabel = 'Cancelar',
  perigoso,
  loading,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      onClose={onClose}
      titulo={titulo}
      largura="sm"
      rodape={
        <>
          <Button onClick={onClose}>{cancelarLabel}</Button>
          <Button variant={perigoso ? 'danger' : 'primary'} loading={loading} onClick={onConfirm}>
            {confirmarLabel}
          </Button>
        </>
      }
    >
      <p className="text-[13px] leading-relaxed text-ink-muted">{mensagem}</p>
    </Dialog>
  )
}
