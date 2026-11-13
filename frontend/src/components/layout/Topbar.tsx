import { useNavigate } from 'react-router-dom'
import { LogOut, Moon, Sun } from 'lucide-react'
import { useAuth } from '../../lib/authContext'
import { useTema } from '../../hooks/useTema'
import { Button, Tooltip } from '../ui'
import { EmpresaPicker } from './EmpresaPicker'
import { StatusSefaz } from './StatusSefaz'

// ==========================================================================
// Barra de comando.
//
// Empresa (contexto global) + status SEFAZ (a informação que decide se vale
// investigar uma rejeição) + tema + operador. Sem título de página aqui: o
// PageHeader de cada tela já diz onde você está.
// ==========================================================================

export function Topbar() {
  const { auth, logout } = useAuth()
  const { tema, alternar } = useTema()
  const navigate = useNavigate()

  return (
    <header className="sticky top-0 z-30 flex h-12 items-center gap-3 border-b border-line bg-surface px-4">
      <EmpresaPicker />
      <StatusSefaz />

      <div className="ml-auto flex items-center gap-1">
        <Tooltip content={tema === 'escuro' ? 'Tema claro' : 'Tema escuro'}>
          <Button
            variant="ghost"
            size="sm"
            onClick={alternar}
            aria-label={tema === 'escuro' ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
          >
            {tema === 'escuro' ? (
              <Sun className="h-3.5 w-3.5" aria-hidden />
            ) : (
              <Moon className="h-3.5 w-3.5" aria-hidden />
            )}
          </Button>
        </Tooltip>

        <div className="mx-1 h-4 w-px bg-line" aria-hidden />

        <span className="hidden max-w-40 truncate text-xs text-ink-muted sm:block" title={auth?.email}>
          {auth?.email}
        </span>

        <Tooltip content="Encerrar sessão">
          <Button
            variant="ghost"
            size="sm"
            aria-label="Sair da sessão"
            onClick={() => {
              logout()
              navigate('/login')
            }}
          >
            <LogOut className="h-3.5 w-3.5" aria-hidden />
          </Button>
        </Tooltip>
      </div>
    </header>
  )
}