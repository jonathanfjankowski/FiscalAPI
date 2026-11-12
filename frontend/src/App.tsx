import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { useAuth } from './hooks/useAuth'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Empresas from './pages/Empresas'
import EmpresaDetalhe from './pages/EmpresaDetalhe'
import Documentos from './pages/Documentos'
import DocumentoDetalhe from './pages/DocumentoDetalhe'
import Playground from './pages/Playground'
import Configuracoes from './pages/Configuracoes'
import NotasRecebidas from './pages/NotasRecebidas'
import Auditoria from './pages/Auditoria'
import type { ReactNode } from 'react'

function RequireAuth({ children }: { children: ReactNode }) {
  const { auth } = useAuth()
  if (!auth) return <Navigate to="/login" replace />
  return <>{children}</>
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
        <Route path="/" element={<Dashboard />} />
        <Route path="/empresas" element={<Empresas />} />
        <Route path="/empresas/:id" element={<EmpresaDetalhe />} />
        {/* Alias mantido: links antigos usavam /tenants. */}
        <Route path="/tenants" element={<Empresas />} />
        <Route path="/tenants/:id" element={<EmpresaDetalhe />} />
        <Route path="/documentos" element={<Documentos />} />
        <Route path="/documentos/:id" element={<DocumentoDetalhe />} />
        <Route path="/notas-recebidas" element={<NotasRecebidas />} />
        <Route path="/auditoria" element={<Auditoria />} />
        <Route path="/playground" element={<Playground />} />
        <Route path="/configuracoes" element={<Configuracoes />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}