import { useSyncExternalStore } from 'react'
import {
  alternarGravacao,
  estaGravando,
  inscrever,
  limparRegistros,
  obterRegistros,
  type RegistroTrafego,
} from '../lib/trafego'

// ==========================================================================
// Acesso React ao registro de tráfego.
//
// `useSyncExternalStore` é o jeito certo de ler um store externo: o snapshot
// é estável entre renders e o React não entra em loop de re-render (que
// aconteceria com um useState + subscribe manual).
// ==========================================================================

// Snapshot do flag de gravação: primitivo, estável por valor.
function obterGravando() {
  return estaGravando()
}

export function useTrafego(): {
  registros: RegistroTrafego[]
  gravando: boolean
  alternar: () => void
  limpar: () => void
} {
  const registros = useSyncExternalStore(inscrever, obterRegistros, obterRegistros)
  const gravando = useSyncExternalStore(inscrever, obterGravando, obterGravando)

  return { registros, gravando, alternar: alternarGravacao, limpar: limparRegistros }
}
