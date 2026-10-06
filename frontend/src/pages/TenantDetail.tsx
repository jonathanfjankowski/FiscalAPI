import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { TriangleAlert, ArrowLeft } from 'lucide-react'
import { api, type ApiKeyCriada, type WebhookEntrega } from '../lib/api'
import {
  Badge, Button, Card, ConfirmDialog, Field, Input, Modal, PageHeader, PageLoading, Select,
  StatusBadge, Table, Tabs, Td, Th, Toggle,
} from '../components/ui'
import { useToast } from '../components/toast'
import { copiar, formatarCnpj, formatarData, formatarDataCurta } from '../lib/utils'
import { TenantFormModal } from './Tenants'

type Aba = 'dados' | 'keys' | 'certs' | 'sandbox' | 'webhooks'

const ABAS: { id: Aba; label: string }[] = [
  { id: 'dados', label: 'Dados fiscais' },
  { id: 'keys', label: 'API Keys' },
  { id: 'certs', label: 'Certificados' },
  { id: 'sandbox', label: 'Sandbox' },
  { id: 'webhooks', label: 'Webhooks' },
]

export default function TenantDetail() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const [aba, setAba] = useState<Aba>('dados')
  const [modalEditar, setModalEditar] = useState(false)

  const { data: tenant, isLoading } = useQuery({ queryKey: ['tenant', id], queryFn: () => api.getTenant(id) })

  if (isLoading) return <PageLoading />
  if (!tenant) return <p className="py-16 text-center text-sm text-zinc-500">Empresa não encontrada.</p>

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['tenant', id] })
    qc.invalidateQueries({ queryKey: ['tenants'] })
  }

  return (
    <>
      <PageHeader
        titulo={tenant.razaoSocial}
        descricao={`CNPJ ${formatarCnpj(tenant.cnpj)} · ${tenant.uf} · criada em ${formatarData(tenant.criadoEm)}`}
        acoes={
          <>
            <Badge
              className={
                tenant.sandbox
                  ? 'border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300'
                  : 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
              }
            >
              {tenant.sandbox ? 'Sandbox' : 'Produção real'}
            </Badge>
            <Badge
              className={
                tenant.ativo
                  ? 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                  : 'border-zinc-200 bg-zinc-100 text-zinc-500 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'
              }
            >
              {tenant.ativo ? 'Ativa' : 'Inativa'}
            </Badge>
            <Button onClick={() => setModalEditar(true)}>Editar</Button>
            <Link to="/empresas">
              <Button variant="ghost">
                <ArrowLeft className="h-3.5 w-3.5" /> Voltar
              </Button>
            </Link>
          </>
        }
      />

      <Tabs abas={ABAS} ativa={aba} onChange={setAba} />

      {aba === 'dados' && <DadosTab tenantId={id} />}
      {aba === 'keys' && <ApiKeysTab tenantId={id} onChanged={invalidate} />}
      {aba === 'certs' && <CertificadosTab tenantId={id} onChanged={invalidate} />}
      {aba === 'sandbox' && <SandboxTab tenantId={id} />}
      {aba === 'webhooks' && <WebhooksTab tenantId={id} />}

      {modalEditar && <TenantFormModal open onClose={() => setModalEditar(false)} inicial={tenant} />}
    </>
  )
}

// ------------------- Dados fiscais + CSC -------------------

function DadosTab({ tenantId }: { tenantId: string }) {
  const { data: tenant } = useQuery({ queryKey: ['tenant', tenantId], queryFn: () => api.getTenant(tenantId) })
  if (!tenant) return null

  const linhas: [string, string][] = [
    ['CNPJ', formatarCnpj(tenant.cnpj)],
    ['Inscrição estadual', tenant.inscricaoEstadual ?? '—'],
    ['UF / Município', `${tenant.uf} · ${tenant.nomeMunicipio ?? '—'} (${tenant.codigoMunicipioIbge ?? 'sem IBGE'})`],
    ['Endereço', [tenant.logradouro, tenant.numero, tenant.complemento, tenant.bairro, tenant.cep].filter(Boolean).join(', ') || '—'],
    ['Regime tributário', String(tenant.regimeTributario)],
    ['Ambiente padrão', tenant.ambientePadrao],
  ]

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card className="divide-y divide-zinc-100 dark:divide-zinc-800/60">
        {linhas.map(([k, v]) => (
          <div key={k} className="flex items-center justify-between gap-4 px-4 py-2.5">
            <span className="text-xs font-medium text-zinc-500">{k}</span>
            <span className="text-sm text-zinc-800 dark:text-zinc-200">{v}</span>
          </div>
        ))}
      </Card>
      <CscCard tenantId={tenantId} />
    </div>
  )
}

