import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, type Tenant, type TenantInput } from '../lib/api'
import {
  Badge, Button, Card, EmptyState, Field, Input, Modal, PageHeader, PageLoading, Select, Table, Td, Th,
} from '../components/ui'
import { useToast } from '../components/toast'
import { apenasDigitos, formatarCnpj, formatarData } from '../lib/utils'

const VAZIO: TenantInput = {
  cnpj: '',
  razaoSocial: '',
  uf: '',
  codigoMunicipioIbge: '',
  regimeTributario: 3,
  ambientePadrao: 'homologacao',
  inscricaoEstadual: '',
  logradouro: '',
  numero: '',
  complemento: '',
  bairro: '',
  cep: '',
  nomeMunicipio: '',
  webhookUrl: '',
  ativo: true,
}

function paraForm(t: Tenant): TenantInput {
  return {
    cnpj: t.cnpj,
    razaoSocial: t.razaoSocial,
    uf: t.uf,
    codigoMunicipioIbge: t.codigoMunicipioIbge ?? '',
    regimeTributario: t.regimeTributario,
    ambientePadrao: t.ambientePadrao,
    inscricaoEstadual: t.inscricaoEstadual ?? '',
    logradouro: t.logradouro ?? '',
    numero: t.numero ?? '',
    complemento: t.complemento ?? '',
    bairro: t.bairro ?? '',
    cep: t.cep ?? '',
    nomeMunicipio: t.nomeMunicipio ?? '',
    webhookUrl: t.webhookUrl ?? '',
    ativo: t.ativo,
  }
}

