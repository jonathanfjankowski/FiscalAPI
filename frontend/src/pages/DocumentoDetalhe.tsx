import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, Download, FileText, RefreshCw, ScrollText, XCircle } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { STATUS_LABEL, STATUS_TRANSITORIOS } from '../lib/fiscal'
import { formatarData } from '../lib/format'
import { cn } from '../lib/utils'
import {
  AmbienteBadge,
  Button,
  Dialog,
  ErrorState,
  Field,
  PageLoading,
  StatusBadge,
  Textarea,
  Tooltip,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { ChaveAcesso, Timeline, TrilhoStatus, XmlViewer } from '../components/fiscal'

// ==========================================================================
// Detalhe do documento.
//
// A tela responde, em ordem: qual é este documento (anatomia da chave), em
// que estado está (trilho), o que aconteceu (timeline e eventos), o que foi
// enviado/retornado (XML) e o que dá para fazer (ações).
//
// O DANFE em PDF é o artefato que o contador quer — a versão anterior só
// mostrava XML cru.
// ==========================================================================

type AbaXml = 'assinado' | 'retorno'

export default function DocumentoDetalhe() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const toast = useToast()

  const [abaXml, setAbaXml] = useState<AbaXml>('assinado')
  const [modalCancelar, setModalCancelar] = useState(false)
  const [modalCce, setModalCce] = useState(false)
  const [modalReenviar, setModalReenviar] = useState(false)
  const [baixandoPdf, setBaixandoPdf] = useState(false)

  const { data: doc, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['documento', id],
    queryFn: () => api.getDocumento(id),
    refetchInterval: (query) =>
      query.state.data && STATUS_TRANSITORIOS.includes(query.state.data.status) ? 3000 : false,
  })

  const { data: eventos } = useQuery({
    queryKey: ['documento-eventos', id],
    queryFn: () => api.listEventos(id),
    enabled: !!doc,
    refetchInterval: (query) =>
      query.state.data?.some((e) => e.status === 'PENDENTE' || e.status === 'PROCESSANDO')
        ? 3000
        : false,
  })

  // Timeline derivada do documento: o histórico completo vive no backend só
  // como estado atual + eventos; aqui mostramos criado → estado atual e os
  // eventos como passos próprios.
  const transicoes = useMemo(() => {
    if (!doc) return []
    const passos = [{ status: 'PENDENTE', em: doc.criadoEm, motivo: null as string | null }]
    if (doc.status !== 'PENDENTE') {
      passos.push({ status: doc.status, em: doc.atualizadoEm, motivo: doc.motivoStatus })
    }
    return passos
  }, [doc])

  if (isLoading) return <PageLoading mensagem="Carregando documento…" />

  if (isError || !doc) {
    return (
      <ErrorState
        titulo="Não foi possível carregar o documento"
        detalhe={error instanceof ApiError ? (error.detail ?? error.message) : 'Documento não encontrado.'}
        aoTentarNovamente={() => refetch()}
      />
    )
  }

  const autorizada = doc.status === 'AUTORIZADA'
  const podeCancelar = autorizada && (doc.modelo === 55 || doc.modelo === 65)
  const podeCce = autorizada && doc.modelo === 55
  // NFS-e usa payload DPS, que não binda em EmissaoRequest — o reenvio
  // sempre falharia com 400. Não oferecer a ação.
  const podeReenviar =
    ['REJEITADA', 'DENEGADA', 'ERRO_INTERNO', 'FALHA_EMISSAO'].includes(doc.status) &&
    doc.tipo !== 'NFSE'
  const podePdf = autorizada || doc.status === 'CANCELADA'

  const titulo =
    doc.serie !== null
      ? `${doc.tipo === 'NFE' ? 'NF-e' : doc.tipo === 'NFCE' ? 'NFC-e' : 'NFS-e'} ${doc.serie}/${String(doc.numero ?? 0).padStart(9, '0')}`
      : doc.tipo

  async function baixarPdf() {
    setBaixandoPdf(true)
    try {
      const blob = await api.baixarPdf(id)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `danfe-${id}.pdf`
      a.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      toast({
        tipo: 'erro',
        titulo: 'Não foi possível baixar o PDF',
        detalhe: e instanceof ApiError ? (e.detail ?? e.message) : 'Erro inesperado.',
      })
    } finally {
      setBaixandoPdf(false)
    }
  }

  return (
    <>
      <PageHeader
        titulo={titulo}
        descricao={`${doc.tenantRazaoSocial} · criado em ${formatarData(doc.criadoEm)}`}
        acoes={
          <>
            <StatusBadge status={doc.status} />
            <AmbienteBadge ambiente={doc.ambiente} />
            <Button asChild variant="ghost">
              <Link to="/documentos">
                <ArrowLeft className="h-3.5 w-3.5" aria-hidden />
                Voltar
              </Link>
            </Button>
          </>
        }
      />

      {/* ---------- Identidade e situação ---------- */}
      <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
        <div className="space-y-4">
          <ChaveAcesso chave={doc.chaveAcesso} />

          <div className="rounded-lg border border-line bg-surface p-4">
            <TrilhoStatus status={doc.status} />
            {doc.motivoStatus && (
              <p className="mt-3 border-t border-line pt-3 text-xs leading-relaxed text-ink-muted">
                {doc.motivoStatus}
              </p>
            )}
          </div>
        </div>

        <div className="space-y-4">
          <div className="rounded-lg border border-line bg-surface">
            <dl className="divide-y divide-line">
              <Linha rotulo="Modelo / Série / Nº" valor={`${doc.modelo ?? '—'} / ${doc.serie ?? '—'} / ${doc.numero ?? '—'}`} />
              <Linha rotulo="Protocolo" valor={doc.protocoloAutorizacao ?? '—'} mono />
              <Linha rotulo="Recibo do lote" valor={doc.reciboLote ?? '—'} mono />
              <Linha rotulo="Tentativas" valor={String(doc.tentativas)} />
              <Linha rotulo="Atualizado em" valor={formatarData(doc.atualizadoEm)} />
            </dl>
          </div>

          {doc.status === 'CONTINGENCIA' && (
            <div className="rounded-lg border border-contingencia/30 bg-contingencia-soft/50 p-3 text-xs leading-relaxed text-ink">
              <p className="font-medium text-contingencia">Em contingência</p>
              <p className="mt-1 text-ink-muted">
                Nova tentativa{' '}
                {doc.proximaTentativaEm ? formatarData(doc.proximaTentativaEm) : 'em breve'} (tentativa{' '}
                {doc.tentativas + 1}). O Worker reenfileira a cada 30s.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* ---------- Ações ---------- */}
      <div className="mt-4 flex flex-wrap items-center gap-2">
        {podePdf ? (
          <Button variant="primary" onClick={baixarPdf} loading={baixandoPdf}>
            <Download className="h-3.5 w-3.5" aria-hidden />
            DANFE (PDF)
          </Button>
        ) : (
          <Tooltip content={`PDF só para AUTORIZADA ou CANCELADA (atual: ${STATUS_LABEL[doc.status] ?? doc.status})`}>
            <span>
              <Button variant="primary" disabled>
                <Download className="h-3.5 w-3.5" aria-hidden />
                DANFE (PDF)
              </Button>
            </span>
          </Tooltip>
        )}

        {podeReenviar && (
          <Button onClick={() => setModalReenviar(true)}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden />
            Corrigir e reenviar
          </Button>
        )}

        {podeCancelar && (
          <Button variant="danger" onClick={() => setModalCancelar(true)}>
            <XCircle className="h-3.5 w-3.5" aria-hidden />
            Cancelar documento
          </Button>
        )}

        {podeCce && (
          <Button onClick={() => setModalCce(true)}>
            <ScrollText className="h-3.5 w-3.5" aria-hidden />
            Carta de correção
          </Button>
        )}

      </div>

      {/* ---------- Linha do tempo e eventos ---------- */}
      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <section className="rounded-lg border border-line bg-surface p-4">
          <h2 className="mb-3 text-xs font-semibold text-ink-muted">Linha do tempo</h2>
          <Timeline transicoes={transicoes} />
        </section>

        <section className="rounded-lg border border-line bg-surface p-4">
          <h2 className="mb-3 text-xs font-semibold text-ink-muted">Eventos fiscais</h2>
          {!eventos || eventos.length === 0 ? (
            <p className="py-4 text-center text-xs text-ink-faint">Nenhum evento registrado.</p>
          ) : (
            <ul className="space-y-2">
              {eventos.map((e) => (
                <li key={e.id} className="rounded border border-line p-2.5">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="text-[13px] text-ink">
                      {e.tipo === 'CANCELAMENTO' ? 'Cancelamento' : e.tipo === 'CCE' ? 'Carta de correção' : e.tipo}
                    </span>
                    <StatusBadge status={e.status} />
                  </div>
                  {e.motivo && <p className="mt-1 text-[11px] text-ink-muted">{e.motivo}</p>}
                  <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-[10px] text-ink-faint">
                    <span>{formatarData(e.criadoEm)}</span>
                    {e.protocolo && <span className="font-mono">prot. {e.protocolo}</span>}
                    <span>tentativas: {e.tentativas}</span>
                    {e.xmlDisponivel && <span className="text-aprovado">XML protocolado disponível</span>}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      {/* ---------- XML ---------- */}
      {(doc.xmlAssinado || doc.xmlRetornoSefaz) && (
        <section className="mt-4 rounded-lg border border-line bg-surface">
          <div className="flex items-center gap-1 border-b border-line px-3 py-2">
            <FileText className="mr-1 h-3.5 w-3.5 text-ink-faint" aria-hidden />
            {doc.xmlAssinado && (
              <BotaoAba ativo={abaXml === 'assinado'} onClick={() => setAbaXml('assinado')}>
                XML assinado
              </BotaoAba>
            )}
            {doc.xmlRetornoSefaz && (
              <BotaoAba ativo={abaXml === 'retorno'} onClick={() => setAbaXml('retorno')}>
                Retorno SEFAZ
              </BotaoAba>
            )}
          </div>
          <XmlViewer xml={abaXml === 'assinado' ? doc.xmlAssinado : doc.xmlRetornoSefaz} />
        </section>
      )}

      {/* ---------- Modais de ação ---------- */}
      <ModalCancelar
        open={modalCancelar}
        onClose={() => setModalCancelar(false)}
        documentoId={id}
        aoConcluir={() => {
          setModalCancelar(false)
          qc.invalidateQueries({ queryKey: ['documento', id] })
          qc.invalidateQueries({ queryKey: ['documento-eventos', id] })
        }}
      />
      <ModalCce
        open={modalCce}
        onClose={() => setModalCce(false)}
        documentoId={id}
        aoConcluir={() => {
          setModalCce(false)
          qc.invalidateQueries({ queryKey: ['documento', id] })
          qc.invalidateQueries({ queryKey: ['documento-eventos', id] })
        }}
      />
      <ModalReenviar
        open={modalReenviar}
        onClose={() => setModalReenviar(false)}
        documentoId={id}
        payloadOriginal={doc.payloadEntrada ?? '{}'}
        aoConcluir={() => {
          setModalReenviar(false)
          qc.invalidateQueries({ queryKey: ['documento', id] })
        }}
      />
    </>
  )
}

function Linha({ rotulo, valor, mono }: { rotulo: string; valor: string; mono?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-3 px-3 py-2">
      <dt className="shrink-0 text-[11px] text-ink-muted">{rotulo}</dt>
      <dd className={cn('truncate text-right text-[13px] text-ink', mono && 'font-mono text-xs')} title={valor}>
        {valor}
      </dd>
    </div>
  )
}

function BotaoAba({ ativo, onClick, children }: { ativo: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded px-2.5 py-1 text-xs transition-colors',
        ativo ? 'bg-carimbo-soft font-medium text-carimbo' : 'text-ink-muted hover:bg-surface-sunken hover:text-ink',
      )}
    >
      {children}
    </button>
  )
}

// ---------- Cancelamento ----------

function ModalCancelar({
  open, onClose, documentoId, aoConcluir,
}: { open: boolean; onClose: () => void; documentoId: string; aoConcluir: () => void }) {
  const toast = useToast()
  const [justificativa, setJustificativa] = useState('')
  // Uma chave por abertura do modal: um retry da mesma submissão é idempotente;
  // abrir de novo é uma intenção nova.
  const [idem] = useState(() => crypto.randomUUID())
  const mut = useMutation({
    mutationFn: () => api.cancelarDocumento(documentoId, justificativa.trim(), idem),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'Cancelamento solicitado' })
      setJustificativa('')
      aoConcluir()
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha no cancelamento', detalhe: e.message }),
  })

  return (
    <Dialog
      open={open}
      onClose={onClose}
      titulo="Cancelar documento"
      descricao="O documento passa a CANCELAMENTO_PENDENTE até a SEFAZ processar o evento 110111."
      rodape={
        <>
          <Button onClick={onClose}>Voltar</Button>
          <Button
            variant="danger"
            loading={mut.isPending}
            disabled={justificativa.trim().length < 15}
            onClick={() => mut.mutate()}
          >
            Confirmar cancelamento
          </Button>
        </>
      }
    >
      <Field
        label="Justificativa"
        htmlFor="justificativa"
        required
        hint={`Mínimo 15 caracteres — vai para a SEFAZ. (${justificativa.trim().length}/15)`}
      >
        <Textarea
          id="justificativa"
          rows={4}
          value={justificativa}
          onChange={(e) => setJustificativa(e.target.value)}
          placeholder="Descreva o motivo do cancelamento…"
        />
      </Field>
    </Dialog>
  )
}

// ---------- Carta de correção ----------

function ModalCce({
  open, onClose, documentoId, aoConcluir,
}: { open: boolean; onClose: () => void; documentoId: string; aoConcluir: () => void }) {
  const toast = useToast()
  const [correcao, setCorrecao] = useState('')
  const [idem] = useState(() => crypto.randomUUID())
  const mut = useMutation({
    mutationFn: () => api.cartaCorrecao(documentoId, correcao.trim(), idem),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'Carta de correção solicitada' })
      setCorrecao('')
      aoConcluir()
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha na CC-e', detalhe: e.message }),
  })

  return (
    <Dialog
      open={open}
      onClose={onClose}
      titulo="Carta de correção (CC-e)"
      descricao="Aplicável só a NF-e (modelo 55). Evento 110110."
      rodape={
        <>
          <Button onClick={onClose}>Voltar</Button>
          <Button
            variant="primary"
            loading={mut.isPending}
            disabled={correcao.trim().length < 15}
            onClick={() => mut.mutate()}
          >
            Enviar CC-e
          </Button>
        </>
      }
    >
      <Field
        label="Correção"
        htmlFor="correcao"
        required
        hint={`Mínimo 15 caracteres. (${correcao.trim().length}/15)`}
      >
        <Textarea
          id="correcao"
          rows={4}
          value={correcao}
          onChange={(e) => setCorrecao(e.target.value)}
          placeholder="Descreva a correção…"
        />
      </Field>
    </Dialog>
  )
}

