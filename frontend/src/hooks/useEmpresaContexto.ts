import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../lib/api'
import type { Tenant } from '../lib/types'

// ==========================================================================
// Contexto de empresa.
//
// Toda operação do painel é por empresa, mas o app shell é global. Em vez de
// um estado solto em memória (que some no F5) ou de um dropdown por página
// (que não conversa com as outras), o contexto vive na URL: `?empresa=<id>`.
//
// Isso torna o contexto deep-linkável — mandar "os documentos da Acme" para
// um colega é mandar a URL — e sobrevive a recarregar a página.
// ==========================================================================

const PARAM = 'empresa'

export interface EmpresaContexto {
  /** Id da empresa selecionada, ou null quando o escopo é "todas". */
  empresaId: string | null
  /** A empresa resolvida, quando há id e ela já carregou. */
  empresa: Tenant | null
  /** Define a empresa no contexto (null = todas). */
  definirEmpresa: (id: string | null) => void
  /** Só o id — atalho para montar query de API. */
  filtroTenant: string | undefined
  carregando: boolean
}

/**
 * Lê e escreve o contexto de empresa na query string.
 *
 * Usa `replace` para não encher o histórico a cada troca de empresa: o botão
 * "voltar" deve levar à página anterior, não à empresa anterior.
 */
export function useEmpresaContexto(): EmpresaContexto {
  const [params, setParams] = useSearchParams()
  const empresaId = params.get(PARAM)

  const { data: empresas, isLoading } = useQuery({
    queryKey: ['tenants'],
    queryFn: api.listTenants,
    // O seletor do topo aparece em toda página; não precisa refazer a cada foco.
    staleTime: 60_000,
  })

  const empresa = useMemo(
    () => (empresaId ? (empresas?.find((t) => t.id === empresaId) ?? null) : null),
    [empresaId, empresas],
  )

  const definirEmpresa = useCallback(
    (id: string | null) => {
      setParams(
        (anterior) => {
          const proximo = new URLSearchParams(anterior)
          if (id) proximo.set(PARAM, id)
          else proximo.delete(PARAM)
          // Trocar de empresa reseta a paginação — a página 3 de outra
          // empresa não faz sentido no novo escopo.
          proximo.delete('page')
          return proximo
        },
        { replace: true },
      )
    },
    [setParams],
  )

  return {
    empresaId,
    empresa,
    definirEmpresa,
    filtroTenant: empresaId ?? undefined,
    carregando: isLoading,
  }
}