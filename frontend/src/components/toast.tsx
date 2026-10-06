import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'
import { cn } from '../lib/utils'

type ToastType = 'sucesso' | 'erro' | 'info'

interface Toast {
  id: number
  tipo: ToastType
  titulo: string
  detalhe?: string
}

const ToastCtx = createContext<(t: { tipo?: ToastType; titulo: string; detalhe?: string }) => void>(
  () => {},
)

export function useToast() {
  return useContext(ToastCtx)
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(1)

  const push = useCallback(
    ({ tipo = 'info', titulo, detalhe }: { tipo?: ToastType; titulo: string; detalhe?: string }) => {
      const id = nextId.current++
      setToasts((ts) => [...ts, { id, tipo, titulo, detalhe }])
      setTimeout(() => setToasts((ts) => ts.filter((t) => t.id !== id)), 5000)
    },
    [],
  )

  const cor: Record<ToastType, string> = {
    sucesso:
      'border-emerald-200 bg-white text-emerald-800 dark:border-emerald-500/40 dark:bg-emerald-950/90 dark:text-emerald-200',
    erro: 'border-red-200 bg-white text-red-800 dark:border-red-500/40 dark:bg-red-950/90 dark:text-red-200',
    info: 'border-zinc-200 bg-white text-zinc-800 dark:border-zinc-700 dark:bg-zinc-900/95 dark:text-zinc-200',
  }

  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div className="fixed bottom-4 right-4 z-[100] flex w-96 max-w-[calc(100vw-2rem)] flex-col gap-2">
        {toasts.map((t) => (
          <div
            key={t.id}
            className={cn('rounded-md border px-4 py-3 shadow-xl backdrop-blur', cor[t.tipo])}
          >
            <p className="text-sm font-medium">{t.titulo}</p>
            {t.detalhe && <p className="mt-0.5 text-xs opacity-80">{t.detalhe}</p>}
          </div>
        ))}
      </div>
    </ToastCtx.Provider>
  )
}
