import { createContext, useContext } from 'react'

export type ToastType = 'sucesso' | 'erro' | 'info'

export interface ToastPayload {
  tipo?: ToastType
  titulo: string
  detalhe?: string
}

export const ToastContext = createContext<(t: ToastPayload) => void>(() => {})

export function useToast() {
  return useContext(ToastContext)
}
