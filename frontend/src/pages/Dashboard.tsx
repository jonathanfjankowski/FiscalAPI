import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../lib/api'
import { PageLoading, PageHeader, StatCard, Card, Badge } from '../components/ui'
import { STATUS_LABEL } from '../lib/utils'

// Mapa estático — Tailwind v4 não gera classes construídas em runtime.
const BARRA_POR_STATUS: Record<string, string> = {
  PENDENTE: 'bg-sky-500/70',
  PROCESSANDO: 'bg-indigo-500/70',
  AUTORIZADA: 'bg-emerald-500/70',
  REJEITADA: 'bg-red-500/70',
  CONTINGENCIA: 'bg-amber-500/70',
  CANCELAMENTO_PENDENTE: 'bg-orange-500/70',
  CANCELADA: 'bg-zinc-400/70 dark:bg-zinc-500/70',
  ERRO_CANCELAMENTO: 'bg-red-500/70',
  DENEGADA: 'bg-fuchsia-500/70',
  ERRO_INTERNO: 'bg-red-500/70',
}

export default function Dashboard() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['dashboard'],
    queryFn: api.dashboard,
    refetchInterval: 15_000,
  })

  if (isLoading) return <PageLoading />
  if (isError || !data) return <p className="py-16 text-center text-sm text-zinc-500">Sem dados do dashboard.</p>

  const totalStatus = data.porStatus.reduce((acc, s) => acc + s.total, 0)

  return (
    <>
      <PageHeader
        titulo="Dashboard"
        descricao="Visão geral da plataforma — atualiza a cada 15s"
      />

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6">
        <StatCard titulo="Documentos hoje" valor={data.documentosHoje} />
        <StatCard titulo="Últimos 7 dias" valor={data.documentos7Dias} />
        <StatCard titulo="Em contingência" valor={data.emContingencia} alerta={data.emContingencia > 0} />
        <StatCard titulo="Empresas ativas" valor={data.tenantsAtivos} />
        <StatCard titulo="API keys ativas" valor={data.apiKeysAtivas} />
        <StatCard
          titulo="Certif. vencendo (30d)"
          valor={data.certificadosVencendo30Dias}
          alerta={data.certificadosVencendo30Dias > 0}
        />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Documentos por status</h2>
          {data.porStatus.length === 0 ? (
            <p className="py-8 text-center text-xs text-zinc-500">Nenhum documento emitido ainda.</p>
          ) : (
            <div className="mt-4 space-y-2.5">
              {data.porStatus.map((s) => {
                const pct = totalStatus > 0 ? Math.max((s.total / totalStatus) * 100, 2) : 0
                return (
                  <div key={s.status}>
                    <div className="mb-1 flex items-center justify-between text-xs">
                      <Link
                        to={`/documentos?status=${s.status}`}
                        className="text-zinc-600 hover:text-blue-600 dark:text-zinc-400 dark:hover:text-blue-300"
                      >
                        {STATUS_LABEL[s.status] ?? s.status}
                      </Link>
                      <span className="tabular-nums text-zinc-500">{s.total}</span>
                    </div>
                    <div className="h-1.5 overflow-hidden rounded-full bg-zinc-100 dark:bg-zinc-800">
                      <div
                        className={`h-full rounded-full ${BARRA_POR_STATUS[s.status] ?? 'bg-sky-500/70'}`}
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </Card>

        <div className="space-y-4">
          <Card className="p-5">
            <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Atalhos</h2>
            <div className="mt-3 grid grid-cols-2 gap-2">
              <Link
                to="/empresas"
                className="rounded-md border border-zinc-200 px-3 py-2.5 text-xs text-zinc-700 transition-colors hover:border-blue-500/40 hover:text-blue-600 dark:border-zinc-800 dark:text-zinc-300 dark:hover:border-blue-500/40 dark:hover:text-blue-300"
              >
                Gerenciar empresas e API keys
              </Link>
              <Link
                to="/playground"
                className="rounded-md border border-zinc-200 px-3 py-2.5 text-xs text-zinc-700 transition-colors hover:border-blue-500/40 hover:text-blue-600 dark:border-zinc-800 dark:text-zinc-300 dark:hover:border-blue-500/40 dark:hover:text-blue-300"
              >
                Emitir documento de teste
              </Link>
            </div>
          </Card>

          <Card className="p-5">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Fila / jobs</h2>
              <Badge>Hangfire</Badge>
            </div>
            <p className="mt-2 text-xs leading-relaxed text-zinc-500">
              O dashboard do Hangfire (fila de emissão, retries e varredura de contingência) fica
              disponível na própria API, autenticado com o token do painel:
            </p>
            <code className="mt-2 block rounded-md border border-zinc-200 bg-zinc-50 px-3 py-2 text-[11px] break-all text-zinc-500 dark:border-zinc-800 dark:bg-zinc-950 dark:text-zinc-400">
              /hangfire?access_token=&lt;JWT do painel&gt;
            </code>
          </Card>
        </div>
      </div>
    </>
  )
}
