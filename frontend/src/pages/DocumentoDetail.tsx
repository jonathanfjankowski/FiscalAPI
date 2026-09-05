import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api } from '../lib/api'
import { Button, Card, Field, Modal, PageHeader, PageLoading, StatusBadge, Textarea } from '../components/ui'
import { useToast } from '../components/toast'
import { copiar, formatarData, formatarXml, STATUS_TRANSITORIOS } from '../lib/utils'

export default function DocumentoDetail() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const toast = useToast()
  const [modalCancel, setModalCancel] = useState(false)
  const [modalCce, setModalCce] = useState(false)

  const { data: doc, isLoading } = useQuery({
    queryKey: ['documento', id],
    queryFn: () => api.getDocumento(id),
    refetchInterval: (query) =>
      query.state.data && STATUS_TRANSITORIOS.includes(query.state.data.status) ? 3000 : false,
  })

  const invalidate = () => qc.invalidateQueries({ queryKey: ['documento', id] })

  if (isLoading) return <PageLoading />
  if (!doc) return <p className="py-16 text-center text-sm text-zinc-500">Documento não encontrado.</p>

  return (
    <>
      <PageHeader
        titulo={doc.serie !== null ? `${doc.tipo === 'NFE' ? 'NF-e' : 'NFC-e'} ${doc.serie}/${String(doc.numero ?? 0).padStart(9, '0')}` : doc.tipo}
        descricao={`${doc.tenantRazaoSocial} · criado em ${formatarData(doc.criadoEm)} · atualizado em ${formatarData(doc.atualizadoEm)}`}
        acoes={
          <>
            <StatusBadge status={doc.status} />
            <Link to="/documentos">
              <Button variant="ghost">← Voltar</Button>
            </Link>
          </>
        }
      />

      {doc.status === 'AUTORIZADA' && (
        <div className="mb-4 flex flex-wrap gap-2">
          <Button variant="danger" onClick={() => setModalCancel(true)}>
            Cancelar documento
          </Button>
          {doc.modelo === 55 && (
            <Button onClick={() => setModalCce(true)}>Carta de correção</Button>
          )}
        </div>
      )}

      {doc.status === 'CONTINGENCIA' && (
        <Card className="mb-4 border-amber-500/30 bg-amber-500/5 p-4 text-xs text-amber-200">
          Documento em contingência — nova tentativa{' '}
          {doc.proximaTentativaEm ? formatarData(doc.proximaTentativaEm) : 'em breve'} (tentativa{' '}
          {doc.tentativas + 1}). O VarrerContingenciaJob do Worker reenfileira a cada 30s.
        </Card>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="divide-y divide-zinc-800/60">
          <Linha titulo="Status" valor={<StatusBadge status={doc.status} />} />
          <Linha titulo="Motivo" valor={doc.motivoStatus ?? '—'} />
          <Linha
            titulo="Chave de acesso"
            valor={
              doc.chaveAcesso ? (
                <ChaveCopiavel texto={doc.chaveAcesso} />
              ) : (
                '—'
              )
            }
          />
          <Linha titulo="Protocolo de autorização" valor={doc.protocoloAutorizacao ?? '—'} />
          <Linha titulo="Recibo do lote" valor={doc.reciboLote ?? '—'} />
        </Card>

        <Card className="divide-y divide-zinc-800/60">
          <Linha titulo="Tenant" valor={doc.tenantRazaoSocial} />
          <Linha titulo="Modelo / Série / Número" valor={`${doc.modelo ?? '—'} / ${doc.serie ?? '—'} / ${doc.numero ?? '—'}`} />
          <Linha titulo="Ambiente" valor={doc.ambiente} />
          <Linha titulo="Tentativas de transmissão" valor={String(doc.tentativas)} />
          <Linha titulo="Documento ID" valor={<code className="text-xs text-zinc-400">{doc.id}</code>} />
        </Card>
      </div>

      {(doc.xmlAssinado || doc.xmlRetornoSefaz) && (
        <Card className="mt-4">
          <XmlTabs assinado={doc.xmlAssinado} retorno={doc.xmlRetornoSefaz} />
        </Card>
      )}

      <ModalCancelamento
        open={modalCancel}
        onClose={() => setModalCancel(false)}
        onOk={() => {
          setModalCancel(false)
          toast({ tipo: 'sucesso', titulo: 'Cancelamento solicitado' })
          invalidate()
        }}
        documentoId={doc.id}
      />
      <ModalCCe
        open={modalCce}
        onClose={() => setModalCce(false)}
        onOk={() => {
          setModalCce(false)
          toast({ tipo: 'sucesso', titulo: 'Carta de correção solicitada' })
          invalidate()
        }}
        documentoId={doc.id}
      />
    </>
  )
}

function Linha({ titulo, valor }: { titulo: string; valor: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 px-4 py-3">
      <span className="text-xs font-medium text-zinc-500">{titulo}</span>
      <span className="text-right text-sm text-zinc-200">{valor}</span>
    </div>
  )
}

function ChaveCopiavel({ texto }: { texto: string }) {
  return (
    <button
      className="group inline-flex items-center gap-2 font-mono text-xs text-zinc-300 hover:text-emerald-300"
      title="Copiar chave"
      onClick={async () => {
        if (await copiar(texto)) {
          // feedback mínimo via cor; toast global ficaria redundante aqui
        }
      }}
    >
      {texto}
      <span className="opacity-0 transition-opacity group-hover:opacity-100">⧉</span>
    </button>
  )
}

function XmlTabs({ assinado, retorno }: { assinado: string | null; retorno: string | null }) {
  const [aba, setAba] = useState<'assinado' | 'retorno'>(assinado ? 'assinado' : 'retorno')
  const xml = aba === 'assinado' ? assinado : retorno

  return (
    <div>
      <div className="flex gap-1 border-b border-zinc-800 px-4 pt-3">
        {assinado && (
          <TabAtivo ativo={aba === 'assinado'} onClick={() => setAba('assinado')}>
            XML assinado
          </TabAtivo>
        )}
        {retorno && (
          <TabAtivo ativo={aba === 'retorno'} onClick={() => setAba('retorno')}>
            XML retorno SEFAZ
          </TabAtivo>
        )}
      </div>
      <pre className="max-h-96 overflow-auto px-4 py-3 text-[11px] leading-relaxed text-zinc-400">
        {formatarXml(xml)}
      </pre>
    </div>
  )
}

function TabAtivo({ ativo, onClick, children }: { ativo: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={`-mb-px border-b-2 px-3 py-1.5 text-xs transition-colors ${
        ativo ? 'border-emerald-500 font-medium text-emerald-300' : 'border-transparent text-zinc-500 hover:text-zinc-300'
      }`}
    >
      {children}
    </button>
  )
}

function ModalCancelamento({
  open, onClose, onOk, documentoId,
}: { open: boolean; onClose: () => void; onOk: () => void; documentoId: string }) {
  const toast = useToast()
  const [justificativa, setJustificativa] = useState('')
  const mut = useMutation({
    mutationFn: () => api.cancelarDocumento(documentoId, justificativa.trim()),
    onSuccess: onOk,
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha no cancelamento', detalhe: e.message }),
  })

  return (
    <Modal
      open={open}
      onClose={onClose}
      titulo="Cancelar documento"
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
      <Field label="Justificativa *" hint="Mínimo 15 caracteres — vai para a SEFAZ no evento 110111.">
        <Textarea
          rows={4}
          value={justificativa}
          onChange={(e) => setJustificativa(e.target.value)}
          placeholder="Descreva o motivo do cancelamento…"
          className="font-sans text-sm"
        />
      </Field>
      <p className="mt-2 text-[11px] text-zinc-500">
        O documento passa para CANCELAMENTO_PENDENTE até a transmissão do evento.
      </p>
    </Modal>
  )
}

function ModalCCe({
  open, onClose, onOk, documentoId,
}: { open: boolean; onClose: () => void; onOk: () => void; documentoId: string }) {
  const toast = useToast()
  const [correcao, setCorrecao] = useState('')
  const mut = useMutation({
    mutationFn: () => api.cartaCorrecao(documentoId, correcao.trim()),
    onSuccess: onOk,
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha na CC-e', detalhe: e.message }),
  })

  return (
    <Modal
      open={open}
      onClose={onClose}
      titulo="Carta de correção (CC-e)"
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
      <Field label="Correção *" hint="Mínimo 15 caracteres. Aplicável só a NF-e (modelo 55).">
        <Textarea
          rows={4}
          value={correcao}
          onChange={(e) => setCorrecao(e.target.value)}
          placeholder="Descreva a correção…"
          className="font-sans text-sm"
        />
      </Field>
    </Modal>
  )
}
