import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { Plus, PowerOff } from 'lucide-react'
import { api, ApiError, type TenantInput } from '../lib/api'
import { apenasDigitos, formatarCnpj, formatarData } from '../lib/format'
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
  Table,
  Td,
  Th,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { SegredoUnico } from '../components/ui/segredo'

// ==========================================================================
// Empresas (tenants).
//
// A criação usa `criarApiKey`: empresa + primeira API key na MESMA
// transação, e a chave aparece uma vez no mesmo modal. O fluxo antigo era
// criar → navegar → abrir aba → gerar chave — quatro passos para o que a
// API já fazia em um.
// ==========================================================================

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
  ativo: true,
}

const REGIMES: Record<number, string> = {
  1: 'Simples Nacional',
  2: 'Simples (excesso sublimite)',
  3: 'Regime Normal',
  4: 'MEI',
}

export default function Empresas() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const toast = useToast()
  const [modalNova, setModalNova] = useState(false)
  const [desativar, setDesativar] = useState<{ id: string; nome: string } | null>(null)

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['tenants'],
    queryFn: api.listTenants,
  })

  const mutDesativar = useMutation({
    mutationFn: (id: string) => api.disableTenant(id),
    onSuccess: () => {
      toast({ tipo: 'sucesso', titulo: 'Empresa desativada', detalhe: 'Dados e chaves preservados; a empresa deixa de emitir.' })
      setDesativar(null)
      qc.invalidateQueries({ queryKey: ['tenants'] })
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha ao desativar', detalhe: e.message }),
  })

  return (
    <>
      <PageHeader
        titulo="Empresas"
        descricao="Emitentes cadastradas na plataforma."
        acoes={
          <Button variant="primary" onClick={() => setModalNova(true)}>
            <Plus className="h-3.5 w-3.5" aria-hidden />
            Nova empresa
          </Button>
        }
      />

      <div className="rounded-lg border border-line bg-surface">
        {isLoading ? (
          <PageLoading mensagem="Carregando empresas…" />
        ) : isError ? (
          <div className="p-4">
            <ErrorState
              titulo="Não foi possível carregar as empresas"
              detalhe={error instanceof ApiError ? (error.detail ?? error.message) : 'Verifique se a API está rodando.'}
              aoTentarNovamente={() => refetch()}
            />
          </div>
        ) : !data || data.length === 0 ? (
          <EmptyState
            titulo="Nenhuma empresa cadastrada"
            descricao="Crie a primeira empresa para gerar API keys e emitir documentos. O ERP também provisiona automaticamente via chave master."
            acao={
              <Button variant="primary" onClick={() => setModalNova(true)}>
                <Plus className="h-3.5 w-3.5" aria-hidden />
                Nova empresa
              </Button>
            }
          />
        ) : (
          <Table label="Empresas emitentes">
            <thead>
              <tr>
                <Th>Razão social</Th>
                <Th className="w-44">CNPJ</Th>
                <Th className="w-14">UF</Th>
                <Th className="w-32">Ambiente</Th>
                <Th className="w-28">Modo</Th>
                <Th className="w-20 text-center">Keys</Th>
                <Th className="w-24 text-center">Certificados</Th>
                <Th className="w-24">Situação</Th>
                <Th className="w-36">Criada em</Th>
                <Th className="w-12" />
              </tr>
            </thead>
            <tbody>
              {data.map((t) => (
                <tr key={t.id} className="transition-colors hover:bg-surface-sunken/60">
                  <Td>
                    <Link
                      to={`/empresas/${t.id}`}
                      className="text-[13px] font-medium text-ink hover:text-carimbo hover:underline"
                    >
                      {t.razaoSocial}
                    </Link>
                    <p className="text-[10px] text-ink-faint">{REGIMES[t.regimeTributario] ?? `regime ${t.regimeTributario}`}</p>
                  </Td>
                  <Td className="font-mono text-xs tabular">{formatarCnpj(t.cnpj)}</Td>
                  <Td className="text-xs">{t.uf}</Td>
                  <Td>
                    <AmbienteBadge ambiente={t.ambientePadrao} />
                  </Td>
                  <Td>
                    <Badge
                      className={
                        t.sandbox
                          ? 'border-contingencia/30 bg-contingencia-soft text-contingencia'
                          : 'border-aprovado/30 bg-aprovado-soft text-aprovado'
                      }
                    >
                      {t.sandbox ? 'Sandbox' : 'Real'}
                    </Badge>
                  </Td>
                  <Td className="text-center text-xs tabular">{t.apiKeysAtivas}</Td>
                  <Td className="text-center text-xs tabular">
                    {t.certificadosAtivos === 0 && !t.sandbox ? (
                      <span className="text-rejeitado" title="Sem certificado ativo — emissão real falha">
                        0
                      </span>
                    ) : (
                      t.certificadosAtivos
                    )}
                  </Td>
                  <Td>
                    <Badge
                      className={
                        t.ativo
                          ? 'border-aprovado/30 bg-aprovado-soft text-aprovado'
                          : 'border-line-strong bg-surface-sunken text-ink-faint'
                      }
                    >
                      {t.ativo ? 'Ativa' : 'Inativa'}
                    </Badge>
                  </Td>
                  <Td className="text-xs text-ink-muted tabular">{formatarData(t.criadoEm)}</Td>
                  <Td className="text-right">
                    {t.ativo && (
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={`Desativar ${t.razaoSocial}`}
                        title="Desativar empresa"
                        onClick={() => setDesativar({ id: t.id, nome: t.razaoSocial })}
                      >
                        <PowerOff className="h-3.5 w-3.5" aria-hidden />
                      </Button>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </div>

      {modalNova && (
        <NovaEmpresaModal
          onClose={() => setModalNova(false)}
          aoCriar={(id) => {
            qc.invalidateQueries({ queryKey: ['tenants'] })
            navigate(`/empresas/${id}`)
          }}
        />
      )}

      <ConfirmDialog
        open={!!desativar}
        onClose={() => setDesativar(null)}
        onConfirm={() => desativar && mutDesativar.mutate(desativar.id)}
        titulo="Desativar empresa"
        mensagem={`${desativar?.nome ?? ''} deixa de emitir. Os dados e as API keys são preservados — a empresa pode ser reativada depois.`}
        confirmarLabel="Desativar"
        perigoso
        loading={mutDesativar.isPending}
      />
    </>
  )
}

// ---------- Criação em uma chamada ----------

function NovaEmpresaModal({
  onClose,
  aoCriar,
}: {
  onClose: () => void
  aoCriar: (id: string) => void
}) {
  const toast = useToast()
  const [form, setForm] = useState<TenantInput>({ ...VAZIO })
  const [criarKey, setCriarKey] = useState(true)
  const [erro, setErro] = useState<string | null>(null)
  const [chaveCriada, setChaveCriada] = useState<{ id: string; chave: string; aviso: string } | null>(null)

  const mut = useMutation({
    mutationFn: async () => {
      const body: TenantInput = {
        ...form,
        cnpj: apenasDigitos(form.cnpj),
        cep: apenasDigitos(form.cep ?? ''),
        criarApiKey: criarKey ? (form.ambientePadrao as 'producao' | 'homologacao') : undefined,
      }
      return api.createTenant(body)
    },
    onSuccess: (resp) => {
      if (typeof resp === 'string') {
        toast({ tipo: 'sucesso', titulo: 'Empresa criada' })
        aoCriar(resp)
        return
      }
      // Empresa + chave na mesma transação: mostra a chave antes de navegar.
      setChaveCriada({ id: resp.tenant.id, chave: resp.apiKey.chave, aviso: resp.apiKey.aviso })
    },
    onError: (e) => setErro(e instanceof ApiError ? (e.detail ?? e.message) : 'Erro inesperado'),
  })

  const set = (campo: keyof TenantInput, valor: string | number | boolean) =>
    setForm((f) => ({ ...f, [campo]: valor }))

  function submeter(e: React.FormEvent) {
    e.preventDefault()
    setErro(null)
    if (apenasDigitos(form.cnpj).length !== 14) return setErro('CNPJ deve ter 14 dígitos.')
    if (!form.razaoSocial.trim()) return setErro('Razão social é obrigatória.')
    if (form.uf.trim().length !== 2) return setErro('UF deve ter 2 letras.')
    mut.mutate()
  }

  // Depois de criada, o modal vira a exibição única da chave.
  if (chaveCriada) {
    return (
      <Dialog
        open
        onClose={() => aoCriar(chaveCriada.id)}
        titulo="Empresa criada — API key gerada"
        descricao="A chave abaixo autentica o integrador. Ela não será exibida novamente."
        rodape={
          <Button variant="primary" onClick={() => aoCriar(chaveCriada.id)}>
            Ir para a empresa
          </Button>
        }
      >
        <SegredoUnico valor={chaveCriada.chave} aviso={chaveCriada.aviso} />
      </Dialog>
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      titulo="Nova empresa"
      descricao="Dados fiscais do emitente. A primeira API key pode ser criada junto."
      largura="lg"
      rodape={
        <>
          {erro && <p className="mr-auto text-xs text-rejeitado">{erro}</p>}
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" loading={mut.isPending} onClick={() => mut.mutate()}>
            Criar empresa
          </Button>
        </>
      }
    >
      <form onSubmit={submeter} className="grid grid-cols-2 gap-3">
        <Field label="CNPJ" htmlFor="cnpj" required>
          <Input
            id="cnpj"
            value={form.cnpj}
            onChange={(e) => set('cnpj', e.target.value)}
            placeholder="00.000.000/0000-00"
            className="font-mono"
          />
        </Field>
        <Field label="Inscrição estadual" htmlFor="ie">
          <Input id="ie" value={form.inscricaoEstadual ?? ''} onChange={(e) => set('inscricaoEstadual', e.target.value)} />
        </Field>
        <Field label="Razão social" htmlFor="razao" required className="col-span-2">
          <Input id="razao" value={form.razaoSocial} onChange={(e) => set('razaoSocial', e.target.value)} />
        </Field>
        <Field label="UF" htmlFor="uf" required>
          <Input id="uf" value={form.uf} onChange={(e) => set('uf', e.target.value.toUpperCase())} maxLength={2} placeholder="PR" />
        </Field>
        <Field label="Código IBGE do município" htmlFor="ibge" hint="7 dígitos (4106902 = Curitiba)">
          <Input
            id="ibge"
            value={form.codigoMunicipioIbge ?? ''}
            onChange={(e) => set('codigoMunicipioIbge', apenasDigitos(e.target.value))}
            maxLength={7}
            className="font-mono"
          />
        </Field>
        <Field label="Município" htmlFor="municipio">
          <Input id="municipio" value={form.nomeMunicipio ?? ''} onChange={(e) => set('nomeMunicipio', e.target.value)} />
        </Field>
        <Field label="Regime tributário" htmlFor="regime">
          <Select id="regime" value={form.regimeTributario} onChange={(e) => set('regimeTributario', Number(e.target.value))}>
            {Object.entries(REGIMES).map(([v, nome]) => (
              <option key={v} value={v}>
                {v} — {nome}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Logradouro" htmlFor="logradouro">
          <Input id="logradouro" value={form.logradouro ?? ''} onChange={(e) => set('logradouro', e.target.value)} />
        </Field>
        <Field label="Número" htmlFor="numero">
          <Input id="numero" value={form.numero ?? ''} onChange={(e) => set('numero', e.target.value)} />
        </Field>
        <Field label="Bairro" htmlFor="bairro">
          <Input id="bairro" value={form.bairro ?? ''} onChange={(e) => set('bairro', e.target.value)} />
        </Field>
        <Field label="CEP" htmlFor="cep">
          <Input id="cep" value={form.cep ?? ''} onChange={(e) => set('cep', e.target.value)} maxLength={9} placeholder="00000-000" className="font-mono" />
        </Field>
        <Field label="Ambiente padrão" htmlFor="ambiente" hint="Chaves de homologação não emitem em produção.">
          <Select id="ambiente" value={form.ambientePadrao} onChange={(e) => set('ambientePadrao', e.target.value)}>
            <option value="homologacao">Homologação</option>
            <option value="producao">Produção</option>
          </Select>
        </Field>

        <label className="col-span-2 flex items-start gap-2 rounded border border-line bg-surface-sunken px-3 py-2.5">
          <input
            type="checkbox"
            checked={criarKey}
            onChange={(e) => setCriarKey(e.target.checked)}
            className="mt-0.5 h-3.5 w-3.5 accent-[var(--carimbo)]"
          />
          <span className="text-xs leading-relaxed text-ink">
            Criar a primeira API key junto
            <span className="block text-[11px] text-ink-muted">
              Mesma transação. A chave aparece uma única vez, logo após a criação.
            </span>
          </span>
        </label>
      </form>
    </Dialog>
  )
}
