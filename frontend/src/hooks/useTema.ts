import { useCallback, useEffect, useState } from 'react'

const TEMA_KEY = 'fiscal.tema'

export type Tema = 'claro' | 'escuro'

function temaInicial(): Tema {
  const salvo = localStorage.getItem(TEMA_KEY)
  if (salvo === 'claro' || salvo === 'escuro') return salvo
  // Sem preferência salva: segue o sistema.
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro'
}

export function useTema() {
  const [tema, setTema] = useState<Tema>(temaInicial)

  useEffect(() => {
    document.documentElement.classList.toggle('dark', tema === 'escuro')
    localStorage.setItem(TEMA_KEY, tema)
  }, [tema])

  const alternar = useCallback(() => setTema((t) => (t === 'escuro' ? 'claro' : 'escuro')), [])

  return { tema, alternar }
}
