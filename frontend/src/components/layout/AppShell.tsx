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
        {/* Skip link: quem navega por teclado não precisa passar pelos 7
            itens do rail em toda página. */}
        <a
          href="#conteudo"
          className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-100 focus:rounded focus:bg-carimbo focus:px-3 focus:py-2 focus:text-[13px] focus:font-medium focus:text-on-carimbo"
        >
          Ir para o conteúdo
        </a>
        <Sidebar />
        {/* pb-16 no mobile abre espaço para a barra de navegação inferior. */}
        <div className="pb-16 sm:pb-0 sm:pl-15">
          <Topbar />
          <main id="conteudo" tabIndex={-1} className="mx-auto max-w-[88rem] px-4 py-5 focus:outline-none sm:px-6 sm:py-6">
            <Outlet />
          </main>
        </div>
      </div>
    </TooltipProvider>
  )
}