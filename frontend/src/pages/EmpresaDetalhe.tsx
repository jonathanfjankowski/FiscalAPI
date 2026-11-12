import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Plus, Power, PowerOff, RefreshCw, Upload } from 'lucide-react'
import { api, ApiError, type Tenant } from '../lib/api'
import { formatarCnpj, formatarData, formatarDataCurta } from '../lib/format'
import { cn } from '../lib/utils'
import {
  AmbienteBadge,
  Badge,
  Button,
  ConfirmDialog,
  Dialog,
  EmptyState,
  ErrorState,
  Field,
  Input,
  PageLoading,
  Select,
  StatusBadge,
  Switch,
  Table,
  Tabs,
  Td,
  Th,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { SegredoUnico } from '../components/ui/segredo'

// ==========================================================================
// Detalhe da empresa.
//
// As abas vivem na URL (?tab=) — dá para mandar "os certificados da Acme"
// como link, e o F5 não volta para a primeira aba.
// ==========================================================================

const ABAS = [
  { id: 'dados', label: 'Dados fiscais' },
  { id: 'csc', label: 'CSC (NFC-e)' },
  { id: 'keys', label: 'API keys' },
  { id: 'certs', label: 'Certificados' },
  { id: 'sandbox', label: 'Sandbox' },
  { id: 'webhooks', label: 'Webhooks' },
] as const

type Aba = (typeof ABAS)[number]['id']

export default function EmpresaDetalhe() {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const abaParam = params.get('tab')
  const aba: Aba = ABAS.some((a) => a.id === abaParam) ? (abaParam as Aba) : 'dados'

  const { data: tenant, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['tenant', id],
    queryFn: () => api.getTenant(id),
  })

  if (isLoading) return <PageLoading mensagem="Carregando empresa…" />
  if (isError || !tenant) {
    return (
      <ErrorState
        titulo="Não foi possível carregar a empresa"
        detalhe={error instanceof ApiError ? (error.detail ?? error.message) : 'Empresa não encontrada.'}
        aoTentarNovamente={() => refetch()}
      />
    )
  }

  return (
    <>
      <PageHeader
        titulo={tenant.razaoSocial}
        descricao={`CNPJ ${formatarCnpj(tenant.cnpj)} · ${tenant.uf} · criada em ${formatarData(tenant.criadoEm)}`}
        acoes={
          <>
            <AmbienteBadge ambiente={tenant.ambientePadrao} />
            <Badge
              className={
                tenant.sandbox
                  ? 'border-contingencia/30 bg-contingencia-soft text-contingencia'
                  : 'border-aprovado/30 bg-aprovado-soft text-aprovado'
              }
            >
              {tenant.sandbox ? 'Sandbox' : 'Produção real'}
            </Badge>
            {!tenant.ativo && <Badge>Inativa</Badge>}
            <Button asChild variant="ghost">
              <Link to="/empresas">
                <ArrowLeft className="h-3.5 w-3.5" aria-hidden />
                Voltar
              </Link>
            </Button>
          </>
        }
      />

      <Tabs
        abas={ABAS.map((a) => ({ id: a.id, label: a.label }))}
        ativa={aba}
        onChange={(nova) => {
          const proximo = new URLSearchParams(params)
          if (nova === 'dados') proximo.delete('tab')
          else proximo.set('tab', nova)
          setParams(proximo, { replace: true })
        }}
        className="mb-4"
      />

      {aba === 'dados' && <AbaDados tenant={tenant} />}
      {aba === 'csc' && <AbaCsc tenant={tenant} />}
      {aba === 'keys' && <AbaKeys tenantId={id} />}
      {aba === 'certs' && <AbaCertificados tenantId={id} sandbox={tenant.sandbox} />}
      {aba === 'sandbox' && <AbaSandbox tenant={tenant} />}
      {aba === 'webhooks' && <AbaWebhooks tenant={tenant} />}
    </>
  )
}

// ---------- Dados fiscais ----------

