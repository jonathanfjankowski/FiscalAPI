import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Moon, Sun } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useTema } from '../hooks/useTema'
import { formatarData } from '../lib/format'
import {
  Badge,
  Button,
  ConfirmDialog,
  Dialog,
  ErrorState,
  PageLoading,
  Switch,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { SegredoUnico } from '../components/ui/segredo'

// ==========================================================================
// Configurações da plataforma.
//
// Chave master de provisionamento (o ERP usa no POST /v1/empresas) e
// preferências do painel. A rotação invalida a chave anterior na hora — o
// aviso diz isso, porque o ERP para de provisionar até ser atualizado.
// ==========================================================================

export default function Configuracoes() {
  const toast = useToast()
  const qc = useQueryClient()
  const { tema, alternar } = useTema()
  const [confirmar, setConfirmar] = useState(false)
  const [gerada, setGerada] = useState<{ chave: string; aviso: string } | null>(null)

  const { data: chave, isLoading, isError, refetch } = useQuery({
    queryKey: ['bootstrap-key'],
    queryFn: api.getBootstrapKey,
  })

  const mut = useMutation({
    mutationFn: api.rotacionarBootstrapKey,
    onError: (e) => {
      // Operação destrutiva: falha silenciosa deixaria o diálogo só parando
      // o loading, sem dizer que a chave NÃO foi trocada.
      setConfirmar(false)
      toast({
        tipo: 'erro',
        titulo: 'Falha ao rotacionar a chave',
        detalhe: e instanceof ApiError ? (e.detail ?? e.message) : e.message,
      })
    },
    onSuccess: (k) => {
      qc.invalidateQueries({ queryKey: ['bootstrap-key'] })
      setConfirmar(false)
      setGerada({ chave: k.chave, aviso: k.aviso })
    },
  })

  return (
    <>
      <PageHeader titulo="Configurações" descricao="Chave de provisionamento e preferências do painel." />

      <div className="max-w-2xl space-y-4">
        {/* ---------- Chave master ---------- */}
        <section className="rounded-lg border border-line bg-surface">
          <header className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
            <div className="flex items-center gap-2">
              <KeyRound className="h-3.5 w-3.5 text-ink-faint" aria-hidden />
              <h2 className="text-[13px] font-semibold text-ink">Chave master de provisionamento</h2>
            </div>
            {chave && (
              <Badge
                className={
                  chave.configurada === false
                    ? 'border-rejeitado/30 bg-rejeitado-soft text-rejeitado'
                    : 'border-aprovado/30 bg-aprovado-soft text-aprovado'
                }
              >
                {chave.configurada === false ? 'Não configurada' : 'Configurada'}
              </Badge>
            )}
          </header>

          {isLoading ? (
            <PageLoading />
          ) : isError ? (
            <div className="p-4">
              <ErrorState titulo="Não foi possível carregar a chave" aoTentarNovamente={() => refetch()} />
            </div>
          ) : (
            <div className="p-4">
              <p className="text-xs leading-relaxed text-ink-muted">
                Segredo que o ERP envia no header{' '}
                <code className="font-mono text-ink">X-Bootstrap-Key</code> ao chamar{' '}
                <code className="font-mono text-ink">POST /v1/empresas</code> para criar/atualizar
                empresas automaticamente (idempotente por CNPJ).
              </p>

              <dl className="mt-3 grid grid-cols-2 gap-3">
                <div className="rounded border border-line px-3 py-2">
                  <dt className="text-[11px] text-ink-muted">Prefixo</dt>
                  <dd className="mt-0.5 font-mono text-[13px] text-ink">
                    {chave?.configurada === false || !chave?.prefixo ? '—' : `${chave.prefixo}…`}
                  </dd>
                </div>
                <div className="rounded border border-line px-3 py-2">
                  <dt className="text-[11px] text-ink-muted">Criada em</dt>
                  <dd className="mt-0.5 text-[13px] text-ink tabular">{formatarData(chave?.criadoEm)}</dd>
                </div>
              </dl>

              <p className="mt-3 text-[11px] leading-relaxed text-ink-faint">
                A chave completa nunca é exibida depois de criada — apenas o prefixo.
              </p>

              <div className="mt-4 flex justify-end">
                <Button variant="primary" onClick={() => setConfirmar(true)}>
                  Rotacionar chave
                </Button>
              </div>
            </div>
          )}
        </section>

        {/* ---------- Aparência ---------- */}
        <section className="rounded-lg border border-line bg-surface p-4">
          <div className="flex items-start justify-between gap-4">
            <div>
              <h2 className="text-[13px] font-semibold text-ink">Tema escuro</h2>
              <p className="mt-1 text-xs text-ink-muted">
                A preferência fica neste navegador. Sem preferência salva, segue o sistema.
              </p>
            </div>
            <div className="flex items-center gap-2">
              {tema === 'escuro' ? (
                <Moon className="h-3.5 w-3.5 text-ink-faint" aria-hidden />
              ) : (
                <Sun className="h-3.5 w-3.5 text-ink-faint" aria-hidden />
              )}
              <Switch checked={tema === 'escuro'} onChange={alternar} label="Tema escuro" />
            </div>
          </div>
        </section>

        {/* ---------- Sobre ---------- */}
        <section className="rounded-lg border border-line bg-surface p-4">
          <h2 className="text-[13px] font-semibold text-ink">Sobre</h2>
          <p className="mt-1 text-xs leading-relaxed text-ink-muted">
            FiscalAPI — emissão de NF-e (55), NFC-e (65) e NFS-e Nacional (DPS). A API não
            calcula tributos: os valores vêm prontos do ERP e a API valida estrutura e
            aritmética.
          </p>
          <p className="mt-2 text-[11px] text-ink-faint">
            Fila de jobs:{' '}
            <a
              href="/hangfire"
              target="_blank"
              rel="noreferrer"
              className="font-mono text-carimbo hover:underline"
            >
              /hangfire
            </a>{' '}
            (autenticado com o token do painel).
          </p>
        </section>
      </div>

      <ConfirmDialog
        open={confirmar}
        onClose={() => setConfirmar(false)}
        onConfirm={() => mut.mutate()}
        titulo="Rotacionar chave master"
        mensagem="Uma nova chave é gerada e a atual deixa de valer imediatamente — o ERP precisa ser atualizado na hora, senão o provisionamento automático para."
        confirmarLabel="Rotacionar"
        perigoso
        loading={mut.isPending}
      />

      <Dialog
        open={!!gerada}
        onClose={() => setGerada(null)}
        titulo="Nova chave master"
        rodape={<Button variant="primary" onClick={() => setGerada(null)}>Fechar</Button>}
      >
        {gerada && <SegredoUnico valor={gerada.chave} aviso={gerada.aviso} />}
      </Dialog>
    </>
  )
}
