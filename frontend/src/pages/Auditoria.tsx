import { useQuery } from '@tanstack/react-query'
import { api, ApiError } from '../lib/api'
import { useEmpresaContexto } from '../hooks/useEmpresaContexto'
import { useFiltrosUrl } from '../hooks/useFiltrosUrl'
import { useValorAdiado } from '../hooks/useValorAdiado'
import { copiar, formatarData } from '../lib/format'
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  PageLoading,
  Table,
  Td,
  Th,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { Paginacao } from '../components/ui/paginacao'

// ==========================================================================
// Trilha de auditoria.
//
// A tabela é gravada em ~77 pontos do backend e é append-only por trigger —
// mas nunca tinha sido lida. "Quem cancelou essa nota e quando" é pergunta
// de primeira classe num console de operação.
// ==========================================================================

const CHAVES = ['acao', 'recursoId', 'de', 'ate', 'pageSize'] as const

/** Agrupa a ação por assunto para dar cor semântica. */
function corAcao(acao: string): string | undefined {
  if (acao.includes('REVOGADO') || acao.includes('DESATIVADO') || acao.includes('INVALIDA')) {
    return 'border-rejeitado/30 bg-rejeitado-soft text-rejeitado'
  }
  if (acao.includes('CRIADO') || acao.includes('CRIADA') || acao.includes('ATIVADO') || acao.includes('PROCESSADO')) {
    return 'border-aprovado/30 bg-aprovado-soft text-aprovado'
  }
  if (acao.includes('REJEITADO') || acao.includes('ERRO')) {
    return 'border-contingencia/30 bg-contingencia-soft text-contingencia'
  }
  return undefined
}

export default function Auditoria() {
  const toast = useToast()
  const { filtroTenant } = useEmpresaContexto()
  const { valores, pagina, definir, definirPagina, limpar, algumAtivo } = useFiltrosUrl(CHAVES)
  const pageSize = Number.parseInt(valores.pageSize || '50', 10) || 50
  const acaoAdiada = useValorAdiado(valores.acao)
  const recursoAdiado = useValorAdiado(valores.recursoId)

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['auditoria', filtroTenant, valores, acaoAdiada, recursoAdiado, pagina, pageSize],
    queryFn: () =>
      api.listarAuditoria({
        tenantId: filtroTenant,
        acao: acaoAdiada || undefined,
        recursoId: recursoAdiado || undefined,
        de: valores.de ? new Date(valores.de + 'T00:00:00').toISOString() : undefined,
        ate: valores.ate ? new Date(valores.ate + 'T23:59:59').toISOString() : undefined,
        page: pagina,
        pageSize,
      }),
  })

  return (
    <>
      <PageHeader
        titulo="Auditoria"
        descricao="Trilha de ações da plataforma. Registro append-only — nada é alterado ou removido."
      />

      <div className="mb-4 flex flex-wrap items-center gap-2 rounded-lg border border-line bg-surface p-3">
        <Input
          value={valores.acao}
          onChange={(e) => definir('acao', e.target.value)}
          placeholder="Ação (ex.: CERTIFICADO, CANCELAMENTO)…"
          aria-label="Filtrar por ação"
          className="h-8 w-64"
        />
        <Input
          value={valores.recursoId}
          onChange={(e) => definir('recursoId', e.target.value)}
          placeholder="ID do recurso (aceita prefixo)…"
          aria-label="Filtrar por recurso"
          className="h-8 w-72 font-mono text-xs"
        />
        <div className="flex items-center gap-1.5">
          <Input type="date" value={valores.de} onChange={(e) => definir('de', e.target.value)} aria-label="Data inicial" className="h-8 w-36" />
          <span className="text-xs text-ink-faint">até</span>
          <Input type="date" value={valores.ate} onChange={(e) => definir('ate', e.target.value)} aria-label="Data final" className="h-8 w-36" />
        </div>
        {algumAtivo && (
          <Button variant="ghost" size="sm" onClick={limpar}>
            Limpar
          </Button>
        )}
      </div>

      <div className="rounded-lg border border-line bg-surface">
        {isLoading ? (
          <PageLoading mensagem="Carregando trilha…" />
        ) : isError ? (
          <div className="p-4">
            <ErrorState
              titulo="Não foi possível carregar a auditoria"
              detalhe={error instanceof ApiError ? (error.detail ?? error.message) : 'Verifique se a API está rodando.'}
              aoTentarNovamente={() => refetch()}
            />
          </div>
        ) : !data || data.itens.length === 0 ? (
          <EmptyState
            titulo="Nenhum registro de auditoria"
            descricao="Ações como criar empresa, gerar chave e cancelar documento aparecem aqui."
          />
        ) : (
          <>
            <Table label="Trilha de auditoria">
              <thead>
                <tr>
                  <Th className="w-36">Quando</Th>
                  <Th className="w-56">Ação</Th>
                  <Th>Empresa</Th>
                  <Th className="w-40">Recurso</Th>
                  <Th className="w-36">IP de origem</Th>
                  <Th>Detalhe</Th>
                </tr>
              </thead>
              <tbody>
                {data.itens.map((a) => (
                  <tr key={a.id} className="transition-colors hover:bg-surface-sunken/60">
                    <Td className="text-xs text-ink-muted tabular">{formatarData(a.criadoEm)}</Td>
                    <Td>
                      <Badge className={corAcao(a.acao)}>{a.acao}</Badge>
                    </Td>
                    <Td className="max-w-48 truncate text-xs" title={a.tenantRazaoSocial ?? undefined}>
                      {a.tenantRazaoSocial ?? <span className="text-ink-faint">—</span>}
                    </Td>
                    <Td>
                      {a.recursoId ? (
                        <button
                          type="button"
                          onClick={async () => {
                            if (await copiar(a.recursoId!)) toast({ tipo: 'sucesso', titulo: 'ID copiado' })
                          }}
                          title={`${a.recursoId} — clique para copiar`}
                          className="font-mono text-[10px] text-ink-muted tabular hover:text-carimbo hover:underline"
                        >
                          {a.recursoId.slice(0, 8)}…
                        </button>
                      ) : (
                        <span className="text-[10px] text-ink-faint">—</span>
                      )}
                    </Td>
                    <Td className="font-mono text-[11px] text-ink-muted tabular">{a.ipOrigem ?? '—'}</Td>
                    <Td className="max-w-72 truncate font-mono text-[10px] text-ink-faint" title={a.detalhe ?? undefined}>
                      {a.detalhe ?? '—'}
                    </Td>
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
                aoMudarTamanho={(n) => definir('pageSize', String(n))}
              />
            </div>
          </>
        )}
      </div>
    </>
  )
}
