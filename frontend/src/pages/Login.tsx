import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button, Field, Input } from '../components/ui'
import { useAuth } from '../lib/authContext'
import { ApiError } from '../lib/api'

// ==========================================================================
// Login do operador — ferramenta interna, sem copy de apresentação.
//
// Uma coluna, o formulário e o que o operador precisa saber para entrar.
// O erro aparece no campo, não em toast: o problema está nos dados
// digitados e a mensagem fica onde ele está olhando.
// ==========================================================================

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setLoading(true)
    try {
      await login(email, senha)
      navigate('/', { replace: true })
    } catch (err) {
      const msg =
        err instanceof ApiError
          ? (err.detail ?? err.message)
          : 'Não foi possível entrar. Verifique a conexão com a API.'
      setErro(msg)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-paper">
      <header className="border-b border-line px-6 py-3">
        <div className="flex items-center gap-2.5">
          <span className="flex h-7 w-7 items-center justify-center rounded bg-carimbo font-mono text-xs font-semibold text-on-carimbo">
            F
          </span>
          <div>
            <p className="text-[13px] font-semibold tracking-tight text-ink">FiscalAPI</p>
            <p className="text-[10px] text-ink-muted">Painel de operação</p>
          </div>
        </div>
      </header>

      <main className="flex flex-1 items-center justify-center px-6 py-16">
        <div className="w-full max-w-xs">
          <h1 className="text-lg font-semibold tracking-tight text-ink">Entrar</h1>
          <p className="mt-1 text-xs text-ink-muted">Acesso restrito a operadores.</p>

          <form onSubmit={submit} className="mt-6 space-y-3.5" noValidate>
            <Field label="E-mail" htmlFor="email">
              <Input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="admin@exemplo.com"
                autoComplete="username"
                autoFocus
                required
                invalid={!!erro}
                className="h-9"
              />
            </Field>

            <Field label="Senha" htmlFor="senha" error={erro ?? undefined}>
              <Input
                id="senha"
                type="password"
                value={senha}
                onChange={(e) => setSenha(e.target.value)}
                placeholder="••••••••"
                autoComplete="current-password"
                required
                invalid={!!erro}
                className="h-9"
              />
            </Field>

            <Button type="submit" variant="primary" loading={loading} className="h-9 w-full">
              Entrar
            </Button>
          </form>

          <p className="mt-8 border-t border-line pt-4 text-[11px] leading-relaxed text-ink-faint">
            O primeiro operador é criado no startup da API via{' '}
            <code className="font-mono text-ink-muted">ADMIN_EMAIL</code> /{' '}
            <code className="font-mono text-ink-muted">ADMIN_PASSWORD</code>. Sem recuperação
            de senha.
          </p>
        </div>
      </main>
    </div>
  )
}