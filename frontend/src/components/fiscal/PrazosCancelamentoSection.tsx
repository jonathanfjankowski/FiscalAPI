import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Clock, Pencil, RotateCcw, Search } from 'lucide-react'
import { api, ApiError, type PrazoCancelamentoItem } from '../../lib/api'
import {
  Badge,
  Button,
  Dialog,
  ErrorState,
  Input,
  PageLoading,
  useToast,
} from '../ui'

const NOMES_UF: Record<string, string> = {
  AC: 'Acre',
  AL: 'Alagoas',
  AM: 'Amazonas',
  AP: 'Amapá',
  BA: 'Bahia',
  CE: 'Ceará',
  DF: 'Distrito Federal',
  ES: 'Espírito Santo',
  GO: 'Goiás',
  MA: 'Maranhão',
  MG: 'Minas Gerais',
  MS: 'Mato Grosso do Sul',
  MT: 'Mato Grosso',
  PA: 'Pará',
  PB: 'Paraíba',
  PE: 'Pernambuco',
  PI: 'Piauí',
  PR: 'Paraná',
  RJ: 'Rio de Janeiro',
  RN: 'Rio Grande do Norte',
  RO: 'Rondônia',
  RR: 'Roraima',
  RS: 'Rio Grande do Sul',
  SC: 'Santa Catarina',
  SE: 'Sergipe',
  SP: 'São Paulo',
  TO: 'Tocantins',
}

function formatarPrazo(minutos: number): string {
  if (minutos < 60) return `${minutos} min`
  if (minutos % 1440 === 0 && minutos > 1440) return `${minutos / 60}h (${minutos / 1440}d)`
  if (minutos % 60 === 0) return `${minutos / 60}h`
  const h = Math.floor(minutos / 60)
  const m = minutos % 60
  return `${h}h${String(m).padStart(2, '0')}m`
}

