import { useMemo, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, type EmissaoResponse } from '../lib/api'
import {
  Badge, Button, Card, Field, Input, PageHeader, Select, StatusBadge, Textarea,
} from '../components/ui'
import { useToast } from '../components/toast'
import { formatarData, formatarDecimal, STATUS_COLOR, STATUS_TERMINAIS } from '../lib/utils'

interface ItemForm {
  codigo: string
  descricao: string
  ncm: string
  cfop: string
  quantidade: number
  valorUnitario: number
  aliquota: number
}

const ITEM_PADRAO: ItemForm = {
  codigo: 'SKU1',
  descricao: 'Produto de teste',
  ncm: '12345678',
  cfop: '5102',
  quantidade: 1,
  valorUnitario: 100,
  aliquota: 1.65,
}

const KEY_STORAGE = 'fiscal.playground.apikey'

function totalItem(i: ItemForm): number {
  return Math.round(i.quantidade * i.valorUnitario * 100) / 100
}

function montarJson(ambiente: string, serie: number, itens: ItemForm[], destCnpj: string, destNome: string, natureza: string) {
  const itensJson = itens.map((i) => {
    const total = totalItem(i)
    const base = total
    const valorImposto = Math.round(((base * i.aliquota) / 100) * 100) / 100
    return {
      codigo: i.codigo,
      descricao: i.descricao,
      ncm: i.ncm,
      cfop: i.cfop,
      quantidade: i.quantidade,
      valorUnitario: i.valorUnitario,
      valorTotal: total,
      impostos: [{ cst: '01', baseCalculo: base, aliquota: i.aliquota, valor: valorImposto }],
    }
  })
  const valorProdutos = Math.round(itensJson.reduce((acc, i) => acc + i.valorTotal, 0) * 100) / 100

  return {
    ambiente,
    serie,
    naturezaOperacao: natureza,
    destinatario: { cnpjCpf: destCnpj, nome: destNome },
    itens: itensJson,
    totais: { valorProdutos, valorNota: valorProdutos },
    pagamento: [{ forma: '01', valor: valorProdutos }],
  }
}

interface HistoricoItem {
  id: string
  modelo: string
  status: string
  em: string
  json: string
}

