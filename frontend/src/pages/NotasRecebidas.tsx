import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FileCode2 } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useEmpresaContexto } from '../hooks/useEmpresaContexto'
import { formatarCnpj, formatarData, formatarDecimal } from '../lib/format'
import {
  AmbienteBadge,
  Badge,
  Button,
  Dialog,
  EmptyState,
  ErrorState,
  Field,
  PageLoading,
  Select,
  Table,
  Td,
  Th,
  Textarea,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { Paginacao } from '../components/ui/paginacao'
import { XmlViewer } from '../components/fiscal'

// ==========================================================================
// Notas recebidas — distribuição DFe.
//
// Seção inteira que não existia no painel. É onde o produto quebra
// silenciosamente: a nota chega pela SEFAZ e o ERP não sabe. Aqui o
// operador vê o que foi sincronizado e pode manifestar em nome do
// destinatário.
// ==========================================================================

const MANIFESTACOES: Record<string, { nome: string; exigeJustificativa: boolean }> = {
  '210200': { nome: 'Confirmação da operação', exigeJustificativa: false },
  '210210': { nome: 'Ciência da operação', exigeJustificativa: false },
  '210220': { nome: 'Desconhecimento da operação', exigeJustificativa: false },
  '210240': { nome: 'Operação não realizada', exigeJustificativa: true },
}

const NOME_MANIFESTACAO: Record<string, string> = {
  confirmacao: 'Confirmada',
  ciencia: 'Ciência',
  desconhecimento: 'Desconhecida',
  nao_realizacao: 'Não realizada',
}

export default function NotasRecebidas() {
  const { filtroTenant } = useEmpresaContexto()
  const [pagina, setPagina] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [manifestar, setManifestar] = useState<{ id: string; chave: string } | null>(null)
  const [verXml, setVerXml] = useState<string | null>(null)

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['notas-recebidas', filtroTenant, pagina, pageSize],
    queryFn: () => api.listNotasRecebidas({ tenantId: filtroTenant, page: pagina, pageSize }),
  })

  return (
    <>
      <PageHeader
        titulo="Notas recebidas"
        descricao="Documentos emitidos contra as empresas, sincronizados pela distribuição DFe."
      />

      <div className="rounded-lg border border-line bg-surface">
        {isLoading ? (
          <PageLoading mensagem="Carregando notas…" />
        ) : isError ? (
          <div className="p-4">
            <ErrorState
              titulo="Não foi possível carregar as notas recebidas"
              detalhe={error instanceof ApiError ? (error.detail ?? error.message) : 'Verifique se a API está rodando.'}
              aoTentarNovamente={() => refetch()}
            />
          </div>
        ) : !data || data.itens.length === 0 ? (
          <EmptyState
            titulo="Nenhuma nota recebida"
            descricao="A distribuição DFe sincroniza a cada 60s. Notas emitidas contra estas empresas aparecem aqui."
          />
        ) : (
          <>
            <Table label="Notas recebidas">
              <thead>
                <tr>
                  <Th>Emitente</Th>
                  <Th className="w-56">Chave</Th>
                  <Th className="w-28 text-right">Valor</Th>
                  <Th className="w-32">Ambiente</Th>
                  <Th className="w-36">Manifestação</Th>
                  <Th className="w-24">Schema</Th>
                  <Th className="w-36">Recebida em</Th>
                  <Th className="w-28" />
                </tr>
              </thead>
              <tbody>
                {data.itens.map((n) => (
                  <tr key={n.id} className="transition-colors hover:bg-surface-sunken/60">
                    <Td>
                      <p className="max-w-56 truncate text-[13px] text-ink" title={n.nomeEmitente ?? undefined}>
                        {n.nomeEmitente ?? '—'}
                      </p>
                      <p className="font-mono text-[10px] text-ink-faint tabular">
                        {n.cnpjEmitente ? formatarCnpj(n.cnpjEmitente) : '—'}
                      </p>
                    </Td>
                    <Td className="font-mono text-[10px] text-ink-muted tabular" title={n.chave}>
                      …{n.chave.slice(-12)}
                    </Td>
                    <Td className="text-right text-xs tabular">
                      {formatarDecimal(n.valor)}
                    </Td>
                    <Td>
                      <AmbienteBadge ambiente={n.ambiente} />
                    </Td>
                    <Td>
                      {n.manifestacaoAtual ? (
                        <Badge className="border-aprovado/30 bg-aprovado-soft text-aprovado">
                          {NOME_MANIFESTACAO[n.manifestacaoAtual] ?? n.manifestacaoAtual}
                        </Badge>
                      ) : (
                        <span className="text-xs text-ink-faint">pendente</span>
                      )}
                    </Td>
                    <Td>
                      <Badge className={n.tipoSchema === 'procNFe' ? 'border-carimbo/25 bg-carimbo-soft text-carimbo' : undefined}>
                        {n.tipoSchema}
                      </Badge>
                    </Td>
                    <Td className="text-xs text-ink-muted tabular">{formatarData(n.recebidaEm)}</Td>
                    <Td className="text-right">
                      <div className="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          title="Ver XML completo"
                          onClick={() => setVerXml(n.id)}
                        >
                          <FileCode2 className="h-3.5 w-3.5" aria-hidden />
                        </Button>
                        <Button variant="ghost" size="sm" onClick={() => setManifestar({ id: n.id, chave: n.chave })}>
                          Manifestar
                        </Button>
                      </div>
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
                aoMudarPagina={setPagina}
                aoMudarTamanho={(n) => {
                  setPageSize(n)
                  setPagina(1)
                }}
              />
            </div>
          </>
        )}
      </div>

      {manifestar && (
        <ModalManifestar
          notaId={manifestar.id}
          chave={manifestar.chave}
          onClose={() => setManifestar(null)}
        />
      )}

      {verXml && <ModalXml notaId={verXml} onClose={() => setVerXml(null)} />}
    </>
  )
}

function ModalManifestar({
  notaId, chave, onClose,
}: { notaId: string; chave: string; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [tipo, setTipo] = useState('210210')
  const [justificativa, setJustificativa] = useState('')
  const [idem] = useState(() => crypto.randomUUID())

  const exige = MANIFESTACOES[tipo]?.exigeJustificativa ?? false
  const justificativaOk = !exige || (justificativa.trim().length >= 15 && justificativa.trim().length <= 1000)

  const mut = useMutation({
    mutationFn: () => api.manifestar(notaId, tipo, exige ? justificativa.trim() : undefined, idem),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'Manifestação solicitada', detalhe: MANIFESTACOES[tipo]?.nome })
      qc.invalidateQueries({ queryKey: ['notas-recebidas'] })
      onClose()
    },
    onError: (e) =>
      toast({
        tipo: 'erro',
        titulo: 'Falha na manifestação',
        detalhe: e instanceof ApiError ? (e.detail ?? e.message) : e.message,
      }),
  })

  return (
    <Dialog
      open
      onClose={onClose}
      titulo="Manifestar nota recebida"
      descricao={`Chave …${chave.slice(-12)}. A manifestação é transmitida à SEFAZ de forma assíncrona.`}
      rodape={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} disabled={!justificativaOk} onClick={() => mut.mutate()}>
            Manifestar
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Tipo de manifestação" htmlFor="tipo-manif">
          <Select id="tipo-manif" value={tipo} onChange={(e) => setTipo(e.target.value)}>
            {Object.entries(MANIFESTACOES).map(([codigo, m]) => (
              <option key={codigo} value={codigo}>
                {codigo} — {m.nome}
              </option>
            ))}
          </Select>
        </Field>

        {exige && (
          <Field
            label="Justificativa"
            htmlFor="just-manif"
            required
            hint={`De 15 a 1000 caracteres. (${justificativa.trim().length})`}
          >
            <Textarea
              id="just-manif"
              rows={4}
              value={justificativa}
              onChange={(e) => setJustificativa(e.target.value)}
              placeholder="Descreva por que a operação não foi realizada…"
            />
          </Field>
        )}
      </div>
    </Dialog>
  )
}

function ModalXml({ notaId, onClose }: { notaId: string; onClose: () => void }) {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['nota-xml', notaId],
    queryFn: () => api.xmlCompletoNota(notaId),
    retry: false,
  })

  return (
    <Dialog open onClose={onClose} titulo="XML completo da nota" largura="xl">
      {isLoading ? (
        <PageLoading mensagem="Carregando XML…" />
      ) : isError ? (
        <ErrorState
          titulo="XML completo ainda não sincronizado"
          detalhe={
            error instanceof ApiError
              ? (error.detail ?? error.message)
              : 'A distribuição DFe devolveu apenas o resumo. O procNFe chega em ciclos posteriores.'
          }
        />
      ) : (
        <XmlViewer xml={typeof data === 'string' ? data : JSON.stringify(data)} />
      )}
    </Dialog>
  )
}
