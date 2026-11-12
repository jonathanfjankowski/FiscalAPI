import { useCallback, useEffect, useState } from 'react'

// ==========================================================================
// Estado da barra lateral (recolhida / expandida).
//
// Vive num store de módulo porque dois componentes precisam do mesmo valor
// — a Sidebar desenha e o AppShell reserva o espaço do conteúdo. Prop
// drilling funcionaria, mas o AppShell teria que repassar para o Topbar só
// para o botão de alternar; um store pequeno é mais direto.
//
// A preferência persiste no localStorage: o operador que trabalha o dia
// inteiro com os rótulos visíveis não precisa reexpandir a cada F5.
// ==========================================================================

const CHAVE = 'fiscal.sidebar.expandida'

let expandida = ler()
const ouvintes = new Set<() => void>()

function ler(): boolean {
  try {
    return localStorage.getItem(CHAVE) === '1'
  } catch {
    return false
  }
}

function notificar() {
  for (const fn of ouvintes) fn()
}

export function inscreverSidebar(fn: () => void): () => void {
  ouvintes.add(fn)
  return () => ouvintes.delete(fn)
}

export function obterExpandida(): boolean {
  return expandida
}

export function alternarSidebar(): void {
  expandida = !expandida
  try {
    localStorage.setItem(CHAVE, expandida ? '1' : '0')
  } catch {
    // localStorage indisponível (modo privado): segue só em memória.
  }
  notificar()
}

/** Largura em px — usada no padding do conteúdo. */
export const LARGURA_RECOLHIDA = 60
export const LARGURA_EXPANDIDA = 232

export function useSidebar() {
  const [valor, setValor] = useState(expandida)

  useEffect(() => {
    const aoMudar = () => setValor(expandida)
    return inscreverSidebar(aoMudar)
  }, [])

  return {
    expandida: valor,
    alternar: useCallback(() => alternarSidebar(), []),
    largura: valor ? LARGURA_EXPANDIDA : LARGURA_RECOLHIDA,
  }
}
