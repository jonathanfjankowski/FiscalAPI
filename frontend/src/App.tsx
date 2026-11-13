import { lazy, Suspense, type ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { PageLoading } from './components/ui'
import { useAuth } from './lib/authContext'
import Login from './pages/Login'

// ==========================================================================
// Rotas.
//
// Login entra no bundle inicial (é a primeira tela); o resto é lazy. O
// painel tem XmlViewer, PDF inline, Playground e inspetor de tráfego — nada
// disso precisa ser baixado por quem só vai ver a visão geral.
// ==========================================================================

const VisaoGeral = lazy(() => import('./pages/VisaoGeral'))
const Documentos = lazy(() => import('./pages/Documentos'))
const DocumentoDetalhe = lazy(() => import('./pages/DocumentoDetalhe'))
const Empresas = lazy(() => import('./pages/Empresas'))
const EmpresaDetalhe = lazy(() => import('./pages/EmpresaDetalhe'))
const NotasRecebidas = lazy(() => import('./pages/NotasRecebidas'))
const Auditoria = lazy(() => import('./pages/Auditoria'))
const Trafego = lazy(() => import('./pages/Trafego'))
const Playground = lazy(() => import('./pages/Playground'))
const Configuracoes = lazy(() => import('./pages/Configuracoes'))

function RequireAuth({ children }: { children: ReactNode }) {
  const { auth } = useAuth()
  if (!auth) return <Navigate to="/login" replace />
  return <>{children}</>
}

/** Envolve a página lazy — o shell já está na tela, só o conteúdo carrega. */
function Pagina({ children }: { children: ReactNode }) {
  return <Suspense fallback={<PageLoading />}>{children}</Suspense>
}

export default function App() {
  const { auth } = useAuth()

  return (
    <Routes>
      <Route path="/login" element={auth ? <Navigate to="/" replace /> : <Login />} />

      <Route
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route path="/" element={<Pagina><VisaoGeral /></Pagina>} />
        <Route path="/empresas" element={<Pagina><Empresas /></Pagina>} />
        <Route path="/empresas/:id" element={<Pagina><EmpresaDetalhe /></Pagina>} />
        {/* Alias mantido: links antigos usavam /tenants. */}
        <Route path="/tenants" element={<Pagina><Empresas /></Pagina>} />
        <Route path="/tenants/:id" element={<Pagina><EmpresaDetalhe /></Pagina>} />
        <Route path="/documentos" element={<Pagina><Documentos /></Pagina>} />
        <Route path="/documentos/:id" element={<Pagina><DocumentoDetalhe /></Pagina>} />
        <Route path="/notas-recebidas" element={<Pagina><NotasRecebidas /></Pagina>} />
        <Route path="/auditoria" element={<Pagina><Auditoria /></Pagina>} />
        <Route path="/trafego" element={<Pagina><Trafego /></Pagina>} />
        <Route path="/playground" element={<Pagina><Playground /></Pagina>} />
        <Route path="/configuracoes" element={<Pagina><Configuracoes /></Pagina>} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