export function TenantFormModal({
  open,
  onClose,
  inicial,
  criado,
}: {
  open: boolean
  onClose: () => void
  inicial?: Tenant
  criado?: (id: string) => void
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const [form, setForm] = useState<TenantInput>(inicial ? paraForm(inicial) : { ...VAZIO })
  const [erro, setErro] = useState<string | null>(null)

  const mut = useMutation({
    mutationFn: async () => {
      const body: TenantInput = { ...form, cnpj: apenasDigitos(form.cnpj), cep: apenasDigitos(form.cep ?? '') }
      if (inicial) {
        await api.updateTenant(inicial.id, body)
        return inicial.id
      }
      return api.createTenant(body)
    },
    onSuccess: (id) => {
      qc.invalidateQueries({ queryKey: ['tenants'] })
      qc.invalidateQueries({ queryKey: ['tenant', inicial?.id] })
      toast({ tipo: 'sucesso', titulo: inicial ? 'Empresa atualizada' : 'Empresa criada' })
      if (!inicial && criado && id) criado(id)
      onClose()
    },
    onError: (e) => setErro(e instanceof Error ? e.message : 'Erro inesperado'),
  })

  const set = (campo: keyof TenantInput, valor: string | number | boolean) =>
    setForm((f) => ({ ...f, [campo]: valor }))

  function submit(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    if (apenasDigitos(form.cnpj).length !== 14) {
      setErro('CNPJ deve ter 14 dígitos.')
      return
    }
    if (!form.razaoSocial.trim()) {
      setErro('Razão social é obrigatória.')
      return
    }
    if (form.uf.trim().length !== 2) {
      setErro('UF deve ter 2 letras.')
      return
    }
    mut.mutate()
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      titulo={inicial ? `Editar — ${inicial.razaoSocial}` : 'Nova empresa'}
      largura="max-w-2xl"
      rodape={
        <>
          {erro && <p className="mr-auto text-xs text-red-500 dark:text-red-400">{erro}</p>}
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            {inicial ? 'Salvar' : 'Criar empresa'}
          </Button>
        </>
      }
    >
      <form onSubmit={submit} className="grid grid-cols-2 gap-4">
        <Field label="CNPJ *" className="col-span-1">
          <Input value={form.cnpj} onChange={(e) => set('cnpj', e.target.value)} placeholder="00.000.000/0000-00" />
        </Field>
        <Field label="Inscrição Estadual">
          <Input value={form.inscricaoEstadual ?? ''} onChange={(e) => set('inscricaoEstadual', e.target.value)} />
        </Field>
        <Field label="Razão social *" className="col-span-2">
          <Input value={form.razaoSocial} onChange={(e) => set('razaoSocial', e.target.value)} />
        </Field>
        <Field label="UF *">
          <Input value={form.uf} onChange={(e) => set('uf', e.target.value.toUpperCase())} maxLength={2} placeholder="PR" />
        </Field>
        <Field label="Código município IBGE" hint="7 dígitos (ex.: 4106902 = Curitiba)">
          <Input value={form.codigoMunicipioIbge ?? ''} onChange={(e) => set('codigoMunicipioIbge', apenasDigitos(e.target.value))} maxLength={7} />
        </Field>
        <Field label="Nome do município">
          <Input value={form.nomeMunicipio ?? ''} onChange={(e) => set('nomeMunicipio', e.target.value)} />
        </Field>
        <Field label="Regime tributário">
          <Select value={form.regimeTributario} onChange={(e) => set('regimeTributario', Number(e.target.value))}>
            <option value={1}>1 — Simples Nacional</option>
            <option value={2}>2 — Simples (excesso sublimite)</option>
            <option value={3}>3 — Regime Normal</option>
          </Select>
        </Field>
        <Field label="Logradouro">
          <Input value={form.logradouro ?? ''} onChange={(e) => set('logradouro', e.target.value)} />
        </Field>
        <Field label="Número">
          <Input value={form.numero ?? ''} onChange={(e) => set('numero', e.target.value)} />
        </Field>
        <Field label="Complemento">
          <Input value={form.complemento ?? ''} onChange={(e) => set('complemento', e.target.value)} />
        </Field>
        <Field label="Bairro">
          <Input value={form.bairro ?? ''} onChange={(e) => set('bairro', e.target.value)} />
        </Field>
        <Field label="CEP">
          <Input value={form.cep ?? ''} onChange={(e) => set('cep', e.target.value)} maxLength={9} placeholder="00000-000" />
        </Field>
        <Field label="Ambiente padrão">
          <Select value={form.ambientePadrao} onChange={(e) => set('ambientePadrao', e.target.value)}>
            <option value="homologacao">Homologação</option>
            <option value="producao">Produção</option>
          </Select>
        </Field>
        <Field label="Webhook URL" hint="Endpoint que receberá os eventos do tenant">
          <Input value={form.webhookUrl ?? ''} onChange={(e) => set('webhookUrl', e.target.value)} placeholder="https://…" />
        </Field>
        {inicial && (
          <Field label="Situação">
            <Select value={String(form.ativo)} onChange={(e) => set('ativo', e.target.value === 'true')}>
              <option value="true">Ativo</option>
              <option value="false">Inativo</option>
            </Select>
          </Field>
        )}
      </form>
    </Modal>
  )
}

export default function Tenants() {
  const { data, isLoading } = useQuery({ queryKey: ['tenants'], queryFn: api.listTenants })
  const [modalNovo, setModalNovo] = useState(false)

  return (
    <>
      <PageHeader
        titulo="Empresas"
        descricao="Empresas emitentes cadastradas na plataforma"
        acoes={
          <Button variant="primary" onClick={() => setModalNovo(true)}>
            Nova empresa
          </Button>
        }
      />
      <Card>
        {isLoading ? (
          <PageLoading />
        ) : !data || data.length === 0 ? (
          <EmptyState
            titulo="Nenhuma empresa cadastrada"
            descricao="Crie a primeira empresa para poder gerar API keys e emitir documentos. O ERP também provisiona automaticamente via chave master."
            acao={
              <Button variant="primary" onClick={() => setModalNovo(true)}>
                Nova empresa
              </Button>
            }
          />
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Razão social</Th>
                <Th>CNPJ</Th>
                <Th>UF</Th>
                <Th>Ambiente padrão</Th>
                <Th>Modo</Th>
                <Th className="text-center">API keys</Th>
                <Th className="text-center">Certificados</Th>
                <Th>Situação</Th>
                <Th>Criado em</Th>
              </tr>
            </thead>
            <tbody>
              {data.map((t) => (
                <tr key={t.id} className="transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/30">
                  <Td>
                    <Link to={`/empresas/${t.id}`} className="font-medium text-zinc-900 hover:text-blue-600 dark:text-zinc-100 dark:hover:text-blue-300">
                      {t.razaoSocial}
                    </Link>
                  </Td>
                  <Td className="font-mono text-xs">{formatarCnpj(t.cnpj)}</Td>
                  <Td>{t.uf}</Td>
                  <Td>
                    <Badge
                      className={
                        t.ambientePadrao === 'producao'
                          ? 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300'
                          : 'border-sky-200 bg-sky-50 text-sky-700 dark:border-sky-500/30 dark:bg-sky-500/10 dark:text-sky-300'
                      }
                    >
                      {t.ambientePadrao}
                    </Badge>
                  </Td>
                  <Td>
                    <Badge
                      className={
                        t.sandbox
                          ? 'border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300'
                          : 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                      }
                    >
                      {t.sandbox ? 'Sandbox' : 'Produção real'}
                    </Badge>
                  </Td>
                  <Td className="text-center tabular-nums">{t.apiKeysAtivas}</Td>
                  <Td className="text-center tabular-nums">{t.certificadosAtivos}</Td>
                  <Td>
                    <Badge
                      className={
                        t.ativo
                          ? 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                          : 'border-zinc-200 bg-zinc-100 text-zinc-500 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-400'
                      }
                    >
                      {t.ativo ? 'Ativa' : 'Inativa'}
                    </Badge>
                  </Td>
                  <Td className="text-xs text-zinc-500">{formatarData(t.criadoEm)}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>
      {modalNovo && (
        <TenantFormModal
          open
          onClose={() => setModalNovo(false)}
          criado={(id) => window.location.assign(`/empresas/${id}`)}
        />
      )}
    </>
  )
}