export default function Playground() {
  const toast = useToast()
  const { data: tenants } = useQuery({ queryKey: ['tenants'], queryFn: api.listTenants })

  const [tenantId, setTenantId] = useState('')
  const [modelo, setModelo] = useState<'nfe' | 'nfce'>('nfe')
  const [ambiente, setAmbiente] = useState('homologacao')
  const [serie, setSerie] = useState(1)
  const [apiKey, setApiKey] = useState(() => sessionStorage.getItem(KEY_STORAGE) ?? '')
  const [modo, setModo] = useState<'form' | 'json'>('form')

  const [destCnpj, setDestCnpj] = useState('12345678000199')
  const [destNome, setDestNome] = useState('Destinatário Teste LTDA')
  const [natureza, setNatureza] = useState('Venda de teste')
  const [itens, setItens] = useState<ItemForm[]>([{ ...ITEM_PADRAO }])

  const jsonGerado = useMemo(
    () => JSON.stringify(montarJson(ambiente, serie, itens, destCnpj, destNome, natureza), null, 2),
    [ambiente, serie, itens, destCnpj, destNome, natureza],
  )
  const [jsonCru, setJsonCru] = useState('')

  const [emitindo, setEmitindo] = useState(false)
  const [seguindo, setSeguindo] = useState<EmissaoResponse | null>(null)
  // Token do acompanhamento ativo: emissão nova invalida o loop anterior e o
  // loop aborta se o componente desmontou (antes, loops ficavam rodando em
  // paralelo e brigavam pelo mesmo estado).
  const seguirToken = useRef(0)
  const seguirAtivo = useRef(true)
  useEffect(() => {
    seguirAtivo.current = true
    return () => {
      seguirAtivo.current = false
    }
  }, [])
  const [transicoes, setTransicoes] = useState<{ status: string; em: string }[]>([])
  const [historico, setHistorico] = useState<HistoricoItem[]>([])

  const totalNota = itens.reduce((acc, i) => acc + totalItem(i), 0)

  const salvarKey = (valor: string) => {
    setApiKey(valor)
    sessionStorage.setItem(KEY_STORAGE, valor)
  }

  async function emitir() {
    const key = apiKey.trim()
    if (!key) {
      toast({ tipo: 'erro', titulo: 'Informe a API key do tenant', detalhe: 'Gere uma na aba API Keys do tenant.' })
      return
    }
    setEmitindo(true)
    setSeguindo(null)
    setTransicoes([])
    try {
      const body = modo === 'json' ? JSON.parse(jsonCru) : montarJson(ambiente, serie, itens, destCnpj, destNome, natureza)
      const idem = crypto.randomUUID()
      const resp = await api.emitir(modelo, key, body, idem)

      const inicial: EmissaoResponse = {
        id: resp.id,
        tipo: modelo.toUpperCase(),
        status: resp.status,
        ambiente,
        serie: null,
        numero: null,
        chaveAcesso: null,
        protocoloAutorizacao: null,
        xmlAssinado: null,
        xmlRetornoSefaz: null,
        motivoStatus: null,
        criadoEm: new Date().toISOString(),
        atualizadoEm: new Date().toISOString(),
      }
      setSeguindo(inicial)
      setTransicoes([{ status: resp.status, em: new Date().toISOString() }])

      const entrada: HistoricoItem = {
        id: resp.id,
        modelo,
        status: resp.status,
        em: new Date().toISOString(),
        json: JSON.stringify(body, null, 2),
      }
      setHistorico((h) => [entrada, ...h].slice(0, 20))

      if (!STATUS_TERMINAIS.includes(resp.status)) {
        seguirToken.current += 1
        const token = seguirToken.current
        void acompanhar(resp.id, key, [resp.status], token)
      }
    } catch (err) {
      toast({ tipo: 'erro', titulo: 'Emissão rejeitada', detalhe: err instanceof Error ? err.message : String(err) })
    } finally {
      setEmitindo(false)
    }
  }

  async function acompanhar(id: string, key: string, vistos: string[], token: number) {
    for (let i = 0; i < 45; i++) {
      await new Promise((r) => setTimeout(r, 2000))
      if (!seguirAtivo.current || seguirToken.current !== token) return
      try {
        const doc = await api.consultarComoTenant(id, key)
        setSeguindo(doc)
        if (doc.status !== vistos[vistos.length - 1]) {
          vistos = [...vistos, doc.status]
          setTransicoes((t) => [...t, { status: doc.status, em: new Date().toISOString() }])
        }
        if (STATUS_TERMINAIS.includes(doc.status)) {
          setHistorico((h) => h.map((x) => (x.id === id ? { ...x, status: doc.status } : x)))
          return
        }
      } catch {
        // falha de polling: tenta de novo no próximo ciclo
      }
    }
  }

  const setItem = (idx: number, campo: keyof ItemForm, valor: string | number) =>
    setItens((arr) => arr.map((it, i) => (i === idx ? { ...it, [campo]: campo === 'codigo' || campo === 'descricao' || campo === 'ncm' || campo === 'cfop' ? valor : Number(valor) || 0 } : it)))

  return (
    <>
      <PageHeader
        titulo="Playground"
        descricao="Emita documentos de teste pelo caminho real da API (API key do tenant + Idempotency-Key)"
      />

      <div className="grid items-start gap-4 lg:grid-cols-[1fr_380px]">
        <div className="space-y-4">
          {/* Configuração */}
          <Card className="p-5">
            <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Configuração</h2>
            <div className="mt-3 grid grid-cols-2 gap-3 lg:grid-cols-4">
              <Field label="Tenant">
                <Select value={tenantId} onChange={(e) => setTenantId(e.target.value)}>
                  <option value="">— selecione —</option>
                  {(tenants ?? []).map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.razaoSocial}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field label="Modelo">
                <Select value={modelo} onChange={(e) => setModelo(e.target.value as 'nfe' | 'nfce')}>
                  <option value="nfe">NF-e (55)</option>
                  <option value="nfce">NFC-e (65)</option>
                </Select>
              </Field>
              <Field label="Ambiente">
                <Select value={ambiente} onChange={(e) => setAmbiente(e.target.value)}>
                  <option value="homologacao">Homologação</option>
                  <option value="producao">Produção</option>
                </Select>
              </Field>
              <Field label="Série">
                <Input type="number" min={1} max={999} value={serie} onChange={(e) => setSerie(Number(e.target.value) || 1)} />
              </Field>
            </div>
            <Field
              label="API key do tenant"
              className="mt-3"
              hint={
                tenantId
                  ? 'Gere/copie na aba API Keys do tenant. Fica só na sessão deste navegador.'
                  : 'Selecione um tenant acima para atalho de geração.'
              }
            >
              <div className="flex gap-2">
                <Input
                  type="password"
                  value={apiKey}
                  onChange={(e) => salvarKey(e.target.value)}
                  placeholder="fk_test_…"
                  className="font-mono"
                  autoComplete="off"
                />
                {tenantId && (
                  <Link to={`/tenants/${tenantId}`} tabIndex={-1}>
                    <Button type="button">Gerar key</Button>
                  </Link>
                )}
              </div>
            </Field>
          </Card>

          {/* Conteúdo da emissão */}
          <Card className="p-5">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Documento</h2>
              <div className="flex rounded-md border border-zinc-300 p-0.5 text-xs dark:border-zinc-700">
                {(['form', 'json'] as const).map((m) => (
                  <button
                    key={m}
                    onClick={() => {
                      if (m === 'json') setJsonCru(jsonGerado)
                      setModo(m)
                    }}
                    className={`rounded-md px-3 py-1 transition-colors ${
                      modo === m ? 'bg-zinc-100 font-medium text-zinc-900 dark:bg-zinc-700 dark:text-zinc-100' : 'text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-300'
                    }`}
                  >
                    {m === 'form' ? 'Formulário' : 'JSON cru'}
                  </button>
                ))}
              </div>
            </div>

            {modo === 'form' ? (
              <div className="mt-4 space-y-4">
                <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                  <Field label="Destinatário CNPJ/CPF">
                    <Input value={destCnpj} onChange={(e) => setDestCnpj(e.target.value)} className="font-mono" />
                  </Field>
                  <Field label="Nome do destinatário" className="col-span-2">
                    <Input value={destNome} onChange={(e) => setDestNome(e.target.value)} />
                  </Field>
                  <Field label="Natureza da operação">
                    <Input value={natureza} onChange={(e) => setNatureza(e.target.value)} />
                  </Field>
                </div>

                <div>
                  <div className="mb-2 flex items-center justify-between">
                    <span className="text-xs font-medium text-zinc-400">Itens</span>
                    <Button type="button" onClick={() => setItens((arr) => [...arr, { ...ITEM_PADRAO, codigo: `SKU${arr.length + 1}` }])}>
                      + Item
                    </Button>
                  </div>
                  <div className="space-y-2">
                    {itens.map((item, idx) => (
                      <div key={idx} className="grid grid-cols-12 items-end gap-2 rounded-md border border-zinc-200 p-3 dark:border-zinc-800">
                        <Field label="Código" className="col-span-2">
                          <Input value={item.codigo} onChange={(e) => setItem(idx, 'codigo', e.target.value)} />
                        </Field>
                        <Field label="Descrição" className="col-span-4">
                          <Input value={item.descricao} onChange={(e) => setItem(idx, 'descricao', e.target.value)} />
                        </Field>
                        <Field label="NCM" className="col-span-2">
                          <Input value={item.ncm} onChange={(e) => setItem(idx, 'ncm', e.target.value)} />
                        </Field>
                        <Field label="CFOP" className="col-span-1">
                          <Input value={item.cfop} onChange={(e) => setItem(idx, 'cfop', e.target.value)} />
                        </Field>
                        <Field label="Qtd" className="col-span-1">
                          <Input type="number" min={0} step="any" value={item.quantidade} onChange={(e) => setItem(idx, 'quantidade', e.target.value)} />
                        </Field>
                        <Field label="Vlr unit." className="col-span-1">
                          <Input type="number" min={0} step="any" value={item.valorUnitario} onChange={(e) => setItem(idx, 'valorUnitario', e.target.value)} />
                        </Field>
                        <Field label="ICMS %" className="col-span-1">
                          <Input type="number" min={0} step="any" value={item.aliquota} onChange={(e) => setItem(idx, 'aliquota', e.target.value)} />
                        </Field>
                        <div className="col-span-12 flex items-center justify-between text-[11px] text-zinc-500">
                          <span>
                            Total do item: <span className="text-zinc-700 dark:text-zinc-300">{formatarDecimal(totalItem(item))}</span> ·
                            ICMS: <span className="text-zinc-700 dark:text-zinc-300">{formatarDecimal(Math.round(((totalItem(item) * item.aliquota) / 100) * 100) / 100)}</span>
                          </span>
                          {itens.length > 1 && (
                            <button className="text-red-400 hover:text-red-300" onClick={() => setItens((arr) => arr.filter((_, i) => i !== idx))}>
                              remover
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                  <p className="mt-3 text-right text-sm text-zinc-400">
                    Total da nota: <span className="font-semibold text-emerald-600 dark:text-emerald-300">{formatarDecimal(totalNota)}</span>
                  </p>
                </div>
              </div>
            ) : (
              <div className="mt-4">
                <Textarea
                  rows={18}
                  value={jsonCru}
                  onChange={(e) => setJsonCru(e.target.value)}
                  spellCheck={false}
                />
                <button className="mt-2 text-[11px] text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-300" onClick={() => setJsonCru(jsonGerado)}>
                  restaurar JSON gerado pelo formulário
                </button>
              </div>
            )}

            <div className="mt-5 flex items-center justify-between border-t border-zinc-200 pt-4 dark:border-zinc-800">
              <p className="text-[11px] text-zinc-600">
                Idempotency-Key (uuid) gerada automaticamente a cada envio.
              </p>
              <Button variant="primary" loading={emitindo} onClick={emitir}>
                ▶ Emitir {modelo.toUpperCase()}
              </Button>
            </div>
          </Card>
        </div>

        {/* Acompanhamento */}
        <div className="space-y-4">
          <Card className="p-5">
            <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Acompanhamento</h2>
            {!seguindo ? (
              <p className="py-8 text-center text-xs text-zinc-600">
                Emita um documento para acompanhar
                <br />
                o status em tempo real aqui.
              </p>
            ) : (
              <div className="mt-3 space-y-4">
                <div className="flex items-center gap-2">
                  <StatusBadge status={seguindo.status} />
                  <code className="truncate text-[10px] text-zinc-500">{seguindo.id}</code>
                </div>

                <ol className="space-y-0">
                  {transicoes.map((t, i) => (
                    <li key={i} className="relative flex gap-3 pb-3 pl-1">
                      {i < transicoes.length - 1 && (
                        <span className="absolute top-3.5 left-[7px] h-full w-px bg-zinc-300 dark:bg-zinc-700" />
                      )}
                      <span
                        className={`mt-1 h-3.5 w-3.5 shrink-0 rounded-full border-2 ${
                          STATUS_COLOR[t.status] ?? 'border-zinc-600 bg-zinc-700'
                        } bg-white dark:bg-zinc-950`}
                      />
                      <div>
                        <p className="text-xs font-medium text-zinc-800 dark:text-zinc-200">{t.status}</p>
                        <p className="text-[10px] text-zinc-500">{formatarData(t.em)}</p>
                      </div>
                    </li>
                  ))}
                </ol>

                {(seguindo.chaveAcesso || seguindo.numero !== null) && (
                  <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-800 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-200">
                    {seguindo.numero !== null && (
                      <p>
                        Número <strong>{seguindo.serie}/{String(seguindo.numero).padStart(9, '0')}</strong>
                      </p>
                    )}
                    {seguindo.chaveAcesso && <p className="mt-1 font-mono break-all">{seguindo.chaveAcesso}</p>}
                  </div>
                )}

                <Link to={`/documentos/${seguindo.id}`}>
                  <Button className="w-full">Abrir no visualizador de documentos</Button>
                </Link>
              </div>
            )}
          </Card>

          {historico.length > 0 && (
            <Card className="p-5">
              <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">Emissões desta sessão</h2>
              <div className="mt-3 space-y-1.5">
                {historico.map((h) => (
                  <div key={h.id} className="flex items-center justify-between gap-2 text-xs">
                    <Link to={`/documentos/${h.id}`} className="truncate font-mono text-[10px] text-zinc-500 hover:text-blue-600 dark:text-zinc-400 dark:hover:text-blue-300">
                      {h.modelo.toUpperCase()} · {h.id.slice(0, 8)}…
                    </Link>
                    <Badge className={STATUS_COLOR[h.status]}>{h.status}</Badge>
                  </div>
                ))}
              </div>
            </Card>
          )}
        </div>
      </div>
    </>
  )
}
