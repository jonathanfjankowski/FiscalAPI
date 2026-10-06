import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { LayoutDashboard, Building2, FileText, FlaskConical, Settings, LogOut, Moon, Sun } from 'lucide-react'
import { useAuth } from '../hooks/useAuth'
import { useTema } from '../hooks/useTema'
import { cn } from '../lib/utils'

const NAV = [
  { to: '/', label: 'Dashboard', icone: LayoutDashboard, fim: true },
  { to: '/empresas', label: 'Empresas', icone: Building2 },
  { to: '/documentos', label: 'Documentos', icone: FileText },
  { to: '/playground', label: 'Playground', icone: FlaskConical },
  { to: '/configuracoes', label: 'Configurações', icone: Settings },
]

export default function Layout() {
  const { auth, logout } = useAuth()
  const navigate = useNavigate()
  const { tema, alternar } = useTema()

  return (
    <div className="flex min-h-screen">
      <aside className="fixed inset-y-0 left-0 z-40 flex w-56 flex-col border-r border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900/40">
        <div className="flex items-center gap-2.5 border-b border-zinc-200 px-5 py-4 dark:border-zinc-800">
          <div className="flex h-8 w-8 items-center justify-center rounded-md bg-blue-600 text-sm font-bold text-white">
            F
          </div>
          <div>
            <p className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">FiscalAPI</p>
            <p className="text-[10px] tracking-widest text-zinc-400 uppercase dark:text-zinc-500">Painel admin</p>
          </div>
        </div>

        <nav className="flex-1 space-y-0.5 px-3 py-4">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.fim}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-2.5 rounded-md px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-blue-50 font-medium text-blue-700 dark:bg-blue-600/15 dark:text-blue-300'
                    : 'text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/60 dark:hover:text-zinc-200',
                )
              }
            >
              <item.icone className="h-4 w-4 shrink-0" />
              {item.label}
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-zinc-200 px-4 py-3 dark:border-zinc-800">
          <p className="truncate text-xs text-zinc-600 dark:text-zinc-400">{auth?.email}</p>
          <button
            onClick={() => {
              logout()
              navigate('/login')
            }}
            className="mt-1.5 inline-flex items-center gap-1.5 text-[11px] text-zinc-400 transition-colors hover:text-red-500 dark:text-zinc-500"
          >
            <LogOut className="h-3 w-3" /> Sair da sessão
          </button>
        </div>
      </aside>

      <div className="ml-56 flex min-h-screen flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-12 items-center justify-end gap-2 border-b border-zinc-200 bg-white/90 px-6 backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/80">
          <button
            onClick={alternar}
            className="rounded-md p-2 text-zinc-500 transition-colors hover:bg-zinc-100 hover:text-zinc-800 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
            aria-label={tema === 'escuro' ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
          >
            {tema === 'escuro' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </button>
          <div className="ml-1 border-l border-zinc-200 pl-3 text-xs text-zinc-500 dark:border-zinc-800">
            {auth?.email}
          </div>
        </header>

        <main className="flex-1">
          <div className="mx-auto max-w-7xl px-8 py-7">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