// ---------- Reenvio com payload corrigido ----------

function ModalReenviar({
  open, onClose, documentoId, payloadOriginal, aoConcluir,
}: {
  open: boolean
  onClose: () => void
  documentoId: string
  payloadOriginal: string
  aoConcluir: () => void
}) {
  const toast = useToast()
  const formatar = (bruto: string) => {
    try {
      return JSON.stringify(JSON.parse(bruto), null, 2)
    } catch {
      return bruto
    }
  }
  const [json, setJson] = useState(() => formatar(payloadOriginal))
  const [erroSintaxe, setErroSintaxe] = useState<string | null>(null)
  const [idem] = useState(() => crypto.randomUUID())

  // Navegar entre documentos (/documentos/A -> /documentos/B) não remonta a
  // página: sem isto, o editor do documento B mostraria o payload do A — e
  // reenviar B com os itens de A é erro fiscal grave.
  useEffect(() => {
    setJson(formatar(payloadOriginal))
    setErroSintaxe(null)
  }, [payloadOriginal, documentoId])

  const mut = useMutation({
    mutationFn: async () => {
      let payload: unknown
      try {
        payload = JSON.parse(json)
        setErroSintaxe(null)
      } catch (e) {
        // Erro de sintaxe é do campo, não da SEFAZ — não vira toast de
        // "emissão rejeitada".
        setErroSintaxe(e instanceof Error ? e.message : 'JSON inválido')
        throw new Error('JSON inválido')
      }
      return api.reenviarDocumento(documentoId, payload, idem)
    },
    onSuccess: (r) => {
      toast({
        tipo: 'sucesso',
        titulo: 'Documento reenfileirado',
        detalhe: `Número ${r.serie}/${r.numero} mantido — a rejeição não consumiu numeração.`,
      })
      aoConcluir()
    },
    onError: (e) => {
      if (e.message !== 'JSON inválido') {
        toast({ tipo: 'erro', titulo: 'Falha no reenvio', detalhe: e.message })
      }
    },
  })

  return (
    <Dialog
      open={open}
      onClose={onClose}
      titulo="Corrigir e reenviar"
      descricao="Ajuste o que a SEFAZ rejeitou. O MESMO número é mantido — a rejeição não consumiu numeração."
      largura="lg"
      rodape={
        <>
          <Button onClick={onClose}>Voltar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            Reenviar
          </Button>
        </>
      }
    >
      <Field
        label="Payload de emissão (JSON)"
        htmlFor="payload-reenvio"
        error={erroSintaxe ?? undefined}
        hint="A API valida a aritmética e o ambiente antes de reenfileirar."
      >
        <Textarea
          id="payload-reenvio"
          rows={16}
          mono
          value={json}
          onChange={(e) => {
            setJson(e.target.value)
            setErroSintaxe(null)
          }}
          spellCheck={false}
        />
      </Field>
    </Dialog>
  )
}
