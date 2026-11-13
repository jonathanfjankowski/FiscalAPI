import { createContext, useContext } from 'react'
import type { AuthInfo } from './api'

// Contexto e hook ficam separados do provider: um arquivo que exporta
// componente E hook quebra o fast refresh do React (o HMR não sabe se
// preserva o estado). O provider vive em hooks/useAuth.tsx.

export interface AuthCtx {
  auth: AuthInfo | null
  login: (email: string, senha: string) => Promise<void>
  logout: () => void
}

export const AuthContext = createContext<AuthCtx>({
  auth: null,
  login: async () => {},
  logout: () => {},
})

export function useAuth(): AuthCtx {
  return useContext(AuthContext)
}
