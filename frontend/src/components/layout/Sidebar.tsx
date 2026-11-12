import { NavLink } from 'react-router-dom'
import {
  FileText,
  FlaskConical,
  Building2,
  Inbox,
  LayoutDashboard,
  ScrollText,
  Settings,
} from 'lucide-react'
import { cn } from '../../lib/utils'

// ==========================================================================
// Navegação lateral.
//
// Rail de 60px: ícone sempre, rótulo no hover. O painel é usado o dia
// inteiro numa tela larga — o rail devolve largura ao conteúdo sem esconder
// a navegação atrás de um menu.
// ==========================================================================

const NAV = [
  { to: '/', label: 'Visão geral', icone: LayoutDashboard, fim: true },
  { to: '/documentos', label: 'Documentos', icone: FileText },
  { to: '/notas-recebidas', label: 'Notas recebidas', icone: Inbox },
  { to: '/empresas', label: 'Empresas', icone: Building2 },
  { to: '/auditoria', label: 'Auditoria', icone: ScrollText },
  { to: '/playground', label: 'Playground', icone: FlaskConical },
  { to: '/configuracoes', label: 'Configurações', icone: Settings },
] as const

export function Sidebar() {
  return (
    <nav
      aria-label="Navegação principal"
      className={cn(
        // Desktop: rail fixo à esquerda. Mobile: barra inferior — o rail
        // lateral comeria 60px de uma tela de 360px.
        'fixed z-40 flex border-line bg-surface',
        'inset-x-0 bottom-0 h-14 flex-row border-t',
        'sm:inset-y-0 sm:left-0 sm:right-auto sm:h-auto sm:w-15 sm:flex-col sm:border-t-0 sm:border-r',
      )}
    >
      <div className="hidden h-12 items-center justify-center border-b border-line sm:flex">
        <NavLink
          to="/"
          className="flex h-7 w-7 items-center justify-center rounded bg-carimbo font-mono text-xs font-semibold text-on-carimbo"
          aria-label="FiscalAPI — início"
        >
          F
        </NavLink>
      </div>

      <ul className="flex flex-1 flex-row items-center justify-around gap-0.5 px-1 sm:flex-col sm:justify-start sm:px-0 sm:py-2">
        {NAV.map((item) => (
          <li key={item.to} className="sm:w-full">
            <NavLink
              to={item.to}
              end={'fim' in item ? item.fim : undefined}
              className={({ isActive }) =>
                cn(
                  'group relative flex h-11 w-full items-center justify-center transition-colors',
                  'focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-carimbo',
                  isActive
                    ? 'text-carimbo'
                    : 'text-ink-faint hover:bg-surface-sunken hover:text-ink',
                )
              }
            >
              {({ isActive }) => (
                <>
                  {/* Marca do item ativo: barra à esquerda no desktop,
                      embaixo no mobile. */}
                  <span
                    className={cn(
                      'absolute rounded',
                      'bottom-0 left-1/2 h-0.5 w-5 -translate-x-1/2',
                      'sm:top-1/2 sm:bottom-auto sm:left-0 sm:h-5 sm:w-0.5 sm:translate-x-0 sm:-translate-y-1/2 sm:rounded-r',
                      isActive ? 'bg-carimbo' : 'bg-transparent',
                    )}
                    aria-hidden
                  />
                  <item.icone className="h-4 w-4" aria-hidden />
                  <span className="sr-only">{item.label}</span>
                  {/* Rótulo no hover — só no desktop, onde há espaço. */}
                  <span
                    className="pointer-events-none absolute left-full z-50 ml-1 hidden whitespace-nowrap rounded border border-line bg-surface px-2 py-1 text-[11px] text-ink shadow-overlay sm:group-hover:block"
                    aria-hidden
                  >
                    {item.label}
                  </span>
                </>
              )}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}