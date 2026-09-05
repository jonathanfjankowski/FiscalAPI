import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button, Card, Field, Input } from '../components/ui'
import { useToast } from '../components/toast'
import { useAuth } from '../hooks/useAuth'
import { ApiError } from '../lib/api'

export default function Login() {
  const { login } = useAuth()
  const toast = useToast()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [loading, setLoading] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setLoading(true)
    try {
      await login(email, senha)
      navigate('/', { replace: true })
    } catch (err) {
      const msg = err instanceof ApiError ? (err.detail ?? err.message) : 'Erro inesperado'
      toast({ tipo: 'erro', titulo: 'Falha no login', detalhe: msg })
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-emerald-600/20 text-lg font-bold text-emerald-400">
            F
          </div>
          <div className="text-center">
            <h1 className="text-lg font-semibold text-zinc-100">FiscalAPI Admin</h1>
            <p className="mt-1 text-xs text-zinc-500">Acesso restrito a operadores</p>
          </div>
        </div>

        <Card className="p-6">
          <form onSubmit={submit} className="space-y-4">
            <Field label="E-mail">
              <Input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="admin@exemplo.com"
                autoFocus
                required
              />
            </Field>
            <Field label="Senha">
              <Input
                type="password"
                value={senha}
                onChange={(e) => setSenha(e.target.value)}
                placeholder="••••••••"
                required
              />
            </Field>
            <Button type="submit" variant="primary" loading={loading} className="w-full">
              Entrar
            </Button>
          </form>
        </Card>

        <p className="mt-6 text-center text-[11px] leading-relaxed text-zinc-600">
          O primeiro operador é criado via seed no startup da API
          <br />
          (envs ADMIN_EMAIL / ADMIN_PASSWORD).
        </p>
      </div>
    </div>
  )
}
