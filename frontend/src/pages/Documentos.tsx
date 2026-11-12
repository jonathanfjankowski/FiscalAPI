import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { ArrowDownUp, Check, Copy, Search, X } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useEmpresaContexto } from '../hooks/useEmpresaContexto'
import { useFiltrosUrl } from '../hooks/useFiltrosUrl'
import { useValorAdiado } from '../hooks/useValorAdiado'
import { STATUS_LABEL, STATUS_TRANSITORIOS } from '../lib/fiscal'
import { copiar, formatarCnpj, formatarData } from '../lib/format'
import { cn } from '../lib/utils'
import {
  AmbienteBadge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  PageLoading,
  Select,
  StatusDot,
  Table,
  Td,
  Th,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { Paginacao } from '../components/ui/paginacao'
import { ChaveAcesso } from '../components/fiscal'

// ==========================================================================
// Documentos fiscais.
//
// Tudo na URL: filtro, busca, ordenação, página. O contexto de empresa vem
// do topo (?empresa=) e é o mesmo das outras telas.
//
// A distinção erro × vazio é o ponto que a versão anterior errava: quando a
// API caía, a tela dizia "nenhum documento com os filtros atuais" — o
// operador acreditava ter filtrado errado, quando a API estava fora.
// ==========================================================================

const CHAVES_FILTRO = ['status', 'modelo', 'de', 'ate', 'q', 'ordenarPor', 'ordem', 'pageSize'] as const

const STATUS_OPCOES = Object.keys(STATUS_LABEL)

/**
 * Selo do modelo. O código fiscal (55/65/115) vai em mono ao lado do nome:
 * é o que o operador e o contador usam para distinguir o documento, e some
 * se ficar só o nome por extenso.
 */
function SeloModelo({ tipo }: { tipo: string }) {
  const par =
    tipo === 'NFE' ? ['NF-e', '55']
    : tipo === 'NFCE' ? ['NFC-e', '65']
    : tipo === 'NFSE' ? ['NFS-e', '115']
    : [tipo, '']

  return (
    <span className="inline-flex shrink-0 items-center gap-1 rounded border border-line-strong bg-surface-sunken px-1.5 py-0.5 text-[10px] font-medium text-ink-muted">
      {par[0]}
      {par[1] && <span className="font-mono text-ink-faint tabular">{par[1]}</span>}
    </span>
  )
}

export default function Documentos() {
  const navigate = useNavigate()
  const { filtroTenant, definirEmpresa } = useEmpresaContexto()
  const [copiado, setCopiado] = useState<string | null>(null)

  // CNPJ/UF do emitente na linha: a razão social sozinha não distingue
  // matriz de filial. Vem do cache de ['tenants'] que o seletor do topo já
  // mantém — sem requisição extra.
  const { data: tenants } = useQuery({ queryKey: ['tenants'], queryFn: api.listTenants, staleTime: 60_000 })
  const porId = useMemo(() => new Map((tenants ?? []).map((t) => [t.id, t])), [tenants])
  const { valores, pagina, definir, definirPagina, limpar, algumAtivo } = useFiltrosUrl(CHAVES_FILTRO)

  const pageSize = Number.parseInt(valores.pageSize || '50', 10) || 50
  // Busca com atraso: sem isso cada tecla dispara uma requisição.
  const buscaAdiada = useValorAdiado(valores.q)
  const ordenarPor = valores.ordenarPor || 'criadoEm'
  const ordem = (valores.ordem || 'desc') as 'asc' | 'desc'

  const filtros = useMemo(
    () => ({
      tenantId: filtroTenant,
      status: valores.status || undefined,
      modelo: valores.modelo || undefined,
      // O fuso do navegador: 'de' vinha como UTC e pegava o dia anterior em
      // UTC-3 — monta com hora local explícita.
      de: valores.de ? new Date(valores.de + 'T00:00:00').toISOString() : undefined,
      ate: valores.ate ? new Date(valores.ate + 'T23:59:59').toISOString() : undefined,
      q: buscaAdiada || undefined,
      ordenarPor,
      ordem,
      page: pagina,
      pageSize,
    }),
    [filtroTenant, valores, buscaAdiada, ordenarPor, ordem, pagina, pageSize],
  )

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['documentos', filtros],
    queryFn: () => api.listDocumentos(filtros),
    // Só auto-atualiza enquanto houver documento em trânsito — pollar uma
    // lista toda terminal é desperdício.
    refetchInterval: (query) =>
      query.state.data?.itens.some((d) => STATUS_TRANSITORIOS.includes(d.status)) ? 5000 : false,
  })

  const totalPaginas = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1

  return (
    <>
      <PageHeader
        titulo="Documentos fiscais"
        descricao="NF-e, NFC-e e NFS-e de todas as empresas — o contexto do topo filtra."
        acoes={
          isFetching && !isLoading ? (
            <span className="text-[11px] text-ink-faint">atualizando…</span>
          ) : undefined
        }
      />

      {/* ---------- Filtros ---------- */}
      <div className="mb-4 rounded-lg border border-line bg-surface p-3">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-56 flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 h-3.5 w-3.5 -translate-y-1/2 text-ink-faint" aria-hidden />
            <Input
              value={valores.q}
              onChange={(e) => definir('q', e.target.value)}
              placeholder="Chave de acesso, número ou empresa…"
              aria-label="Buscar documentos"
              className="h-8 pl-8"
            />
          </div>

          <Select
            value={valores.status}
            onChange={(e) => definir('status', e.target.value)}
            aria-label="Filtrar por status"
            className="h-8 w-44"
          >
            <option value="">Todos os status</option>
            {STATUS_OPCOES.map((s) => (
              <option key={s} value={s}>
                {STATUS_LABEL[s]}
              </option>
            ))}
          </Select>

          <Select
            value={valores.modelo}
            onChange={(e) => definir('modelo', e.target.value)}
            aria-label="Filtrar por modelo"
            className="h-8 w-36"
          >
            <option value="">Todos os modelos</option>
            <option value="55">NF-e (55)</option>
            <option value="65">NFC-e (65)</option>
            <option value="115">NFS-e (115)</option>
          </Select>

          <div className="flex items-center gap-1.5">
            <Input
              type="date"
              value={valores.de}
              onChange={(e) => definir('de', e.target.value)}
              aria-label="Data inicial"
              className="h-8 w-36"
            />
            <span className="text-xs text-ink-faint">até</span>
            <Input
              type="date"
              value={valores.ate}
              onChange={(e) => definir('ate', e.target.value)}
              aria-label="Data final"
              className="h-8 w-36"
            />
          </div>

          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              // Alterna direção; campo padrão é criadoEm.
              definir('ordem', ordem === 'desc' ? 'asc' : 'desc')
            }}
            title={`Ordenar por data de criação (${ordem === 'desc' ? 'mais recente' : 'mais antigo'} primeiro)`}
          >
            <ArrowDownUp className="h-3.5 w-3.5" aria-hidden />
            {ordem === 'desc' ? 'Recentes' : 'Antigos'}
          </Button>

          {algumAtivo && (
            <Button variant="ghost" size="sm" onClick={limpar}>
              <X className="h-3.5 w-3.5" aria-hidden />
              Limpar
            </Button>
          )}
        </div>
      </div>

      {/* ---------- Lista ---------- */}
      <div className="rounded-lg border border-line bg-surface">
        {isLoading ? (
          <PageLoading mensagem="Carregando documentos…" />
        ) : isError ? (
          <div className="p-4">
            <ErrorState
              titulo="Não foi possível carregar os documentos"
              detalhe={
                error instanceof ApiError
                  ? (error.detail ?? error.message)
                  : 'Verifique se a API está rodando.'
              }
              aoTentarNovamente={() => refetch()}
            />
          </div>
        ) : !data || data.itens.length === 0 ? (
          <EmptyState
            titulo={algumAtivo || filtroTenant ? 'Nenhum documento com estes filtros' : 'Nenhum documento emitido'}
            descricao={
              algumAtivo || filtroTenant
                ? 'Ajuste os filtros ou limpe para ver todos.'
                : 'Emita um pelo Playground para começar.'
            }
            acao={
              algumAtivo ? (
                <Button onClick={limpar}>Limpar filtros</Button>
              ) : (
                <Button asChild variant="primary">
                  <Link to="/playground">Abrir Playground</Link>
                </Button>
              )
            }
          />
        ) : (
          <>
            <Table label="Documentos fiscais">
              <thead>
                <tr>
                  <Th className="w-64">Documento</Th>
                  <Th className="min-w-56">Empresa</Th>
                  <Th className="w-44">Situação</Th>
                  <Th className="w-40">Chave</Th>
                  <Th className="w-32">Ambiente</Th>
                  <Th className="w-36">Criado em</Th>
                </tr>
              </thead>
              <tbody>
                {data.itens.map((d) => {
                  const numero =
                    d.serie !== null ? `${d.serie}/${String(d.numero ?? 0).padStart(9, '0')}` : null
                  const emitente = porId.get(d.tenantId)

                  return (
                  <tr
                    key={d.id}
                    // A linha inteira abre o documento: o alvo grande é mais
                    // rápido de acertar que o link de 12px do número.
                    onClick={() => navigate(`/documentos/${d.id}`)}
                    className="group cursor-pointer transition-colors hover:bg-surface-sunken/60"
                  >
                    <Td>
                      <div className="flex items-center gap-2">
                        {numero ? (
                          <Link
                            to={`/documentos/${d.id}`}
                            onClick={(e) => e.stopPropagation()}
                            className="font-mono text-[15px] leading-none font-semibold text-ink tabular transition-colors group-hover:text-carimbo hover:underline"
                            title={`Abrir ${numero}`}
                          >
                            {numero}
                          </Link>
                        ) : (
                          <span className="font-mono text-[15px] text-ink-faint">sem número</span>
                        )}
                        <SeloModelo tipo={d.tipo} />
                        {numero && (
                          <button
                            type="button"
                            onClick={async (e) => {
                              e.stopPropagation()
                              if (await copiar(numero)) {
                                setCopiado(d.id)
                                setTimeout(() => setCopiado((c) => (c === d.id ? null : c)), 1800)
                              }
                            }}
                            aria-label={`Copiar número ${numero}`}
                            title="Copiar número"
                            className="rounded p-1 text-ink-faint opacity-0 transition-opacity group-hover:opacity-100 hover:bg-surface hover:text-ink focus-visible:opacity-100"
                          >
                            {copiado === d.id ? (
                              <Check className="h-3 w-3 text-aprovado" aria-hidden />
                            ) : (
                              <Copy className="h-3 w-3" aria-hidden />
                            )}
                          </button>
                        )}
                      </div>
                    </Td>
                    <Td>
                      {/* Clicar na empresa troca o contexto do painel para ela —
                          é o caminho de um clique para "só os documentos desta". */}
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation()
                          definirEmpresa(d.tenantId)
                        }}
                        title={`Filtrar o painel por ${d.tenantRazaoSocial}`}
                        className="block max-w-full truncate text-left text-[13px] font-medium text-ink transition-colors hover:text-carimbo hover:underline"
                      >
                        {d.tenantRazaoSocial}
                      </button>
                      <p className="mt-0.5 flex items-center gap-1.5 font-mono text-[10px] text-ink-faint tabular">
                        {emitente && <span>{formatarCnpj(emitente.cnpj)}</span>}
                        {emitente && <span aria-hidden>·</span>}
                        {emitente ? <span>{emitente.uf}</span> : null}
                        {!emitente && <span className="font-sans">—</span>}
                      </p>
                    </Td>
                    <Td>
                      <StatusDot status={d.status} withLabel />
                      {d.motivoStatus && d.status === 'REJEITADA' && (
                        <p
                          className="mt-0.5 max-w-52 truncate text-[11px] text-rejeitado"
                          title={d.motivoStatus}
                        >
                          {d.motivoStatus}
                        </p>
                      )}
                    </Td>
                    <Td>
                      <ChaveAcesso chave={d.chaveAcesso} compacta />
                    </Td>
                    <Td>
                      <AmbienteBadge ambiente={d.ambiente} />
                    </Td>
                    <Td className="text-xs text-ink-muted tabular">{formatarData(d.criadoEm)}</Td>
                  </tr>
                  )
                })}
              </tbody>
            </Table>

            <div className="border-t border-line">
              <Paginacao
                total={data.total}
                pagina={pagina}
                pageSize={pageSize}
                aoMudarPagina={definirPagina}
                aoMudarTamanho={(n) => {
                  definir('pageSize', String(n))
                }}
              />
            </div>
          </>
        )}
      </div>

      <p className={cn('mt-3 text-[11px] text-ink-faint', !data?.itens.length && 'hidden')}>
        {data?.itens.some((d) => STATUS_TRANSITORIOS.includes(d.status))
          ? 'Há documentos em trânsito — a lista se atualiza a cada 5s.'
          : `Página ${pagina} de ${totalPaginas}.`}
      </p>
    </>
  )
}