function AbaDados({ tenant }: { tenant: Tenant }) {
  const linhas: [string, string][] = [
    ['CNPJ', formatarCnpj(tenant.cnpj)],
    ['Inscrição estadual', tenant.inscricaoEstadual ?? '—'],
    ['UF / Município', `${tenant.uf} · ${tenant.nomeMunicipio ?? '—'} (${tenant.codigoMunicipioIbge ?? 'sem IBGE'})`],
    [
      'Endereço',
      [tenant.logradouro, tenant.numero, tenant.complemento, tenant.bairro, tenant.cep]
        .filter(Boolean)
        .join(', ') || '—',
    ],
    ['Regime tributário', String(tenant.regimeTributario)],
    ['Ambiente padrão', tenant.ambientePadrao === 'producao' ? 'Produção' : 'Homologação'],
  ]

  return (
    <div className="max-w-2xl rounded-lg border border-line bg-surface">
      <dl className="divide-y divide-line">
        {linhas.map(([rotulo, valor]) => (
          <div key={rotulo} className="flex items-baseline justify-between gap-4 px-4 py-2.5">
            <dt className="shrink-0 text-[11px] text-ink-muted">{rotulo}</dt>
            <dd className="text-right text-[13px] text-ink">{valor}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}

// ---------- CSC ----------

function AbaCsc({ tenant }: { tenant: Tenant }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [cscId, setCscId] = useState('')
  const [csc, setCsc] = useState('')
  const [erro, setErro] = useState<string | null>(null)

  const mut = useMutation({
    mutationFn: () =>
      api.updateTenant(tenant.id, { cscId: cscId || undefined, csc: csc || undefined }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['tenant', tenant.id] })
      setCscId('')
      setCsc('')
      setErro(null)
      toast({ tipo: 'sucesso', titulo: 'CSC atualizado' })
    },
    onError: (e) => setErro(e instanceof ApiError ? (e.detail ?? e.message) : e.message),
  })

  const configurado = !!tenant.cscId

  return (
    <div className="max-w-2xl rounded-lg border border-line bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-[13px] font-semibold text-ink">CSC / IdCSC da NFC-e</h2>
          <p className="mt-1 text-xs leading-relaxed text-ink-muted">
            Emitido pela SEFAZ da UF. Necessário para emissão real de NFC-e (65). O CSC é
            armazenado cifrado e nunca é devolvido.
          </p>
        </div>
        <Badge
          className={
            configurado
              ? 'border-aprovado/30 bg-aprovado-soft text-aprovado'
              : 'border-line-strong bg-surface-sunken text-ink-faint'
          }
        >
          {configurado ? `Id ${tenant.cscId}` : 'Não cadastrado'}
        </Badge>
      </div>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <Field label="IdCSC" htmlFor="cscid" hint={configurado ? 'Vazio mantém o atual' : undefined}>
          <Input
            id="cscid"
            value={cscId}
            onChange={(e) => setCscId(e.target.value)}
            placeholder={tenant.cscId ?? 'ex.: 000001'}
            className="font-mono"
          />
        </Field>
        <Field label="CSC" htmlFor="csc" hint="Vazio mantém o atual">
          <Input id="csc" type="password" value={csc} onChange={(e) => setCsc(e.target.value)} placeholder="••••••••" />
        </Field>
      </div>

      {erro && <p className="mt-2 text-xs text-rejeitado">{erro}</p>}

      <div className="mt-4 flex justify-end">
        <Button
          variant="primary"
          loading={mut.isPending}
          disabled={!cscId && !csc}
          onClick={() => mut.mutate()}
        >
          Salvar CSC
        </Button>
      </div>
    </div>
  )
}

// ---------- API keys ----------

function AbaKeys({ tenantId }: { tenantId: string }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [gerar, setGerar] = useState(false)
  const [criada, setCriada] = useState<{ chave: string; aviso: string } | null>(null)
  const [revogar, setRevogar] = useState<string | null>(null)

  const { data: keys, isLoading, isError, refetch } = useQuery({
    queryKey: ['api-keys', tenantId],
    queryFn: () => api.listApiKeys(tenantId),
  })

  const mutRevogar = useMutation({
    mutationFn: (keyId: string) => api.revokeApiKey(tenantId, keyId),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'API key revogada' })
      setRevogar(null)
      qc.invalidateQueries({ queryKey: ['api-keys', tenantId] })
      qc.invalidateQueries({ queryKey: ['tenants'] })
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha ao revogar', detalhe: e.message }),
  })

  return (
    <>
      <div className="rounded-lg border border-line bg-surface">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-2.5">
          <p className="text-xs text-ink-muted">
            A chave completa é exibida <strong className="text-ink">uma única vez</strong> na criação — depois, só o prefixo.
          </p>
          <Button variant="primary" size="sm" onClick={() => setGerar(true)}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Gerar API key
          </Button>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : isError ? (
          <div className="p-4">
            <ErrorState titulo="Não foi possível carregar as chaves" aoTentarNovamente={() => refetch()} />
          </div>
        ) : !keys || keys.length === 0 ? (
          <EmptyState titulo="Nenhuma API key" descricao="Gere a primeira para o integrador autenticar." />
        ) : (
          <Table label="API keys">
            <thead>
              <tr>
                <Th>Prefixo</Th>
                <Th>Descrição</Th>
                <Th className="w-32">Ambiente</Th>
                <Th className="w-28">Situação</Th>
                <Th className="w-36">Criada em</Th>
                <Th className="w-24" />
              </tr>
            </thead>
            <tbody>
              {keys.map((k) => (
                <tr key={k.id}>
                  <Td className="font-mono text-xs">{k.prefixo}…</Td>
                  <Td className="text-xs">{k.descricao || <span className="text-ink-faint">—</span>}</Td>
                  <Td>
                    <AmbienteBadge ambiente={k.ambiente} />
                  </Td>
                  <Td>
                    <Badge
                      className={
                        k.ativa
                          ? 'border-aprovado/30 bg-aprovado-soft text-aprovado'
                          : 'border-line-strong bg-surface-sunken text-ink-faint'
                      }
                    >
                      {k.ativa ? 'Ativa' : 'Revogada'}
                    </Badge>
                  </Td>
                  <Td className="text-xs text-ink-muted tabular">{formatarData(k.criadoEm)}</Td>
                  <Td className="text-right">
                    {k.ativa && (
                      <Button variant="ghost" size="sm" onClick={() => setRevogar(k.id)}>
                        Revogar
                      </Button>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </div>

      {gerar && (
        <GerarKeyModal
          tenantId={tenantId}
          onClose={() => setGerar(false)}
          aoCriar={(k) => {
            setCriada({ chave: k.chave, aviso: k.aviso })
            setGerar(false)
            qc.invalidateQueries({ queryKey: ['api-keys', tenantId] })
            qc.invalidateQueries({ queryKey: ['tenants'] })
          }}
        />
      )}

      <Dialog
        open={!!criada}
        onClose={() => setCriada(null)}
        titulo="API key gerada"
        rodape={<Button variant="primary" onClick={() => setCriada(null)}>Fechar</Button>}
      >
        {criada && <SegredoUnico valor={criada.chave} aviso={criada.aviso} />}
      </Dialog>

      <ConfirmDialog
        open={!!revogar}
        onClose={() => setRevogar(null)}
        onConfirm={() => revogar && mutRevogar.mutate(revogar)}
        titulo="Revogar API key"
        mensagem="A revogação é imediata: integrações usando esta chave passam a receber 401."
        confirmarLabel="Revogar definitivamente"
        perigoso
        loading={mutRevogar.isPending}
      />
    </>
  )
}

function GerarKeyModal({
  tenantId, onClose, aoCriar,
}: {
  tenantId: string
  onClose: () => void
  aoCriar: (k: { chave: string; aviso: string }) => void
}) {
  const [descricao, setDescricao] = useState('')
  const [ambiente, setAmbiente] = useState('homologacao')
  const [erro, setErro] = useState<string | null>(null)

  const mut = useMutation({
    mutationFn: () => api.createApiKey(tenantId, { descricao: descricao || undefined, ambiente }),
    onSuccess: (k) => aoCriar(k),
    onError: (e) => setErro(e instanceof ApiError ? (e.detail ?? e.message) : e.message),
  })

  return (
    <Dialog
      open
      onClose={onClose}
      titulo="Gerar API key"
      rodape={
        <>
          {erro && <p className="mr-auto text-xs text-rejeitado">{erro}</p>}
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            Gerar chave
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Descrição" htmlFor="desc-key" hint="Para que/quem é esta chave (ex.: 'ERP Matriz')">
          <Input id="desc-key" value={descricao} onChange={(e) => setDescricao(e.target.value)} placeholder="Integração ERP" />
        </Field>
        <Field label="Ambiente" htmlFor="amb-key" hint="Chaves de homologação não emitem em produção (e vice-versa).">
          <Select id="amb-key" value={ambiente} onChange={(e) => setAmbiente(e.target.value)}>
            <option value="homologacao">Homologação (fk_test_)</option>
            <option value="producao">Produção (fk_live_)</option>
          </Select>
        </Field>
      </div>
    </Dialog>
  )
}

// ---------- Certificados ----------

function AbaCertificados({ tenantId, sandbox }: { tenantId: string; sandbox: boolean }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [upload, setUpload] = useState(false)
  const [alternar, setAlternar] = useState<{ id: string; ativar: boolean } | null>(null)

  const { data: certs, isLoading, isError, refetch } = useQuery({
    queryKey: ['certificados', tenantId],
    queryFn: () => api.listCertificados(tenantId),
  })

  const invalidar = () => {
    qc.invalidateQueries({ queryKey: ['certificados', tenantId] })
    qc.invalidateQueries({ queryKey: ['tenants'] })
    qc.invalidateQueries({ queryKey: ['tenant', tenantId] })
  }

  const mutAlternar = useMutation({
    mutationFn: async ({ id, ativar }: { id: string; ativar: boolean }) => {
      if (ativar) await api.ativarCertificado(tenantId, id)
      else await api.disableCertificado(tenantId, id)
    },
    onSuccess: (_, v) => {
      toast({ tipo: 'sucesso', titulo: v.ativar ? 'Certificado ativado' : 'Certificado desativado' })
      setAlternar(null)
      invalidar()
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha na operação', detalhe: e.message }),
  })

  const diasRestantes = (validoAte: string) =>
    Math.ceil((new Date(validoAte + 'T00:00:00Z').getTime() - Date.now()) / 86_400_000)

  return (
    <>
      <div className="rounded-lg border border-line bg-surface">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-2.5">
          <p className="text-xs text-ink-muted">
            Certificado A1 (.pfx), armazenado cifrado com AES-GCM.
            {!sandbox && <span className="ml-1 text-rejeitado">Obrigatório para emissão real.</span>}
          </p>
          <Button variant="primary" size="sm" onClick={() => setUpload(true)}>
            <Upload className="h-3.5 w-3.5" aria-hidden />
            Enviar .pfx
          </Button>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : isError ? (
          <div className="p-4">
            <ErrorState titulo="Não foi possível carregar os certificados" aoTentarNovamente={() => refetch()} />
          </div>
        ) : !certs || certs.length === 0 ? (
          <EmptyState
            titulo="Nenhum certificado"
            descricao={sandbox ? 'Em sandbox o certificado é dispensado.' : 'Sem certificado ativo a emissão real falha.'}
          />
        ) : (
          <Table label="Certificados A1">
            <thead>
              <tr>
                <Th>Thumbprint</Th>
                <Th className="w-44">Validade</Th>
                <Th className="w-28">Situação</Th>
                <Th className="w-36">Enviado em</Th>
                <Th className="w-28" />
              </tr>
            </thead>
            <tbody>
              {certs.map((c) => {
                const dias = diasRestantes(c.validoAte)
                const vencido = dias <= 0
                const vencendo = !vencido && dias <= 30
                return (
                  <tr key={c.id}>
                    <Td className="font-mono text-[11px] break-all">{c.thumbprint}</Td>
                    <Td className="text-xs tabular">
                      {formatarDataCurta(c.validoAte + 'T00:00:00')}{' '}
                      <span className={cn(vencido ? 'text-rejeitado' : vencendo ? 'text-contingencia' : 'text-ink-faint')}>
                        ({dias}d)
                      </span>
                    </Td>
                    <Td>
                      <Badge
                        className={
                          !c.ativo
                            ? 'border-line-strong bg-surface-sunken text-ink-faint'
                            : vencido
                              ? 'border-rejeitado/30 bg-rejeitado-soft text-rejeitado'
                              : vencendo
                                ? 'border-contingencia/30 bg-contingencia-soft text-contingencia'
                                : 'border-aprovado/30 bg-aprovado-soft text-aprovado'
                        }
                      >
                        {!c.ativo ? 'Inativo' : vencido ? 'Vencido' : vencendo ? 'Vencendo' : 'Válido'}
                      </Badge>
                    </Td>
                    <Td className="text-xs text-ink-muted tabular">{formatarData(c.criadoEm)}</Td>
                    <Td className="text-right">
                      {c.ativo ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          title="Desativar certificado"
                          onClick={() => setAlternar({ id: c.id, ativar: false })}
                        >
                          <PowerOff className="h-3.5 w-3.5" aria-hidden />
                        </Button>
                      ) : (
                        <Button
                          variant="ghost"
                          size="sm"
                          title="Ativar certificado"
                          onClick={() => setAlternar({ id: c.id, ativar: true })}
                        >
                          <Power className="h-3.5 w-3.5" aria-hidden />
                        </Button>
                      )}
                    </Td>
                  </tr>
                )
              })}
            </tbody>
          </Table>
        )}
      </div>

      {upload && (
        <UploadCertModal
          tenantId={tenantId}
          onClose={() => setUpload(false)}
          aoEnviar={() => {
            setUpload(false)
            toast({ tipo: 'sucesso', titulo: 'Certificado armazenado' })
            invalidar()
          }}
        />
      )}

      <ConfirmDialog
        open={!!alternar}
        onClose={() => setAlternar(null)}
        onConfirm={() => alternar && mutAlternar.mutate(alternar)}
        titulo={alternar?.ativar ? 'Ativar certificado' : 'Desativar certificado'}
        mensagem={
          alternar?.ativar
            ? 'Este certificado passa a ser o ativo da empresa; os outros ativos são desativados.'
            : 'O certificado deixa de ser usado. Sem certificado ativo, a emissão real falha.'
        }
        confirmarLabel={alternar?.ativar ? 'Ativar' : 'Desativar'}
        perigoso={!alternar?.ativar}
        loading={mutAlternar.isPending}
      />
    </>
  )
}

function UploadCertModal({
  tenantId, onClose, aoEnviar,
}: { tenantId: string; onClose: () => void; aoEnviar: () => void }) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)

  const mut = useMutation({
    mutationFn: () => {
      const file = fileRef.current?.files?.[0]
      if (!file) throw new Error('Selecione o arquivo .pfx')
      return api.uploadCertificado(tenantId, file, senha)
    },
    onSuccess: aoEnviar,
    onError: (e) => setErro(e instanceof ApiError ? (e.detail ?? e.message) : e.message),
  })

  return (
    <Dialog
      open
      onClose={onClose}
      titulo="Enviar certificado A1"
      descricao="O .pfx é cifrado com AES-GCM antes de ser armazenado."
      rodape={
        <>
          {erro && <p className="mr-auto text-xs text-rejeitado">{erro}</p>}
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            Enviar
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Arquivo .pfx" htmlFor="pfx">
          <input
            id="pfx"
            ref={fileRef}
            type="file"
            accept=".pfx,.p12"
            className="w-full cursor-pointer rounded border border-line bg-surface px-3 py-2 text-xs text-ink file:mr-3 file:rounded file:border-0 file:bg-surface-sunken file:px-3 file:py-1 file:text-xs file:text-ink"
          />
        </Field>
        <Field label="Senha do certificado" htmlFor="senha-pfx">
          <Input id="senha-pfx" type="password" value={senha} onChange={(e) => setSenha(e.target.value)} />
        </Field>
      </div>
    </Dialog>
  )
}

// ---------- Sandbox ----------

function AbaSandbox({ tenant }: { tenant: Tenant }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [confirmar, setConfirmar] = useState<boolean | null>(null)

  const mut = useMutation({
    mutationFn: (sandbox: boolean) => api.updateTenant(tenant.id, { sandbox }),
    onSuccess: (_, sandbox) => {
      qc.invalidateQueries({ queryKey: ['tenant', tenant.id] })
      qc.invalidateQueries({ queryKey: ['tenants'] })
      setConfirmar(null)
      toast({
        tipo: 'sucesso',
        titulo: sandbox ? 'Sandbox ativado' : 'Sandbox desativado',
        detalhe: sandbox
          ? 'Emissores passam a usar o mock — nada é transmitido à SEFAZ.'
          : 'Emissores reais passam a transmitir à SEFAZ.',
      })
    },
    onError: (e) => {
      setConfirmar(null)
      toast({ tipo: 'erro', titulo: 'Falha ao alterar sandbox', detalhe: e.message })
    },
  })

  return (
    <>
      <div className="max-w-2xl rounded-lg border border-line bg-surface p-4">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-[13px] font-semibold text-ink">Modo sandbox</h2>
            <p className="mt-1 text-xs leading-relaxed text-ink-muted">
              {tenant.sandbox
                ? 'Ativo: toda emissão, evento e consulta desta empresa usa o emissor mock — nenhum tráfego chega à SEFAZ e o certificado A1 é dispensado.'
                : 'Inativo: a empresa emite de verdade — o XML é assinado com o certificado A1 e transmitido à SEFAZ do ambiente configurado.'}
            </p>
          </div>
          <Switch checked={tenant.sandbox} onChange={(v) => setConfirmar(v)} label="Modo sandbox" />
        </div>

        <div
          className={cn(
            'mt-4 rounded border px-3 py-2.5 text-xs leading-relaxed',
            tenant.sandbox
              ? 'border-contingencia/30 bg-contingencia-soft/50 text-ink'
              : 'border-rejeitado/30 bg-rejeitado-soft/50 text-ink',
          )}
        >
          {tenant.sandbox
            ? 'Ao desativar, confirme que a empresa tem certificado A1 válido e dados fiscais completos — tentativas sem certificado falham com ERRO_INTERNO.'
            : 'Esta empresa está emitindo documentos reais. Alterações aqui afetam imediatamente os novos processamentos em fila.'}
        </div>
      </div>

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

// ---------- Webhooks ----------

function AbaWebhooks({ tenant }: { tenant: Tenant }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [url, setUrl] = useState<string | null>(null)
  const [secret, setSecret] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [statusFiltro, setStatusFiltro] = useState('')
  const [pagina, setPagina] = useState(1)

  const mut = useMutation({
    mutationFn: () =>
      api.updateTenant(tenant.id, { webhookUrl: url ?? undefined, webhookSecret: secret || undefined }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['tenant', tenant.id] })
      setUrl(null)
      setSecret('')
      setErro(null)
      toast({ tipo: 'sucesso', titulo: 'Webhook atualizado' })
    },
    onError: (e) => setErro(e instanceof ApiError ? (e.detail ?? e.message) : e.message),
  })

  const { data: entregas, isLoading } = useQuery({
    queryKey: ['webhook-entregas', tenant.id, statusFiltro, pagina],
    queryFn: () => api.listWebhookEntregas(tenant.id, { status: statusFiltro || undefined, page: pagina, pageSize: 25 }),
    refetchInterval: 15_000,
  })

  const mutReenviar = useMutation({
    mutationFn: (entregaId: string) => api.reenviarWebhookEntrega(tenant.id, entregaId),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'Reenvio solicitado' })
      qc.invalidateQueries({ queryKey: ['webhook-entregas', tenant.id] })
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha no reenvio', detalhe: e.message }),
  })

  const urlAtual = url !== null ? url : (tenant.webhookUrl ?? '')

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-line bg-surface p-4">
        <h2 className="text-[13px] font-semibold text-ink">Configuração</h2>
        <p className="mt-1 text-xs leading-relaxed text-ink-muted">
          Entregas assinadas com HMAC-SHA256 (header <code className="font-mono">X-Fiscal-Signature</code>) e
          retry próprio (até 8 tentativas). Fora do sandbox, a URL exige https.
        </p>
        <div className="mt-3 grid gap-3 lg:grid-cols-2">
          <Field label="Webhook URL" htmlFor="wh-url">
            <Input
              id="wh-url"
              value={urlAtual}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://erp.exemplo.com/hooks/fiscal"
            />
          </Field>
          <Field label="Webhook secret" htmlFor="wh-secret" hint="Mínimo 16 caracteres. Vazio mantém o atual.">
            <Input id="wh-secret" type="password" value={secret} onChange={(e) => setSecret(e.target.value)} placeholder="••••••••" />
          </Field>
        </div>
        {erro && <p className="mt-2 text-xs text-rejeitado">{erro}</p>}
        <div className="mt-3 flex justify-end">
          <Button variant="primary" loading={mut.isPending} disabled={url === null && !secret} onClick={() => mut.mutate()}>
            Salvar webhook
          </Button>
        </div>
      </div>

      <div className="rounded-lg border border-line bg-surface">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-2.5">
          <p className="text-xs text-ink-muted">Entregas (outbox) — atualiza a cada 15s</p>
          <Select
            value={statusFiltro}
            onChange={(e) => {
              setStatusFiltro(e.target.value)
              setPagina(1)
            }}
            aria-label="Filtrar entregas por status"
            className="h-7 w-40 text-xs"
          >
            <option value="">Todos os status</option>
            <option value="PENDENTE">Pendente</option>
            <option value="ENTREGANDO">Entregando</option>
            <option value="ENTREGUE">Entregue</option>
            <option value="FALHA">Falha</option>
          </Select>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : !entregas || entregas.itens.length === 0 ? (
          <EmptyState titulo="Nenhuma entrega registrada" />
        ) : (
          <>
            <Table label="Entregas de webhook">
              <thead>
                <tr>
                  <Th>Evento</Th>
                  <Th className="w-32">Status</Th>
                  <Th className="w-24 text-center">Tentativas</Th>
                  <Th className="w-24">HTTP</Th>
                  <Th>Erro</Th>
                  <Th className="w-36">Criada em</Th>
                  <Th className="w-24" />
                </tr>
              </thead>
              <tbody>
                {entregas.itens.map((w) => (
                  <tr key={w.id}>
                    <Td className="font-mono text-[11px]">{w.tipoEvento}</Td>
                    <Td>
                      <StatusBadge status={w.status} />
                    </Td>
                    <Td className="text-center text-xs tabular">{w.tentativas}</Td>
                    <Td className="text-xs tabular">{w.ultimoStatusCode ?? '—'}</Td>
                    <Td className="max-w-72 truncate text-xs text-ink-muted" title={w.ultimoErro ?? undefined}>
                      {w.ultimoErro ?? '—'}
                    </Td>
                    <Td className="text-xs text-ink-muted tabular">{formatarData(w.criadoEm)}</Td>
                    <Td className="text-right">
                      {(w.status === 'FALHA' || w.status === 'PENDENTE') && (
                        <Button
                          variant="ghost"
                          size="sm"
                          loading={mutReenviar.isPending}
                          onClick={() => mutReenviar.mutate(w.id)}
                          title="Reenviar entrega"
                        >
                          <RefreshCw className="h-3.5 w-3.5" aria-hidden />
                        </Button>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <div className="flex items-center justify-between gap-3 border-t border-line px-3 py-2.5">
              <p className="text-xs text-ink-muted tabular">
                Página {pagina} · {entregas.itens.length} entrega(s)
              </p>
              <div className="flex items-center gap-1">
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={pagina <= 1}
                  onClick={() => setPagina((p) => p - 1)}
                  aria-label="Página anterior"
                >
                  Anterior
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  // O endpoint não devolve `total`: só habilita "próxima"
                  // quando a página veio cheia (pode haver mais).
                  disabled={entregas.itens.length < entregas.pageSize}
                  onClick={() => setPagina((p) => p + 1)}
                  aria-label="Próxima página"
                >
                  Próxima
                </Button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
