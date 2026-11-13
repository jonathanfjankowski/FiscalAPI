import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'

// ==========================================================================
// Filtros na URL.
//
// Toda a lista vive na query string — filtro, ordenação, página. Isso faz o
// estado ser compartilhável (mandar "as rejeitadas da Acme" é mandar a URL),
// sobreviver a F5 e voltar corretamente com o botão do navegador.
//
// Trocar qualquer filtro reseta a página: a página 3 de "todas" não faz
// sentido depois de filtrar por empresa.
// ==========================================================================

export function useFiltrosUrl<const T extends string>(chaves: readonly T[]) {
  const [params, setParams] = useSearchParams()

  const valores = useMemo(() => {
    const v = {} as Record<T, string>
    for (const chave of chaves) v[chave] = params.get(chave) ?? ''
    return v
  }, [params, chaves])

  const pagina = useMemo(() => {
    const bruto = Number.parseInt(params.get('page') ?? '1', 10)
    return Number.isFinite(bruto) && bruto > 0 ? bruto : 1
  }, [params])

  const definir = useCallback(
    (chave: T, valor: string) => {
      setParams(
        (anterior) => {
          const proximo = new URLSearchParams(anterior)
          if (valor) proximo.set(chave, valor)
          else proximo.delete(chave)
          // Trocar filtro volta pra página 1 — mas trocar a própria página não.
          if (chave !== ('page' as T)) proximo.delete('page')
          return proximo
        },
        { replace: true },
      )
    },
    [setParams],
  )

  const definirPagina = useCallback(
    (n: number) => {
      setParams(
        (anterior) => {
          const proximo = new URLSearchParams(anterior)
          if (n > 1) proximo.set('page', String(n))
          else proximo.delete('page')
          return proximo
        },
        { replace: true },
      )
    },
    [setParams],
  )

  const limpar = useCallback(() => {
    setParams(
      (anterior) => {
        const proximo = new URLSearchParams(anterior)
        for (const chave of chaves) proximo.delete(chave)
        proximo.delete('page')
        return proximo
      },
      { replace: true },
    )
  }, [setParams, chaves])

  const algumAtivo = useMemo(
    () => chaves.some((chave) => (valores[chave] ?? '') !== ''),
    [chaves, valores],
  )

  return { valores, pagina, definir, definirPagina, limpar, algumAtivo }
}
