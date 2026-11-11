import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ArrowDownUp, Search, X } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useEmpresaContexto } from '../hooks/useEmpresaContexto'
import { useFiltrosUrl } from '../hooks/useFiltrosUrl'
import { STATUS_LABEL, STATUS_TRANSITORIOS } from '../lib/fiscal'
import { formatarData } from '../lib/format'
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

/** Rótulo curto do modelo para a coluna. */
function rotuloModelo(tipo: string): string {
  if (tipo === 'NFE') return 'NF-e 55'
  if (tipo === 'NFCE') return 'NFC-e 65'
  if (tipo === 'NFSE') return 'NFS-e'
  return tipo
}

export default function Documentos() {
  const { filtroTenant } = useEmpresaContexto()
  const { valores, pagina, definir, definirPagina, limpar, algumAtivo } = useFiltrosUrl(CHAVES_FILTRO)

  const pageSize = Number.parseInt(valores.pageSize || '50', 10) || 50
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
      q: valores.q || undefined,
      ordenarPor,
      ordem,
      page: pagina,
      pageSize,
    }),
    [filtroTenant, valores, ordenarPor, ordem, pagina, pageSize],
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
                  <Th className="w-32">Número</Th>
                  <Th className="w-24">Modelo</Th>
                  <Th>Empresa</Th>
                  <Th className="w-44">Situação</Th>
                  <Th className="w-56">Chave</Th>
                  <Th className="w-32">Ambiente</Th>
                  <Th className="w-36">Criado em</Th>
                </tr>
              </thead>
              <tbody>
                {data.itens.map((d) => (
                  <tr key={d.id} className="transition-colors hover:bg-surface-sunken/60">
                    <Td>
                      <Link
                        to={`/documentos/${d.id}`}
                        className="font-mono text-xs font-medium text-carimbo tabular hover:underline"
                      >
                        {d.serie !== null
                          ? `${d.serie}/${String(d.numero ?? 0).padStart(9, '0')}`
                          : '—'}
                      </Link>
                    </Td>
                    <Td className="text-xs text-ink-muted">{rotuloModelo(d.tipo)}</Td>
                    <Td className="max-w-56 truncate text-xs" title={d.tenantRazaoSocial}>
                      {d.tenantRazaoSocial}
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
                ))}
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