import { Outlet } from 'react-router-dom'
import { TooltipProvider } from '../ui'
import { useSidebar } from '../../hooks/useSidebar'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'

// ==========================================================================
// Shell do painel.
//
// Rail fixo à esquerda + barra de comando + conteúdo. O conteúdo usa TODA a
// largura disponível: um console de operação é lido em monitor largo, e
// limitar a 88rem deixava uma faixa vazia à direita enquanto a tabela de
// documentos rolava na horizontal.
//
// A largura do rail vem por variável CSS para que o padding só valha a
// partir de `sm` — no mobile a navegação é inferior e não há o que reservar.
// ==========================================================================

export function AppShell() {
  const { largura } = useSidebar()

  return (
    <TooltipProvider>
      <div
        className="min-h-screen bg-paper"
        style={{ '--largura-rail': `${largura}px` } as React.CSSProperties}
      >
        {/* Skip link: quem navega por teclado não precisa passar pelos 8
            itens do rail em toda página. */}
        <a
          href="#conteudo"
          className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-100 focus:rounded focus:bg-carimbo focus:px-3 focus:py-2 focus:text-[13px] focus:font-medium focus:text-on-carimbo"
        >
          Ir para o conteúdo
        </a>
        <Sidebar />
        {/* pb-16 no mobile abre espaço para a barra inferior. */}
        <div className="pb-16 transition-[padding] duration-200 sm:pb-0 sm:pl-[var(--largura-rail)]">
          <Topbar />
          <main id="conteudo" tabIndex={-1} className="px-4 py-5 focus:outline-none sm:px-6 sm:py-6">
            <Outlet />
          </main>
        </div>
      </div>
    </TooltipProvider>
  )
}