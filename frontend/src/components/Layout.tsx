import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import { cn } from '../lib/utils'

const NAV = [
  { to: '/', label: 'Dashboard', icone: '▦', fim: true },
  { to: '/tenants', label: 'Tenants', icone: '🏢' },
  { to: '/documentos', label: 'Documentos', icone: '📄' },
  { to: '/playground', label: 'Playground', icone: '▶' },
]

export default function Layout() {
  const { auth, logout } = useAuth()
  const navigate = useNavigate()

  return (
    <div className="flex min-h-screen">
      <aside className="fixed inset-y-0 left-0 z-40 flex w-60 flex-col border-r border-zinc-800 bg-zinc-900/40">
        <div className="flex items-center gap-2.5 border-b border-zinc-800 px-5 py-4">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-600/20 text-sm font-bold text-emerald-400">
            F
          </div>
          <div>
            <p className="text-sm font-semibold text-zinc-100">FiscalAPI</p>
            <p className="text-[10px] tracking-widest text-zinc-500 uppercase">Admin Panel</p>
          </div>
        </div>

        <nav className="flex-1 space-y-1 px-3 py-4">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.fim}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-emerald-600/15 font-medium text-emerald-300'
                    : 'text-zinc-400 hover:bg-zinc-800/60 hover:text-zinc-200',
                )
              }
            >
              <span className="w-4 text-center text-xs opacity-80">{item.icone}</span>
              {item.label}
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-zinc-800 px-4 py-3">
          <p className="truncate text-xs text-zinc-400">{auth?.email}</p>
          <button
            onClick={() => {
              logout()
              navigate('/login')
            }}
            className="mt-1.5 text-[11px] text-zinc-500 transition-colors hover:text-red-400"
          >
            Sair da sessão
          </button>
        </div>
      </aside>

      <main className="ml-60 flex-1">
        <div className="mx-auto max-w-6xl px-8 py-8">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
