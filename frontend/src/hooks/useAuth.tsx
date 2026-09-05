import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, clearAuth, getAuth, setAuth, UNAUTHORIZED_EVENT, type AuthInfo } from '../lib/api'

interface AuthCtx {
  auth: AuthInfo | null
  login: (email: string, senha: string) => Promise<void>
  logout: () => void
}

const Ctx = createContext<AuthCtx>({ auth: null, login: async () => {}, logout: () => {} })

export function useAuth() {
  return useContext(Ctx)
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuthState] = useState<AuthInfo | null>(() => getAuth())

  // 401 global: qualquer resposta 401 da API limpa a sessão em memória também
  // (RequireAuth então redireciona para /login sem precisar de F5).
  useEffect(() => {
    const handler = () => setAuthState(null)
    window.addEventListener(UNAUTHORIZED_EVENT, handler)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, handler)
  }, [])

  const value = useMemo<AuthCtx>(
    () => ({
      auth,
      login: async (email, senha) => {
        const info = await api.login(email, senha)
        setAuth(info)
        setAuthState(info)
      },
      logout: () => {
        clearAuth()
        setAuthState(null)
      },
    }),
    [auth],
  )

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
