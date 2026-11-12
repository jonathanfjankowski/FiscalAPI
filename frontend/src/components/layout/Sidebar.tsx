import { NavLink } from 'react-router-dom'
import {
  Activity,
  Building2,
  FileText,
  FlaskConical,
  Inbox,
  LayoutDashboard,
  PanelLeftClose,
  PanelLeftOpen,
  ScrollText,
  Settings,
} from 'lucide-react'
import { cn } from '../../lib/utils'
import { useSidebar } from '../../hooks/useSidebar'
import { useTrafego } from '../../hooks/useTrafego'
import { Tooltip } from '../ui'

// ==========================================================================
// Navegação lateral.
//
// Recolhida: rail de 60px, ícone + rótulo no hover.
// Expandida: 232px com os rótulos sempre visíveis — quem está aprendendo o
// painel (ou tem 7 destinos parecidos) não precisa passar o mouse em cada um.
// A preferência persiste.
//
// No mobile vira barra inferior: um rail lateral comeria 60px de uma tela
// de 360px, e um drawer esconderia a navegação atrás de um clique.
// ==========================================================================

const NAV = [
  { to: '/', label: 'Visão geral', icone: LayoutDashboard, fim: true },
  { to: '/documentos', label: 'Documentos', icone: FileText },
  { to: '/notas-recebidas', label: 'Notas recebidas', icone: Inbox },
  { to: '/empresas', label: 'Empresas', icone: Building2 },
  { to: '/auditoria', label: 'Auditoria', icone: ScrollText },
  { to: '/trafego', label: 'Tráfego HTTP', icone: Activity },
  { to: '/playground', label: 'Playground', icone: FlaskConical },
  { to: '/configuracoes', label: 'Configurações', icone: Settings },
] as const

export function Sidebar() {
  const { expandida, alternar } = useSidebar()
  const { registros } = useTrafego()

  // Contador de falhas no destino Tráfego — o operador vê que algo quebrou
  // sem precisar abrir a tela.
  const falhas = registros.filter((r) => !r.ok).length

  return (
    <nav
      aria-label="Navegação principal"
      style={expandida ? { width: 232 } : undefined}
      className={cn(
        'fixed z-40 flex border-line bg-surface transition-[width] duration-200',
        'inset-x-0 bottom-0 h-14 flex-row border-t',
        'sm:inset-y-0 sm:left-0 sm:right-auto sm:h-auto sm:flex-col sm:border-t-0 sm:border-r',
        !expandida && 'sm:w-15',
      )}
    >
      <div className="hidden h-12 items-center gap-2.5 border-b border-line px-3.5 sm:flex">
        <NavLink
          to="/"
          className="flex h-7 w-7 shrink-0 items-center justify-center rounded bg-carimbo font-mono text-xs font-semibold text-on-carimbo"
          aria-label="FiscalAPI — início"
        >
          F
        </NavLink>
        {expandida && (
          <div className="min-w-0">
            <p className="truncate text-[13px] font-semibold tracking-tight text-ink">FiscalAPI</p>
            <p className="truncate text-[10px] text-ink-faint">Painel de operação</p>
          </div>
        )}
      </div>

      <ul className="flex flex-1 flex-row items-center justify-around gap-0.5 overflow-x-auto px-1 sm:flex-col sm:items-stretch sm:justify-start sm:gap-0.5 sm:overflow-x-visible sm:overflow-y-auto sm:px-2 sm:py-2">
        {NAV.map((item) => {
          const contador = item.to === '/trafego' && falhas > 0 ? falhas : undefined

          const conteudo = ({ isActive }: { isActive: boolean }) => (
            <>
              <span
                className={cn(
                  'absolute rounded',
                  'bottom-0 left-1/2 h-0.5 w-5 -translate-x-1/2',
                  'sm:top-1/2 sm:bottom-auto sm:left-0 sm:h-5 sm:w-0.5 sm:translate-x-0 sm:-translate-y-1/2 sm:rounded-r',
                  isActive ? 'bg-carimbo' : 'bg-transparent',
                )}
                aria-hidden
              />
              <span className="relative shrink-0">
                <item.icone className="h-4 w-4" aria-hidden />
                {contador !== undefined && (
                  <span
                    className="absolute -top-1.5 -right-1.5 flex h-3.5 min-w-3.5 items-center justify-center rounded-full bg-rejeitado px-1 text-[9px] font-semibold text-white tabular"
                    aria-hidden
                  >
                    {contador > 99 ? '99+' : contador}
                  </span>
                )}
              </span>

              {/* Expandida: rótulo sempre. Recolhida: só no hover (desktop). */}
              <span
                className={cn(
                  'truncate text-[13px]',
                  expandida
                    ? 'sm:block'
                    : 'sm:pointer-events-none sm:absolute sm:left-full sm:z-50 sm:ml-2 sm:hidden sm:rounded sm:border sm:border-line sm:bg-surface sm:px-2 sm:py-1 sm:text-[11px] sm:shadow-overlay sm:group-hover:block',
                  'hidden',
                )}
              >
                {item.label}
                {contador !== undefined && expandida && (
                  <span className="ml-1.5 text-[10px] text-rejeitado tabular">{contador}</span>
                )}
              </span>
              <span className="sr-only">
                {item.label}
                {contador !== undefined ? ` — ${contador} falha(s)` : ''}
              </span>
            </>
          )

          return (
            <li key={item.to} className="sm:w-full">
              <NavLink
                to={item.to}
                end={'fim' in item ? item.fim : undefined}
                className={({ isActive }) =>
                  cn(
                    'group relative flex h-11 items-center transition-colors',
                    expandida ? 'sm:gap-2.5 sm:px-2.5' : 'sm:justify-center',
                    'w-full justify-center',
                    'focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-carimbo',
                    expandida && 'sm:rounded',
                    isActive
                      ? cn('text-carimbo', expandida && 'sm:bg-carimbo-soft')
                      : 'text-ink-faint hover:bg-surface-sunken hover:text-ink',
                  )
                }
              >
                {conteudo}
              </NavLink>
            </li>
          )
        })}
      </ul>

      {/* Alternar expansão — só no desktop, onde há o que expandir. */}
      <div className="hidden border-t border-line p-2 sm:block">
        <Tooltip content={expandida ? undefined : 'Expandir menu'} side="right">
          <button
            type="button"
            onClick={alternar}
            aria-label={expandida ? 'Recolher menu' : 'Expandir menu'}
            aria-expanded={expandida}
            className={cn(
              'flex h-8 w-full items-center gap-2.5 rounded px-2.5 text-ink-faint transition-colors',
              'hover:bg-surface-sunken hover:text-ink',
              'focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-carimbo',
              !expandida && 'justify-center px-0',
            )}
          >
            {expandida ? (
              <PanelLeftClose className="h-4 w-4 shrink-0" aria-hidden />
            ) : (
              <PanelLeftOpen className="h-4 w-4 shrink-0" aria-hidden />
            )}
            {expandida && <span className="text-[12px]">Recolher</span>}
          </button>
        </Tooltip>
      </div>
    </nav>
  )
}