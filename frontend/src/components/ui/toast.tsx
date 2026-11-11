import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'
import { CircleAlert, CircleCheck, Info, X } from 'lucide-react'

// ==========================================================================
// Notificações
//
// `aria-live="polite"` no contêiner: o leitor de tela anuncia a mensagem
// quando ela chega, sem interromper o que o operador está fazendo.
// A versão anterior não tinha region viva — a notificação era invisível
// para quem não enxerga.
// ==========================================================================

export type ToastType = 'sucesso' | 'erro' | 'info'

export interface ToastPayload {
  tipo?: ToastType
  titulo: string
  detalhe?: string
}

interface Toast extends ToastPayload {
  id: number
  tipo: ToastType
}

const ToastCtx = createContext<(t: ToastPayload) => void>(() => {})

export function useToast() {
  return useContext(ToastCtx)
}

const ICONES: Record<ToastType, ReactNode> = {
  sucesso: <CircleCheck className="h-4 w-4 text-aprovado" aria-hidden />,
  erro: <CircleAlert className="h-4 w-4 text-rejeitado" aria-hidden />,
  info: <Info className="h-4 w-4 text-carimbo" aria-hidden />,
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(1)

  const push = useCallback(({ tipo = 'info', titulo, detalhe }: ToastPayload) => {
    const id = nextId.current++
    setToasts((ts) => [...ts, { id, tipo, titulo, detalhe }])
    setTimeout(() => setToasts((ts) => ts.filter((t) => t.id !== id)), 5000)
  }, [])

  const dismiss = useCallback((id: number) => {
    setToasts((ts) => ts.filter((t) => t.id !== id))
  }, [])

  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div
        aria-live="polite"
        aria-atomic="false"
        className="pointer-events-none fixed right-4 bottom-4 z-100 flex w-96 max-w-[calc(100vw-2rem)] flex-col gap-2"
      >
        {toasts.map((t) => (
          <div
            key={t.id}
            role="status"
            className="pointer-events-auto flex items-start gap-2.5 rounded-lg border border-line bg-surface px-4 py-3 shadow-overlay"
          >
            <span className="mt-px shrink-0">{ICONES[t.tipo]}</span>
            <div className="min-w-0 flex-1">
              <p className="text-[13px] font-medium text-ink">{t.titulo}</p>
              {t.detalhe && <p className="mt-0.5 text-xs leading-relaxed text-ink-muted">{t.detalhe}</p>}
            </div>
            <button
              type="button"
              onClick={() => dismiss(t.id)}
              aria-label="Dispensar notificação"
              className="shrink-0 rounded p-0.5 text-ink-faint transition-colors hover:bg-surface-sunken hover:text-ink"
            >
              <X className="h-3.5 w-3.5" aria-hidden />
            </button>
          </div>
        ))}
      </div>
    </ToastCtx.Provider>
  )
}
