import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { ArrowRight, CircleAlert, FileText, ShieldAlert, Webhook } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { STATUS_LABEL } from '../lib/fiscal'
import { formatarData, formatarDataCurta } from '../lib/format'
import { cn } from '../lib/utils'
import {
  AmbienteBadge,
  Badge,
  Button,
  ErrorState,
  PageLoading,
  StatusDot,
  Table,
  Td,
  Th,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'

// ==========================================================================
// Visão geral — atenção primeiro.
//
// A versão anterior respondia "quantos": seis cards iguais e um gráfico de
// barras. Esta responde "o que está travado": fila de transmissão, o que
// precisa de ação (certificados, webhooks, rejeições) e a emissão recente.
// As contagens viram uma fita compacta no topo.
//
// Uma única consulta alimenta a página: se ela cai, a tela inteira mostra
// ErrorState com retry, em vez de meia página vazia sem explicação.
// ==========================================================================

export default function VisaoGeral() {
  const navigate = useNavigate()
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['dashboard'],
    queryFn: api.dashboard,
    refetchInterval: 15_000,
  })

  if (isLoading) return <PageLoading mensagem="Carregando visão geral…" />

  if (isError || !data) {
    return (
      <ErrorState
        titulo="Não foi possível carregar a visão geral"
        detalhe={error instanceof ApiError ? (error.detail ?? error.message) : 'Verifique se a API está rodando.'}
        aoTentarNovamente={() => refetch()}
      />
    )
  }

  const fila = data.fila ?? []
  const certs = data.certificadosVencendo ?? []
  const webhooks = data.webhooksEmFalha ?? []
  const rejeicoes = data.rejeicoesRecentes ?? []
  const totalAtencao = certs.length + webhooks.length + rejeicoes.length

  return (
    <>
      <PageHeader
        titulo="Visão geral"
        descricao="O que está em trânsito e o que precisa de ação. Atualiza a cada 15s."
      />

      {/* ---------- Fita de contagens ---------- */}
      <div className="mb-4 flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border border-line bg-surface px-4 py-3">
        <Contagem rotulo="Hoje" valor={data.documentosHoje} />
        <Separador />
        <Contagem rotulo="7 dias" valor={data.documentos7Dias} />
        <Separador />
        <Contagem rotulo="Em contingência" valor={data.emContingencia} alerta={data.emContingencia > 0} />
        <Separador />
        <Contagem rotulo="Empresas ativas" valor={data.tenantsAtivos} />
        <Separador />
        <Contagem rotulo="API keys" valor={data.apiKeysAtivas} />
        <Separador />
        <Contagem
          rotulo="Certificados vencendo"
          valor={data.certificadosVencendo30Dias}
          alerta={data.certificadosVencendo30Dias > 0}
        />
      </div>

      {/* ---------- Fila de transmissão ---------- */}
      <section className="mb-4 rounded-lg border border-line bg-surface">
        <header className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
          <div className="flex items-center gap-2">
            <FileText className="h-3.5 w-3.5 text-ink-faint" aria-hidden />
            <h2 className="text-[13px] font-semibold text-ink">Fila de transmissão</h2>
            {fila.length > 0 && <Badge className="border-carimbo/25 bg-carimbo-soft text-carimbo">{fila.length}</Badge>}
          </div>
          <Button asChild variant="ghost" size="sm">
            <Link to="/documentos?status=PENDENTE,PROCESSANDO,CONTINGENCIA,CANCELAMENTO_PENDENTE">
              Ver todos
              <ArrowRight className="h-3 w-3" aria-hidden />
            </Link>
          </Button>
        </header>

        {fila.length === 0 ? (
          <p className="px-4 py-6 text-center text-xs text-ink-faint">
            Nenhum documento em trânsito — a fila está vazia.
          </p>
        ) : (
          <Table label="Documentos em trânsito">
            <thead>
              <tr>
                <Th className="w-32">Número</Th>
                <Th>Empresa</Th>
                <Th className="w-44">Situação</Th>
                <Th className="w-24 text-center">Tentativas</Th>
                <Th className="w-40">Próxima tentativa</Th>
                <Th className="w-36">Criado em</Th>
              </tr>
            </thead>
            <tbody>
              {fila.map((d) => (
                <tr key={d.id} className="transition-colors hover:bg-surface-sunken/60">
                  <Td>
                    <Link to={`/documentos/${d.id}`} className="font-mono text-xs text-carimbo tabular hover:underline">
                      {d.serie !== null ? `${d.serie}/${String(d.numero ?? 0).padStart(9, '0')}` : '—'}
                    </Link>
                  </Td>
                  <Td className="max-w-56 truncate text-xs" title={d.tenantRazaoSocial}>
                    {d.tenantRazaoSocial}
                  </Td>
                  <Td>
                    <StatusDot status={d.status} withLabel />
                  </Td>
                  <Td className="text-center text-xs tabular">{d.tentativas ?? '—'}</Td>
                  <Td className="text-xs text-ink-muted tabular">
                    {formatarData(d.proximaTentativaEm)}
                  </Td>
                  <Td className="text-xs text-ink-muted tabular">{formatarData(d.criadoEm)}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </section>

      {/* ---------- Precisa de atenção ---------- */}
      <section className="mb-4 rounded-lg border border-line bg-surface">
        <header className="flex items-center gap-2 border-b border-line px-4 py-2.5">
          <CircleAlert className={cn('h-3.5 w-3.5', totalAtencao > 0 ? 'text-contingencia' : 'text-ink-faint')} aria-hidden />
          <h2 className="text-[13px] font-semibold text-ink">Precisa de atenção</h2>
          {totalAtencao > 0 && (
            <Badge className="border-contingencia/30 bg-contingencia-soft text-contingencia">{totalAtencao}</Badge>
          )}
        </header>

        {totalAtencao === 0 ? (
          <p className="px-4 py-6 text-center text-xs text-ink-faint">
            Nada pendente: nenhum certificado vencendo, nenhum webhook em falha, nenhuma rejeição recente.
          </p>
        ) : (
          <div className="divide-y divide-line">
            {certs.length > 0 && (
              <BlocoAtencao
                icone={<ShieldAlert className="h-3.5 w-3.5 text-contingencia" aria-hidden />}
                titulo={`${certs.length} certificado(s) vencendo em 30 dias`}
              >
                <ul className="space-y-1.5">
                  {certs.slice(0, 5).map((c) => (
                    <li key={c.id} className="flex flex-wrap items-baseline justify-between gap-2 text-xs">
                      <Link to={`/empresas/${c.tenantId}?tab=certs`} className="text-ink hover:text-carimbo hover:underline">
                        {c.tenantRazaoSocial ?? 'Empresa'}
                      </Link>
                      <span className="font-mono text-[10px] text-ink-faint">{c.thumbprint.slice(0, 12)}…</span>
                      <span className={cn('tabular', c.diasRestantes <= 7 ? 'text-rejeitado' : 'text-contingencia')}>
                        {formatarDataCurta(c.validoAte)} · {c.diasRestantes}d
                      </span>
                    </li>
                  ))}
                </ul>
              </BlocoAtencao>
            )}

            {webhooks.length > 0 && (
              <BlocoAtencao
                icone={<Webhook className="h-3.5 w-3.5 text-rejeitado" aria-hidden />}
                titulo={`${webhooks.length} entrega(s) de webhook em falha`}
              >
                <ul className="space-y-1.5">
                  {webhooks.slice(0, 5).map((w) => (
                    <li key={w.id} className="flex flex-wrap items-baseline justify-between gap-2 text-xs">
                      <Link to={`/empresas/${w.tenantId}?tab=webhooks`} className="text-ink hover:text-carimbo hover:underline">
                        {w.tenantRazaoSocial ?? 'Empresa'}
                      </Link>
                      <span className="font-mono text-[10px] text-ink-faint">{w.tipoEvento}</span>
                      <span className="text-rejeitado tabular">
                        HTTP {w.ultimoStatusCode ?? '—'} · {w.tentativas} tentativas
                      </span>
                    </li>
                  ))}
                </ul>
              </BlocoAtencao>
            )}

            {rejeicoes.length > 0 && (
              <BlocoAtencao
                icone={<CircleAlert className="h-3.5 w-3.5 text-rejeitado" aria-hidden />}
                titulo={`${rejeicoes.length} rejeição(ões) recente(s)`}
              >
                <ul className="space-y-1.5">
                  {rejeicoes.slice(0, 5).map((r) => (
                    <li key={r.id} className="text-xs">
                      <div className="flex flex-wrap items-baseline justify-between gap-2">
                        <Link to={`/documentos/${r.id}`} className="font-mono text-carimbo tabular hover:underline">
                          {r.serie}/{String(r.numero ?? 0).padStart(9, '0')}
                        </Link>
                        <span className="text-ink-muted">{r.tenantRazaoSocial}</span>
                      </div>
                      {r.motivo && <p className="mt-0.5 text-[11px] text-rejeitado">{r.motivo}</p>}
                    </li>
                  ))}
                </ul>
              </BlocoAtencao>
            )}
          </div>
        )}
      </section>

      {/* ---------- Emissão recente ---------- */}
      {(data.emissaoRecente?.length ?? 0) > 0 && (
        <section className="mb-4 rounded-lg border border-line bg-surface">
          <header className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
            <h2 className="text-[13px] font-semibold text-ink">Emissão recente</h2>
            <Button asChild variant="ghost" size="sm">
              <Link to="/documentos">
                Ver todos
                <ArrowRight className="h-3 w-3" aria-hidden />
              </Link>
            </Button>
          </header>
          <Table label="Documentos emitidos recentemente">
            <thead>
              <tr>
                <Th className="w-52">Documento</Th>
                <Th>Empresa</Th>
                <Th className="w-40">Situação</Th>
                <Th className="w-32">Ambiente</Th>
                <Th className="w-36">Criado em</Th>
              </tr>
            </thead>
            <tbody>
              {data.emissaoRecente!.map((d) => (
                <tr
                  key={d.id}
                  onClick={() => navigate(`/documentos/${d.id}`)}
                  className="group cursor-pointer transition-colors hover:bg-surface-sunken/60"
                >
                  <Td>
                    <div className="flex items-center gap-2">
                      <Link
                        to={`/documentos/${d.id}`}
                        onClick={(e) => e.stopPropagation()}
                        className="font-mono text-[13px] font-semibold text-ink tabular group-hover:text-carimbo hover:underline"
                      >
                        {d.serie !== null ? `${d.serie}/${String(d.numero ?? 0).padStart(9, '0')}` : '—'}
                      </Link>
                      <span className="shrink-0 rounded border border-line-strong bg-surface-sunken px-1.5 py-0.5 text-[10px] text-ink-muted">
                        {d.tipo === 'NFE' ? 'NF-e' : d.tipo === 'NFCE' ? 'NFC-e' : d.tipo === 'NFSE' ? 'NFS-e' : d.tipo}
                      </span>
                    </div>
                  </Td>
                  <Td className="max-w-64 truncate text-xs" title={d.tenantRazaoSocial}>
                    {d.tenantRazaoSocial}
                  </Td>
                  <Td>
                    <StatusDot status={d.status} withLabel />
                  </Td>
                  <Td>
                    <AmbienteBadge ambiente={d.ambiente} />
                  </Td>
                  <Td className="text-xs text-ink-muted tabular">{formatarData(d.criadoEm)}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </section>
      )}

      {/* ---------- Distribuição por status ---------- */}
      {data.porStatus.length > 0 && (
        <section className="rounded-lg border border-line bg-surface">
          <header className="border-b border-line px-4 py-2.5">
            <h2 className="text-[13px] font-semibold text-ink">Documentos por situação</h2>
          </header>
          <div className="flex flex-wrap gap-x-6 gap-y-2 px-4 py-3">
            {data.porStatus.map((s) => (
              <Link
                key={s.status}
                to={`/documentos?status=${s.status}`}
                className="flex items-center gap-2 text-xs hover:underline"
              >
                <StatusDot status={s.status} />
                <span className="text-ink-muted">{STATUS_LABEL[s.status] ?? s.status}</span>
                <span className="font-medium text-ink tabular">{s.total}</span>
              </Link>
            ))}
          </div>
        </section>
      )}
    </>
  )
}

function Contagem({ rotulo, valor, alerta }: { rotulo: string; valor: number; alerta?: boolean }) {
  return (
    <div className="flex items-baseline gap-2">
      <span className="text-[11px] text-ink-muted">{rotulo}</span>
      <span className={cn('text-lg font-semibold tabular', alerta ? 'text-contingencia' : 'text-ink')}>
        {valor}
      </span>
    </div>
  )
}

function Separador() {
  return <span className="h-4 w-px bg-line" aria-hidden />
}

function BlocoAtencao({
  icone, titulo, children,
}: { icone: React.ReactNode; titulo: string; children: React.ReactNode }) {
  return (
    <div className="px-4 py-3">
      <div className="mb-2 flex items-center gap-2">
        {icone}
        <p className="text-xs font-medium text-ink">{titulo}</p>
      </div>
      {children}
    </div>
  )
}
