import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api, type ApiKeyCriada } from '../lib/api'
import {
  Badge, Button, Card, Field, Input, Modal, PageHeader, PageLoading, Select, Table, Td, Th,
} from '../components/ui'
import { useToast } from '../components/toast'
import { copiar, formatarCnpj, formatarData, formatarDataCurta } from '../lib/utils'
import { TenantFormModal } from './Tenants'

type Aba = 'perfil' | 'keys' | 'certs'

export default function TenantDetail() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const [aba, setAba] = useState<Aba>('keys')
  const [modalEditar, setModalEditar] = useState(false)

  const { data: tenant, isLoading } = useQuery({ queryKey: ['tenant', id], queryFn: () => api.getTenant(id) })

  if (isLoading) return <PageLoading />
  if (!tenant) return <p className="py-16 text-center text-sm text-zinc-500">Tenant não encontrado.</p>

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['tenant', id] })
    qc.invalidateQueries({ queryKey: ['tenants'] })
  }

  return (
    <>
      <PageHeader
        titulo={tenant.razaoSocial}
        descricao={`CNPJ ${formatarCnpj(tenant.cnpj)} · ${tenant.uf} · criado em ${formatarData(tenant.criadoEm)}`}
        acoes={
          <>
            <Badge
              className={
                tenant.ativo
                  ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
                  : 'border-zinc-600 bg-zinc-800 text-zinc-400'
              }
            >
              {tenant.ativo ? 'Ativo' : 'Inativo'}
            </Badge>
            <Button onClick={() => setModalEditar(true)}>Editar</Button>
            <Link to="/tenants">
              <Button variant="ghost">← Voltar</Button>
            </Link>
          </>
        }
      />

      <div className="mb-4 flex gap-1 border-b border-zinc-800">
        {([['keys', 'API Keys'], ['certs', 'Certificados'], ['perfil', 'Perfil fiscal']] as [Aba, string][]).map(
          ([chave, label]) => (
            <button
              key={chave}
              onClick={() => setAba(chave)}
              className={`-mb-px border-b-2 px-4 py-2 text-sm transition-colors ${
                aba === chave
                  ? 'border-emerald-500 font-medium text-emerald-300'
                  : 'border-transparent text-zinc-500 hover:text-zinc-300'
              }`}
            >
              {label}
            </button>
          ),
        )}
      </div>

      {aba === 'keys' && <ApiKeysTab tenantId={id} onChanged={invalidate} />}
      {aba === 'certs' && <CertificadosTab tenantId={id} onChanged={invalidate} />}
      {aba === 'perfil' && <PerfilTab tenantId={id} />}

      {modalEditar && <TenantFormModal open onClose={() => setModalEditar(false)} inicial={tenant} />}
    </>
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
        <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-3">
          <p className="text-xs text-zinc-500">
            A chave completa é exibida <strong className="text-zinc-300">uma única vez</strong> na criação — depois só o
            prefixo.
          </p>
          <Button variant="primary" onClick={() => setModalGerar(true)}>
            + Gerar API key
          </Button>
        </div>
        {isLoading ? (
          <PageLoading />
        ) : !keys || keys.length === 0 ? (
          <p className="py-10 text-center text-xs text-zinc-500">Nenhuma API key para este tenant.</p>
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
                  <Td className="font-mono text-xs text-zinc-100">{k.prefixo}…</Td>
                  <Td>{k.descricao || <span className="text-zinc-600">—</span>}</Td>
                  <Td>
                    <Badge
                      className={
                        k.ambiente === 'producao'
                          ? 'border-red-500/30 bg-red-500/10 text-red-300'
                          : 'border-sky-500/30 bg-sky-500/10 text-sky-300'
                      }
                    >
                      {k.ambiente}
                    </Badge>
                  </Td>
                  <Td>
                    <Badge
                      className={
                        k.ativa
                          ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
                          : 'border-zinc-600 bg-zinc-800 text-zinc-500'
                      }
                    >
                      {k.ativa ? 'Ativa' : 'Revogada'}
                    </Badge>
                  </Td>
                  <Td className="text-xs text-zinc-500">{formatarData(k.criadoEm)}</Td>
                  <Td className="text-right">
                    {k.ativa && (
                      <Button variant="ghost" className="text-red-400 hover:text-red-300" onClick={() => setRevogar(k.id)}>
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
        <p className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-200">
          ⚠ {criada?.aviso ?? 'Copie agora — não será exibida novamente.'}
        </p>
        <code className="mt-3 block rounded-lg border border-zinc-800 bg-zinc-950 px-3 py-2.5 font-mono text-xs break-all text-emerald-300 select-all">
          {criada?.chave}
        </code>
      </Modal>

      <Modal
        open={!!revogar}
        onClose={() => setRevogar(null)}
        titulo="Revogar API key"
        rodape={
          <>
            <Button onClick={() => setRevogar(null)}>Cancelar</Button>
            <Button
              variant="danger"
              loading={revogarMut.isPending}
              onClick={() => revogar && revogarMut.mutate(revogar)}
            >
              Revogar definitivamente
            </Button>
          </>
        }
      >
        <p className="text-sm text-zinc-300">
          A revogação é imediata: integrações usando esta chave passam a receber <strong>401</strong>.
        </p>
      </Modal>
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
        <div className="flex items-center justify-between border-b border-zinc-800 px-4 py-3">
          <p className="text-xs text-zinc-500">Certificado A1 (.pfx) — armazenado cifrado com AES-GCM.</p>
          <Button variant="primary" onClick={() => setModalUpload(true)}>
            + Enviar .pfx
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
                      <span className={vencendo ? 'text-amber-300' : 'text-zinc-500'}>
                        ({diasRestantes}d)
                      </span>
                    </Td>
                    <Td>
                      <Badge
                        className={
                          !c.ativo
                            ? 'border-zinc-600 bg-zinc-800 text-zinc-500'
                            : diasRestantes <= 0
                              ? 'border-red-500/30 bg-red-500/10 text-red-300'
                              : vencendo
                                ? 'border-amber-500/30 bg-amber-500/10 text-amber-300'
                                : 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300'
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
          {erro && <p className="mr-auto text-xs text-red-400">{erro}</p>}
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
            className="w-full cursor-pointer rounded-lg border border-zinc-700 bg-zinc-900 px-3 py-2 text-sm file:mr-3 file:rounded-md file:border-0 file:bg-zinc-800 file:px-3 file:py-1.5 file:text-xs file:text-zinc-300"
          />
        </Field>
        <Field label="Senha do certificado">
          <Input type="password" value={senha} onChange={(e) => setSenha(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}

// ------------------- Perfil -------------------

function PerfilTab({ tenantId }: { tenantId: string }) {
  const { data: tenant } = useQuery({ queryKey: ['tenant', tenantId], queryFn: () => api.getTenant(tenantId) })
  if (!tenant) return null

  const linhas: [string, string][] = [
    ['CNPJ', formatarCnpj(tenant.cnpj)],
    ['Inscrição estadual', tenant.inscricaoEstadual ?? '—'],
    ['UF / Município', `${tenant.uf} · ${tenant.nomeMunicipio ?? '—'} (${tenant.codigoMunicipioIbge ?? 'sem IBGE'})`],
    ['Endereço', [tenant.logradouro, tenant.numero, tenant.complemento, tenant.bairro, tenant.cep].filter(Boolean).join(', ') || '—'],
    ['Regime tributário', String(tenant.regimeTributario)],
    ['Ambiente padrão', tenant.ambientePadrao],
    ['Webhook URL', tenant.webhookUrl ?? '—'],
  ]

  return (
    <Card className="divide-y divide-zinc-800/60">
      {linhas.map(([k, v]) => (
        <div key={k} className="flex items-center justify-between gap-4 px-4 py-3">
          <span className="text-xs font-medium text-zinc-500">{k}</span>
          <span className="text-sm text-zinc-200">{v}</span>
        </div>
      ))}
    </Card>
  )
}
