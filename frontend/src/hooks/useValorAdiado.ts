import { useEffect, useState } from 'react'

/**
 * Atrasa a propagação de um valor que muda a cada tecla.
 *
 * Sem isso, cada caractere digitado na busca dispara uma requisição — "padaria"
 * são 7 chamadas, e as 6 primeiras são descartadas pelo React Query. O atraso
 * de 300ms é curto o suficiente para não parecer lag e longo o suficiente para
 * agrupar a digitação.
 */
export function useValorAdiado<T>(valor: T, atrasoMs = 300): T {
  const [adiado, setAdiado] = useState(valor)

  useEffect(() => {
    const t = setTimeout(() => setAdiado(valor), atrasoMs)
    return () => clearTimeout(t)
  }, [valor, atrasoMs])

  return adiado
}
