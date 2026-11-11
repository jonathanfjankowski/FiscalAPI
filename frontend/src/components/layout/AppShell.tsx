import { Outlet } from 'react-router-dom'
import { TooltipProvider } from '../ui'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'

// ==========================================================================
// Shell do painel.
//
// Rail fixo à esquerda + barra de comando + conteúdo. O conteúdo é separado
// por hairlines, não por cards flutuantes: num console denso, a moldura
// compete com o dado.
// ==========================================================================

export function AppShell() {
  return (
    <TooltipProvider>
      <div className="min-h-screen bg-paper">
        <Sidebar />
        <div className="pl-15">
          <Topbar />
          <main id="conteudo" className="mx-auto max-w-[88rem] px-6 py-6">
            <Outlet />
          </main>
        </div>
      </div>
    </TooltipProvider>
  )
}