function CscCard({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { data: tenant } = useQuery({ queryKey: ['tenant', tenantId], queryFn: () => api.getTenant(tenantId) })
  const [cscId, setCscId] = useState('')
  const [csc, setCsc] = useState('')
  const [erro, setErro] = useState<string | null>(null)

  const mut = useMutation({
    mutationFn: () => api.updateTenant(tenantId, { cscId: cscId || undefined, csc: csc || undefined }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['tenant', tenantId] })
      setCscId('')
      setCsc('')
      setErro(null)
      toast({ tipo: 'sucesso', titulo: 'CSC atualizado' })
    },
    onError: (e) => setErro(e.message),
  })

  return (
    <Card className="p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">CSC / IdCSC da NFC-e</h3>
        <Badge
          className={
            tenant?.cscId
              ? 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
              : 'border-zinc-200 bg-zinc-100 text-zinc-500 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400'
          }
        >
          {tenant?.cscId ? `Id ${tenant.cscId}` : 'Não cadastrado'}
        </Badge>
      </div>
      <p className="mt-1.5 text-xs text-zinc-500">
        Emitido pela SEFAZ da UF. Necessário para emissão real de NFC-e (65). Armazenado cifrado.
      </p>
      <div className="mt-3 grid grid-cols-2 gap-3">
        <Field label="IdCSC">
          <Input value={cscId} onChange={(e) => setCscId(e.target.value)} placeholder={tenant?.cscId ?? 'ex.: 000001'} />
        </Field>
        <Field label="CSC" hint={tenant?.cscId ? 'Deixe vazio para manter o atual' : undefined}>
          <Input type="password" value={csc} onChange={(e) => setCsc(e.target.value)} placeholder="••••••••" />
        </Field>
      </div>
      {erro && <p className="mt-2 text-xs text-red-500 dark:text-red-400">{erro}</p>}
      <div className="mt-3 flex justify-end">
        <Button variant="primary" loading={mut.isPending} disabled={!cscId || (!csc && !tenant?.cscId)} onClick={() => mut.mutate()}>
          Salvar CSC
        </Button>
      </div>
    </Card>
  )
}

// ------------------- API Keys -------------------

