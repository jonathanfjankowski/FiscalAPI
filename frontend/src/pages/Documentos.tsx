import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../lib/api'
import { Badge, Card, PageHeader, PageLoading, Select, Table, Td, Th } from '../components/ui'
import { formatarData, STATUS_COLOR, STATUS_LABEL, STATUS_TRANSITORIOS } from '../lib/utils'

const STATUSES = Object.keys(STATUS_LABEL)

export default function Documentos() {
  const [params, setParams] = useSearchParams()
  // ?page=abc não pode virar NaN (quebrava a request).
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const pageSize = 50

  const filtros = {
    tenantId: params.get('tenantId') ?? '',
    status: params.get('status') ?? '',
    modelo: params.get('modelo') ?? '',
    de: params.get('de') ?? '',
    ate: params.get('ate') ?? '',
  }

  const { data: tenants } = useQuery({ queryKey: ['tenants'], queryFn: api.listTenants })
  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['documentos', filtros, page],
    queryFn: () =>
      api.listDocumentos({
        tenantId: filtros.tenantId || undefined,
        status: filtros.status || undefined,
        modelo: filtros.modelo || undefined,
        // Ambas interpretadas no fuso local ('de' vinha como UTC e 'ate' como
        // local — em UTC-3 o filtro "de" pegava docs do dia anterior).
        de: filtros.de ? new Date(filtros.de + 'T00:00:00').toISOString() : undefined,
        ate: filtros.ate ? new Date(filtros.ate + 'T23:59:59').toISOString() : undefined,
        page,
        pageSize,
      }),
    // Auto-refresh enquanto houver documento em trânsito na fila.
    refetchInterval: (query) =>
      query.state.data?.itens.some((d) => STATUS_TRANSITORIOS.includes(d.status)) ? 5000 : false,
  })

  const setFiltro = (chave: string, valor: string) => {
    const next = new URLSearchParams(params)
    if (valor) next.set(chave, valor)
    else next.delete(chave)
    // Trocar filtro volta pra página 1 — mas trocar a PÁGINA (chave 'page')
    // não pode apagar ela mesma (bug: era impossível sair da página 1).
    if (chave !== 'page') next.delete('page')
    setParams(next)
  }

  const totalPaginas = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1

  return (
    <>
      <PageHeader
        titulo="Documentos fiscais"
        descricao="Todos os documentos de todas as empresas"
        acoes={
          isFetching && !isLoading ? (
            <span className="text-[11px] text-zinc-500">atualizando…</span>
          ) : undefined
        }
      />

      <Card className="mb-4 p-4">
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
          <Select value={filtros.tenantId} onChange={(e) => setFiltro('tenantId', e.target.value)}>
            <option value="">Todas as empresas</option>
            {(tenants ?? []).map((t) => (
              <option key={t.id} value={t.id}>
                {t.razaoSocial}
              </option>
            ))}
          </Select>
          <Select value={filtros.status} onChange={(e) => setFiltro('status', e.target.value)}>
            <option value="">Todos os status</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {STATUS_LABEL[s]}
              </option>
            ))}
          </Select>
          <Select value={filtros.modelo} onChange={(e) => setFiltro('modelo', e.target.value)}>
            <option value="">Modelos 55 e 65</option>
            <option value="55">NF-e (55)</option>
            <option value="65">NFC-e (65)</option>
          </Select>
          <input
            type="date"
            value={filtros.de}
            onChange={(e) => setFiltro('de', e.target.value)}
            className="rounded-md border border-zinc-300 bg-white px-3 py-1.5 text-sm text-zinc-800 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-200"
          />
          <input
            type="date"
            value={filtros.ate}
            onChange={(e) => setFiltro('ate', e.target.value)}
            className="rounded-md border border-zinc-300 bg-white px-3 py-1.5 text-sm text-zinc-800 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-200"
          />
        </div>
      </Card>

      <Card>
        {isLoading ? (
          <PageLoading />
        ) : !data || data.itens.length === 0 ? (
          <p className="py-12 text-center text-xs text-zinc-500">
            Nenhum documento com os filtros atuais. Emita um pelo Playground.
          </p>
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>Número</Th>
                  <Th>Tipo</Th>
                  <Th>Empresa</Th>
                  <Th>Status</Th>
                  <Th>Chave de acesso</Th>
                  <Th>Ambiente</Th>
                  <Th>Criado em</Th>
                </tr>
              </thead>
              <tbody>
                {data.itens.map((d) => (
                  <tr key={d.id} className="transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/30">
                    <Td>
                      <Link
                        to={`/documentos/${d.id}`}
                        className="font-mono text-xs font-medium text-blue-700 hover:text-blue-600 dark:text-blue-300 dark:hover:text-blue-200"
                      >
                        {d.serie !== null ? `${d.serie}/${String(d.numero ?? 0).padStart(9, '0')}` : '—'}
                      </Link>
                    </Td>
                    <Td>
                      <Badge
                        className={
                          d.tipo === 'NFE'
                            ? 'border-indigo-200 bg-indigo-50 text-indigo-700 dark:border-indigo-500/30 dark:bg-indigo-500/10 dark:text-indigo-300'
                            : 'border-violet-200 bg-violet-50 text-violet-700 dark:border-violet-500/30 dark:bg-violet-500/10 dark:text-violet-300'
                        }
                      >
                        {d.tipo === 'NFE' ? 'NF-e 55' : d.tipo === 'NFCE' ? 'NFC-e 65' : d.tipo}
                      </Badge>
                    </Td>
                    <Td className="max-w-48 truncate text-xs">{d.tenantRazaoSocial}</Td>
                    <Td>
                      <Badge className={STATUS_COLOR[d.status]}>{STATUS_LABEL[d.status] ?? d.status}</Badge>
                      {d.motivoStatus && d.status === 'REJEITADA' && (
                        <p className="mt-0.5 max-w-64 truncate text-[11px] text-red-500/90 dark:text-red-400/80" title={d.motivoStatus}>
                          {d.motivoStatus}
                        </p>
                      )}
                    </Td>
                    <Td className="font-mono text-[11px] text-zinc-500">{d.chaveAcesso ?? '—'}</Td>
                    <Td>
                      <Badge
                        className={
                          d.ambiente === 'producao'
                            ? 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300'
                            : 'border-sky-200 bg-sky-50 text-sky-700 dark:border-sky-500/30 dark:bg-sky-500/10 dark:text-sky-300'
                        }
                      >
                        {d.ambiente}
                      </Badge>
                    </Td>
                    <Td className="text-xs text-zinc-500">{formatarData(d.criadoEm)}</Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <div className="flex items-center justify-between px-4 py-3 text-xs text-zinc-500">
              <span>
                {data.total} documento(s) · página {page} de {totalPaginas}
              </span>
              <div className="flex gap-2">
                <button
                  disabled={page <= 1}
                  onClick={() => setFiltro('page', String(page - 1))}
                  className="rounded-md border border-zinc-300 px-2.5 py-1 disabled:opacity-40 hover:enabled:border-zinc-400 dark:border-zinc-700 dark:hover:enabled:border-zinc-500"
                >
                  Anterior
                </button>
                <button
                  disabled={page >= totalPaginas}
                  onClick={() => setFiltro('page', String(page + 1))}
                  className="rounded-md border border-zinc-300 px-2.5 py-1 disabled:opacity-40 hover:enabled:border-zinc-400 dark:border-zinc-700 dark:hover:enabled:border-zinc-500"
                >
                  Próxima
                </button>
              </div>
            </div>
          </>
        )}
      </Card>
    </>
  )
}
