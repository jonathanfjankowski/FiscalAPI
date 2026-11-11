import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useEmpresaContexto } from '../../hooks/useEmpresaContexto'
import { Tooltip } from '../ui'
import { cn } from '../../lib/utils'

// ==========================================================================
// Indicador do serviço SEFAZ.
//
// "A SEFAZ do PR está em manutenção" é a informação que decide se o
// operador investiga uma rejeição ou espera. Não existia no painel: o
// status-serviço só era acessível por API key.
//
// Sem empresa selecionada não há UF para consultar — o indicador some em vez
// de mostrar um dado inventado.
// ==========================================================================

export function StatusSefaz() {
  const { empresa } = useEmpresaContexto()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['status-servico', empresa?.id],
    queryFn: () => api.statusServico(empresa!.id),
    enabled: !!empresa,
    // Cache de 60s no backend; aqui 30s evita martelar a SEFAZ.
    staleTime: 30_000,
    retry: false,
  })

  if (!empresa) return null

  if (isLoading) {
    return (
      <span className="hidden items-center gap-1.5 text-[11px] text-ink-faint sm:flex">
        <span className="h-1.5 w-1.5 rounded-full bg-ink-faint" aria-hidden />
        SEFAZ…
      </span>
    )
  }

  if (isError || !data) {
    return (
      <Tooltip content="Não foi possível consultar o status do serviço.">
        <span className="hidden items-center gap-1.5 text-[11px] text-ink-faint sm:flex">
          <span className="h-1.5 w-1.5 rounded-full bg-ink-faint" aria-hidden />
          SEFAZ ?
        </span>
      </Tooltip>
    )
  }

  // 107 = serviço em operação; 108 = paralisado momentaneamente;
  // 109 = paralisado sem previsão.
  const ok = data.cStat === 107
  const alerta = data.cStat === 108

  return (
    <Tooltip content={`${data.xMotivo} · ${empresa.uf} · cStat ${data.cStat}`}>
      <span
        className={cn(
          'hidden items-center gap-1.5 text-[11px] sm:flex',
          ok ? 'text-ink-muted' : alerta ? 'text-contingencia' : 'text-rejeitado',
        )}
      >
        <span
          className={cn(
            'h-1.5 w-1.5 rounded-full',
            ok ? 'bg-aprovado' : alerta ? 'bg-contingencia' : 'bg-rejeitado',
          )}
          aria-hidden
        />
        SEFAZ {empresa.uf}
        <span aria-hidden>{ok ? 'ok' : alerta ? 'instável' : 'fora'}</span>
        <span className="sr-only">
          {ok ? 'em operação' : alerta ? 'paralisado momentaneamente' : 'paralisado'}
        </span>
      </span>
    </Tooltip>
  )
}