function ApiKeysTab({ tenantId, onChanged }: { tenantId: string; onChanged: () => void }) {
  const toast = useToast()
  const { data: keys, isLoading } = useQuery({ queryKey: ['api-keys', tenantId], queryFn: () => api.listApiKeys(tenantId) })
  const [modalGerar, setModalGerar] = useState(false)
  const [criada, setCriada] = useState<ApiKeyCriada | null>(null)
  const [revogar, setRevogar] = useState<string | null>(null)

  const revogarMut = useMutation({
    mutationFn: (keyId: string) => api.revokeApiKey(tenantId, keyId),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'API key revogada' })
      setRevogar(null)
      onChanged()
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha ao revogar', detalhe: e.message }),
  })

  return (
    <>
      <Card>
        <div className="flex items-center justify-between border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
          <p className="text-xs text-zinc-500">
            A chave completa é exibida <strong className="text-zinc-700 dark:text-zinc-300">uma única vez</strong> na criação — depois só o
            prefixo.
          </p>
          <Button variant="primary" onClick={() => setModalGerar(true)}>
            Gerar API key
          </Button>
        </div>
        {isLoading ? (
          <PageLoading />
        ) : !keys || keys.length === 0 ? (
          <p className="py-10 text-center text-xs text-zinc-500">Nenhuma API key para esta empresa.</p>
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Prefixo</Th>
                <Th>Descrição</Th>
                <Th>Ambiente</Th>
                <Th>Situação</Th>
                <Th>Criada em</Th>
                <Th></Th>
              </tr>
            </thead>
            <tbody>
              {keys.map((k) => (
                <tr key={k.id}>
                  <Td className="font-mono text-xs text-zinc-900 dark:text-zinc-100">{k.prefixo}…</Td>
                  <Td>{k.descricao || <span className="text-zinc-400">—</span>}</Td>
                  <Td>
                    <Badge
                      className={
                        k.ambiente === 'producao'
                          ? 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300'
                          : 'border-sky-200 bg-sky-50 text-sky-700 dark:border-sky-500/30 dark:bg-sky-500/10 dark:text-sky-300'
                      }
                    >
                      {k.ambiente}
                    </Badge>
                  </Td>
                  <Td>
                    <Badge
                      className={
                        k.ativa
                          ? 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                          : 'border-zinc-200 bg-zinc-100 text-zinc-500 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'
                      }
                    >
                      {k.ativa ? 'Ativa' : 'Revogada'}
                    </Badge>
                  </Td>
                  <Td className="text-xs text-zinc-500">{formatarData(k.criadoEm)}</Td>
                  <Td className="text-right">
                    {k.ativa && (
                      <Button variant="ghost" className="text-red-500 hover:text-red-600 dark:text-red-400 dark:hover:text-red-300" onClick={() => setRevogar(k.id)}>
                        Revogar
                      </Button>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>

      {modalGerar && (
        <GerarKeyModal
          tenantId={tenantId}
          onClose={() => setModalGerar(false)}
          criada={(k) => {
            setCriada(k)
            onChanged()
          }}
        />
      )}

      <Modal
        open={!!criada}
        onClose={() => setCriada(null)}
        titulo="API key gerada"
        rodape={
          <>
            <Button
              variant="primary"
              onClick={async () => {
                if (criada && (await copiar(criada.chave))) toast({ tipo: 'sucesso', titulo: 'Chave copiada' })
              }}
            >
              Copiar chave
            </Button>
            <Button onClick={() => setCriada(null)}>Fechar</Button>
          </>
        }
      >
        <p className="flex items-start gap-2 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
          <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          {criada?.aviso ?? 'Copie agora — não será exibida novamente.'}
        </p>
        <code className="mt-3 block rounded-md border border-zinc-200 bg-zinc-50 px-3 py-2.5 font-mono text-xs break-all text-blue-700 select-all dark:border-zinc-800 dark:bg-zinc-950 dark:text-blue-300">
          {criada?.chave}
        </code>
      </Modal>

      <ConfirmDialog
        open={!!revogar}
        onClose={() => setRevogar(null)}
        onConfirm={() => revogar && revogarMut.mutate(revogar)}
        titulo="Revogar API key"
        mensagem="A revogação é imediata: integrações usando esta chave passam a receber 401."
        confirmarLabel="Revogar definitivamente"
        perigoso
        loading={revogarMut.isPending}
      />
    </>
  )
}

function GerarKeyModal({
  tenantId,
  onClose,
  criada,
}: {
  tenantId: string
  onClose: () => void
  criada: (k: ApiKeyCriada) => void
}) {
  const toast = useToast()
  const [descricao, setDescricao] = useState('')
  const [ambiente, setAmbiente] = useState('homologacao')

  const mut = useMutation({
    mutationFn: () => api.createApiKey(tenantId, { descricao: descricao || undefined, ambiente }),
    onSuccess: (k) => {
      criada(k)
      onClose()
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha ao gerar chave', detalhe: e.message }),
  })

  return (
    <Modal
      open
      onClose={onClose}
      titulo="Gerar API key"
      rodape={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            Gerar chave
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Descrição" hint="Para que/quem é esta chave (ex.: 'ERP Matriz')">
          <Input value={descricao} onChange={(e) => setDescricao(e.target.value)} placeholder="Integração ERP" />
        </Field>
        <Field label="Ambiente" hint="Chaves de homologação não emitem em produção (e vice-versa).">
          <Select value={ambiente} onChange={(e) => setAmbiente(e.target.value)}>
            <option value="homologacao">Homologação (fk_test_)</option>
            <option value="producao">Produção (fk_live_)</option>
          </Select>
        </Field>
      </div>
    </Modal>
  )
}

// ------------------- Certificados -------------------

function CertificadosTab({ tenantId, onChanged }: { tenantId: string; onChanged: () => void }) {
  const toast = useToast()
  const { data: certs, isLoading } = useQuery({
    queryKey: ['certificados', tenantId],
    queryFn: () => api.listCertificados(tenantId),
  })
  const [modalUpload, setModalUpload] = useState(false)

  const dias = (validoAte: string) =>
    Math.ceil((new Date(validoAte + 'T00:00:00Z').getTime() - Date.now()) / 86_400_000)

  return (
    <>
      <Card>
        <div className="flex items-center justify-between border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
          <p className="text-xs text-zinc-500">Certificado A1 (.pfx) — armazenado cifrado com AES-GCM.</p>
          <Button variant="primary" onClick={() => setModalUpload(true)}>
            Enviar .pfx
          </Button>
        </div>
        {isLoading ? (
          <PageLoading />
        ) : !certs || certs.length === 0 ? (
          <p className="py-10 text-center text-xs text-zinc-500">
            Nenhum certificado. Necessário para emissão fora do sandbox.
          </p>
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Thumbprint</Th>
                <Th>Validade</Th>
                <Th>Situação</Th>
                <Th>Enviado em</Th>
              </tr>
            </thead>
            <tbody>
              {certs.map((c) => {
                const diasRestantes = dias(c.validoAte)
                const vencendo = diasRestantes <= 30
                return (
                  <tr key={c.id}>
                    <Td className="font-mono text-xs break-all">{c.thumbprint}</Td>
                    <Td>
                      {formatarDataCurta(c.validoAte + 'T00:00:00')}{' '}
                      <span className={vencendo ? 'text-amber-600 dark:text-amber-300' : 'text-zinc-500'}>
                        ({diasRestantes}d)
                      </span>
                    </Td>
                    <Td>
                      <Badge
                        className={
                          !c.ativo
                            ? 'border-zinc-200 bg-zinc-100 text-zinc-500 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'
                            : diasRestantes <= 0
                              ? 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300'
                              : vencendo
                                ? 'border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300'
                                : 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                        }
                      >
                        {!c.ativo ? 'Inativo' : diasRestantes <= 0 ? 'Vencido' : vencendo ? 'Vencendo' : 'Válido'}
                      </Badge>
                    </Td>
                    <Td className="text-xs text-zinc-500">{formatarData(c.criadoEm)}</Td>
                  </tr>
                )
              })}
            </tbody>
          </Table>
        )}
      </Card>

      {modalUpload && (
        <UploadCertModal
          tenantId={tenantId}
          onClose={() => setModalUpload(false)}
          ok={() => {
            setModalUpload(false)
            toast({ tipo: 'sucesso', titulo: 'Certificado armazenado' })
            onChanged()
          }}
        />
      )}
    </>
  )
}

function UploadCertModal({
  tenantId,
  onClose,
  ok,
}: {
  tenantId: string
  onClose: () => void
  ok: () => void
}) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)

  const mut = useMutation({
    mutationFn: () => {
      const file = fileRef.current?.files?.[0]
      if (!file) throw new Error('Selecione o arquivo .pfx')
      return api.uploadCertificado(tenantId, file, senha)
    },
    onSuccess: ok,
    onError: (e) => setErro(e.message),
  })

  return (
    <Modal
      open
      onClose={onClose}
      titulo="Enviar certificado A1"
      rodape={
        <>
          {erro && <p className="mr-auto text-xs text-red-500 dark:text-red-400">{erro}</p>}
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            Enviar
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Arquivo .pfx">
          <input
            ref={fileRef}
            type="file"
            accept=".pfx,.p12"
            className="w-full cursor-pointer rounded-md border border-zinc-300 bg-white px-3 py-2 text-sm text-zinc-700 file:mr-3 file:rounded file:border-0 file:bg-zinc-100 file:px-3 file:py-1.5 file:text-xs file:text-zinc-700 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300 dark:file:bg-zinc-800 dark:file:text-zinc-300"
          />
        </Field>
        <Field label="Senha do certificado">
          <Input type="password" value={senha} onChange={(e) => setSenha(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}

// ------------------- Sandbox -------------------

function SandboxTab({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { data: tenant } = useQuery({ queryKey: ['tenant', tenantId], queryFn: () => api.getTenant(tenantId) })
  const [confirmar, setConfirmar] = useState<boolean | null>(null)

  const mut = useMutation({
    mutationFn: (sandbox: boolean) => api.updateTenant(tenantId, { sandbox }),
    onSuccess: (_, sandbox) => {
      qc.invalidateQueries({ queryKey: ['tenant', tenantId] })
      qc.invalidateQueries({ queryKey: ['tenants'] })
      setConfirmar(null)
      toast({
        tipo: 'sucesso',
        titulo: sandbox ? 'Sandbox ativado' : 'Sandbox desativado',
        detalhe: sandbox ? 'Emissores passam a usar o mock — nada é transmitido à SEFAZ.' : 'Emissores reais passam a transmitir à SEFAZ.',
      })
    },
    onError: (e) => {
      setConfirmar(null)
      toast({ tipo: 'erro', titulo: 'Falha ao alterar sandbox', detalhe: e.message })
    },
  })

  if (!tenant) return null
  const sandbox = tenant.sandbox

  return (
    <>
      <Card className="max-w-2xl p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Modo sandbox</h3>
            <p className="mt-1 text-xs leading-relaxed text-zinc-500">
              {sandbox
                ? 'Ativo: toda emissão/evento/consulta desta empresa usa o emissor mock — nenhum tráfego chega à SEFAZ e o certificado A1 é dispensado. Ideal para testes de integração com o ERP.'
                : 'Inativo: a empresa emite de verdade — o XML é assinado com o certificado A1 e transmitido à SEFAZ do ambiente configurado (homologação/produção). Certificado A1 é obrigatório.'}
            </p>
          </div>
          <Toggle checked={sandbox} onChange={(v) => setConfirmar(v)} />
        </div>
        <div
          className={`mt-4 rounded-md border px-3 py-2.5 text-xs leading-relaxed ${
            sandbox
              ? 'border-amber-200 bg-amber-50 text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200'
              : 'border-red-200 bg-red-50 text-red-800 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-200'
          }`}
        >
          {sandbox
            ? 'Ao desativar, confirme que a empresa tem certificado A1 válido e dados fiscais completos — tentativas sem certificado falham com ERRO_INTERNO.'
            : 'Esta empresa está emitindo documentos reais. Alterações aqui afetam imediatamente os novos processamentos em fila.'}
        </div>
      </Card>

      <ConfirmDialog
        open={confirmar !== null}
        onClose={() => setConfirmar(null)}
        onConfirm={() => confirmar !== null && mut.mutate(confirmar)}
        titulo={confirmar ? 'Ativar sandbox' : 'Desativar sandbox'}
        mensagem={
          confirmar
            ? 'A empresa passa a emitir apenas em modo de teste (mock). Nada é transmitido à SEFAZ.'
            : 'A empresa passa a transmitir documentos reais à SEFAZ. Confirma?'
        }
        confirmarLabel={confirmar ? 'Ativar sandbox' : 'Desativar sandbox'}
        perigoso={confirmar === false}
        loading={mut.isPending}
      />
    </>
  )
}

// ------------------- Webhooks -------------------

function WebhooksTab({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { data: tenant } = useQuery({ queryKey: ['tenant', tenantId], queryFn: () => api.getTenant(tenantId) })
  const [url, setUrl] = useState<string | null>(null)
  const [secret, setSecret] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [statusFiltro, setStatusFiltro] = useState('')

  const mut = useMutation({
    mutationFn: () =>
      api.updateTenant(tenantId, {
        webhookUrl: url ?? undefined,
        webhookSecret: secret || undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['tenant', tenantId] })
      setUrl(null)
      setSecret('')
      setErro(null)
      toast({ tipo: 'sucesso', titulo: 'Webhook atualizado' })
    },
    onError: (e) => setErro(e.message),
  })

  const { data: entregas, isLoading: carregandoEntregas } = useQuery({
    queryKey: ['webhook-entregas', tenantId, statusFiltro],
    queryFn: () => api.listWebhookEntregas(tenantId, { status: statusFiltro || undefined }),
    refetchInterval: 15_000,
  })

  const reenviarMut = useMutation({
    mutationFn: (entregaId: string) => api.reenviarWebhookEntrega(tenantId, entregaId),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'Reenvio solicitado' })
      qc.invalidateQueries({ queryKey: ['webhook-entregas', tenantId] })
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha no reenvio', detalhe: e.message }),
  })

  if (!tenant) return null

  const urlAtual = url !== null ? url : (tenant.webhookUrl ?? '')

  return (
    <div className="space-y-4">
      <Card className="p-4">
        <h3 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Configuração</h3>
        <p className="mt-1 text-xs text-zinc-500">
          As entregas são assinadas com HMAC-SHA256 (header X-Fiscal-Signature) e têm retry próprio
          (até 8 tentativas). Fora do sandbox, a URL exige https.
        </p>
        <div className="mt-3 grid gap-3 lg:grid-cols-2">
          <Field label="Webhook URL">
            <Input value={urlAtual} onChange={(e) => setUrl(e.target.value)} placeholder="https://erp.exemplo.com/hooks/fiscal" />
          </Field>
          <Field label="Webhook Secret" hint="Mínimo 16 caracteres. Vazio mantém o atual.">
            <Input type="password" value={secret} onChange={(e) => setSecret(e.target.value)} placeholder="••••••••" />
          </Field>
        </div>
        {erro && <p className="mt-2 text-xs text-red-500 dark:text-red-400">{erro}</p>}
        <div className="mt-3 flex justify-end">
          <Button variant="primary" loading={mut.isPending} disabled={url === null && !secret} onClick={() => mut.mutate()}>
            Salvar webhook
          </Button>
        </div>
      </Card>

      <Card>
        <div className="flex items-center justify-between border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
          <p className="text-xs text-zinc-500">Entregas (outbox) — atualiza a cada 15s</p>
          <Select value={statusFiltro} onChange={(e) => setStatusFiltro(e.target.value)} className="w-40">
            <option value="">Todos os status</option>
            <option value="PENDENTE">Pendente</option>
            <option value="ENTREGANDO">Entregando</option>
            <option value="ENTREGUE">Entregue</option>
            <option value="FALHA">Falha</option>
          </Select>
        </div>
        {carregandoEntregas ? (
          <PageLoading />
        ) : !entregas || entregas.itens.length === 0 ? (
          <p className="py-10 text-center text-xs text-zinc-500">Nenhuma entrega registrada.</p>
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Evento</Th>
                <Th>Status</Th>
                <Th className="text-center">Tentativas</Th>
                <Th>Último HTTP</Th>
                <Th>Erro</Th>
                <Th>Criada em</Th>
                <Th></Th>
              </tr>
            </thead>
            <tbody>
              {entregas.itens.map((w: WebhookEntrega) => (
                <tr key={w.id}>
                  <Td className="font-mono text-xs">{w.tipoEvento}</Td>
                  <Td>
                    <StatusBadge status={w.status} />
                  </Td>
                  <Td className="text-center tabular-nums">{w.tentativas}</Td>
                  <Td className="tabular-nums">{w.ultimoStatusCode ?? '—'}</Td>
                  <Td className="max-w-72 truncate text-xs text-zinc-500" title={w.ultimoErro ?? undefined}>
                    {w.ultimoErro ?? '—'}
                  </Td>
                  <Td className="text-xs text-zinc-500">{formatarData(w.criadoEm)}</Td>
                  <Td className="text-right">
                    {(w.status === 'FALHA' || w.status === 'PENDENTE') && (
                      <Button variant="ghost" loading={reenviarMut.isPending} onClick={() => reenviarMut.mutate(w.id)}>
                        Reenviar
                      </Button>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>
    </div>
  )
}