export function PrazosCancelamentoSection() {
  const toast = useToast()
  const qc = useQueryClient()

  const [busca, setBusca] = useState('')
  const [somenteCustom, setSomenteCustom] = useState(false)
  const [editando, setEditando] = useState<PrazoCancelamentoItem | null>(null)
  const [unidade, setUnidade] = useState<'minutos' | 'horas'>('horas')
  const [valorInput, setValorInput] = useState('')
  const [observacao, setObservacao] = useState('')

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['prazos-cancelamento'],
    queryFn: api.listPrazosCancelamento,
  })

  const mutSalvar = useMutation({
    mutationFn: (args: { uf: string; modelo: 55 | 65; prazoMinutos: number; observacao: string | null }) =>
      api.updatePrazoCancelamento(args.uf, args.modelo, {
        prazoMinutos: args.prazoMinutos,
        observacao: args.observacao,
      }),
    onSuccess: (atualizado) => {
      qc.invalidateQueries({ queryKey: ['prazos-cancelamento'] })
      setEditando(null)
      toast({
        tipo: 'sucesso',
        titulo: `Prazo atualizado (${atualizado.uf} · ${atualizado.modelo === 65 ? 'NFC-e' : 'NF-e'})`,
        detalhe: `Novo prazo: ${formatarPrazo(atualizado.prazoMinutos)}.`,
      })
    },
    onError: (e) => {
      toast({
        tipo: 'erro',
        titulo: 'Falha ao salvar prazo',
        detalhe: e instanceof ApiError ? (e.detail ?? e.message) : e.message,
      })
    },
  })

  const mutRestaurar = useMutation({
    mutationFn: (args: { uf: string; modelo: 55 | 65 }) =>
      api.resetPrazoCancelamento(args.uf, args.modelo),
    onSuccess: (restaurado) => {
      qc.invalidateQueries({ queryKey: ['prazos-cancelamento'] })
      setEditando(null)
      toast({
        tipo: 'sucesso',
        titulo: `Padrão SEFAZ restaurado (${restaurado.uf} · ${restaurado.modelo === 65 ? 'NFC-e' : 'NF-e'})`,
        detalhe: `Prazo voltou para ${formatarPrazo(restaurado.prazoMinutos)}.`,
      })
    },
    onError: (e) => {
      toast({
        tipo: 'erro',
        titulo: 'Falha ao restaurar padrão',
        detalhe: e instanceof ApiError ? (e.detail ?? e.message) : e.message,
      })
    },
  })

  const itens = useMemo(() => data?.itens ?? [], [data])

  const totalCustom = useMemo(() => itens.filter((i) => i.customizado).length, [itens])

  const linhasPorUf = useMemo(() => {
    const mapa = new Map<string, { uf: string; nfe?: PrazoCancelamentoItem; nfce?: PrazoCancelamentoItem }>()
    for (const item of itens) {
      const linha = mapa.get(item.uf) ?? { uf: item.uf }
      if (item.modelo === 55) linha.nfe = item
      else linha.nfce = item
      mapa.set(item.uf, linha)
    }

    const termo = busca.trim().toLowerCase()
    return Array.from(mapa.values()).filter((l) => {
      if (somenteCustom && !l.nfe?.customizado && !l.nfce?.customizado) return false
      if (!termo) return true
      const nome = (NOMES_UF[l.uf] ?? '').toLowerCase()
      return l.uf.toLowerCase().includes(termo) || nome.includes(termo)
    })
  }, [itens, busca, somenteCustom])

  function abrirEdicao(item: PrazoCancelamentoItem) {
    setEditando(item)
    if (item.prazoMinutos >= 60 && item.prazoMinutos % 60 === 0) {
      setUnidade('horas')
      setValorInput(String(item.prazoMinutos / 60))
    } else {
      setUnidade('minutos')
      setValorInput(String(item.prazoMinutos))
    }
    setObservacao(item.observacao ?? '')
  }

  function salvarEdicao() {
    if (!editando) return
    const num = Number(valorInput)
    if (!Number.isFinite(num) || num <= 0) {
      toast({ tipo: 'erro', titulo: 'Informe um prazo válido maior que zero.' })
      return
    }
    const minutos = Math.round(unidade === 'horas' ? num * 60 : num)
    mutSalvar.mutate({
      uf: editando.uf,
      modelo: editando.modelo,
      prazoMinutos: minutos,
      observacao: observacao.trim() || null,
    })
  }

  return (
    <section className="rounded-lg border border-line bg-surface">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-2.5">
        <div className="flex items-center gap-2">
          <Clock className="h-3.5 w-3.5 text-ink-faint" aria-hidden />
          <h2 className="text-[13px] font-semibold text-ink">
            Prazos de cancelamento por UF (SEFAZ)
          </h2>
        </div>
        <Badge className="border-line bg-canvas text-ink-muted">
          {totalCustom > 0 ? `${totalCustom} personalizado(s)` : 'Padrão SEFAZ'}
        </Badge>
      </header>

      <div className="p-4">
        <p className="text-xs leading-relaxed text-ink-muted">
          Prazo máximo contado a partir da autorização para aceitar cancelamento de{' '}
          <strong className="font-medium text-ink">NF-e (55)</strong> e{' '}
          <strong className="font-medium text-ink">NFC-e (65)</strong>. Clique em qualquer prazo
          para ajustar quando alguma SEFAZ alterar a regra estadual.
        </p>

        <div className="mt-3 flex flex-wrap items-center gap-2">
          <div className="relative min-w-[200px] flex-1 max-w-xs">
            <Search
              className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-ink-faint"
              aria-hidden
            />
            <Input
              value={busca}
              onChange={(e) => setBusca(e.target.value)}
              placeholder="Filtrar por UF ou estado…"
              className="pl-8"
              aria-label="Filtrar por UF ou estado"
            />
          </div>
          {totalCustom > 0 && (
            <Button
              variant={somenteCustom ? 'primary' : 'secondary'}
              size="sm"
              onClick={() => setSomenteCustom((v) => !v)}
            >
              Personalizados ({totalCustom})
            </Button>
          )}
        </div>

        {isLoading ? (
          <div className="mt-4">
            <PageLoading />
          </div>
        ) : isError ? (
          <div className="mt-4">
            <ErrorState
              titulo="Não foi possível carregar os prazos"
              aoTentarNovamente={() => refetch()}
            />
          </div>
        ) : (
          <div className="mt-3 max-h-[420px] overflow-auto rounded-md border border-line">
            <table className="w-full border-collapse text-left text-xs">
              <thead className="sticky top-0 z-10 bg-canvas text-[11px] uppercase tracking-wide text-ink-muted">
                <tr className="border-b border-line">
                  <th className="px-3 py-2 font-medium">Estado</th>
                  <th className="px-3 py-2 font-medium">NF-e (55)</th>
                  <th className="px-3 py-2 font-medium">NFC-e (65)</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {linhasPorUf.map((linha) => (
                  <tr key={linha.uf} className="hover:bg-canvas/50">
                    <td className="px-3 py-2">
                      <span className="font-mono font-semibold text-ink">{linha.uf}</span>
                      <span className="ml-2 text-ink-muted">{NOMES_UF[linha.uf] ?? ''}</span>
                    </td>
                    <td className="px-3 py-2">
                      {linha.nfe && (
                        <button
                          type="button"
                          onClick={() => abrirEdicao(linha.nfe!)}
                          title={linha.nfe.observacao ?? undefined}
                          className="group inline-flex items-center gap-1.5 rounded border border-line bg-canvas px-2 py-1 font-mono text-ink transition hover:border-carimbo"
                        >
                          <span>{formatarPrazo(linha.nfe.prazoMinutos)}</span>
                          {linha.nfe.customizado && (
                            <span className="h-1.5 w-1.5 rounded-full bg-carimbo" title="Personalizado" />
                          )}
                          <Pencil className="h-3 w-3 text-ink-faint opacity-60 group-hover:text-carimbo group-hover:opacity-100" />
                        </button>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      {linha.nfce && (
                        <button
                          type="button"
                          onClick={() => abrirEdicao(linha.nfce!)}
                          title={linha.nfce.observacao ?? undefined}
                          className="group inline-flex items-center gap-1.5 rounded border border-line bg-canvas px-2 py-1 font-mono text-ink transition hover:border-carimbo"
                        >
                          <span>{formatarPrazo(linha.nfce.prazoMinutos)}</span>
                          {linha.nfce.customizado && (
                            <span className="h-1.5 w-1.5 rounded-full bg-carimbo" title="Personalizado" />
                          )}
                          <Pencil className="h-3 w-3 text-ink-faint opacity-60 group-hover:text-carimbo group-hover:opacity-100" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {linhasPorUf.length === 0 && (
                  <tr>
                    <td colSpan={3} className="px-3 py-6 text-center text-ink-muted">
                      Nenhuma UF encontrada para o filtro informado.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <Dialog
        open={!!editando}
        onClose={() => setEditando(null)}
        titulo={
          editando
            ? `Prazo de cancelamento · ${editando.uf} (${editando.modelo === 65 ? 'NFC-e 65' : 'NF-e 55'})`
            : ''
        }
        rodape={
          <div className="flex w-full items-center justify-between gap-2">
            <div>
              {editando?.customizado && (
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={mutRestaurar.isPending || mutSalvar.isPending}
                  onClick={() =>
                    mutRestaurar.mutate({ uf: editando.uf, modelo: editando.modelo })
                  }
                >
                  <RotateCcw className="h-3.5 w-3.5" />
                  Restaurar padrão ({formatarPrazo(editando.prazoPadraoMinutos)})
                </Button>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Button variant="secondary" onClick={() => setEditando(null)}>
                Cancelar
              </Button>
              <Button
                variant="primary"
                onClick={salvarEdicao}
                disabled={mutSalvar.isPending}
              >
                {mutSalvar.isPending ? 'Salvando…' : 'Salvar'}
              </Button>
            </div>
          </div>
        }
      >
        {editando && (
          <div className="space-y-3 text-xs">
            <p className="text-ink-muted">
              Padrão SEFAZ para {NOMES_UF[editando.uf] ?? editando.uf}:{' '}
              <strong className="font-mono text-ink">
                {formatarPrazo(editando.prazoPadraoMinutos)}
              </strong>{' '}
              ({editando.prazoPadraoMinutos} min).
            </p>

            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="mb-1 block text-[11px] font-medium text-ink-muted">
                  Prazo
                </label>
                <Input
                  type="number"
                  min={1}
                  step={unidade === 'horas' ? '0.5' : '1'}
                  value={valorInput}
                  onChange={(e) => setValorInput(e.target.value)}
                />
              </div>
              <div>
                <label className="mb-1 block text-[11px] font-medium text-ink-muted">
                  Unidade
                </label>
                <select
                  value={unidade}
                  onChange={(e) => setUnidade(e.target.value === 'minutos' ? 'minutos' : 'horas')}
                  className="h-8 w-full rounded-md border border-line bg-surface px-2.5 text-xs text-ink focus:border-carimbo focus:outline-none"
                >
                  <option value="horas">Horas</option>
                  <option value="minutos">Minutos</option>
                </select>
              </div>
            </div>

            <div>
              <label className="mb-1 block text-[11px] font-medium text-ink-muted">
                Observação (opcional)
              </label>
              <Input
                value={observacao}
                maxLength={300}
                placeholder="Ex.: Portaria SEFAZ alterada em 2026"
                onChange={(e) => setObservacao(e.target.value)}
              />
            </div>
          </div>
        )}
      </Dialog>
    </section>
  )
}
