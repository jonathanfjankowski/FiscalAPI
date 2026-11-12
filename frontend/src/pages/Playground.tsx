import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Copy, Eye, KeyRound, Play, RefreshCw, Terminal } from 'lucide-react'
import { api, ApiError, type EmissaoResponse } from '../lib/api'
import { STATUS_TERMINAIS } from '../lib/fiscal'
import { copiar, formatarDecimal } from '../lib/format'
import { cn } from '../lib/utils'
import {
  Button,
  Dialog,
  Field,
  Input,
  Select,
  StatusBadge,
  Textarea,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'
import { SegredoUnico } from '../components/ui/segredo'
import { Timeline, TrilhoStatus, type Transicao } from '../components/fiscal'

// ==========================================================================
// Playground — o caminho real do integrador.
//
// Split-pane: requisição à esquerda, resposta à direita. Usa API key de
// tenant (não o JWT do operador), então exercita exatamente o que o ERP
// faz: header ApiKey + Idempotency-Key.
//
// O preview do DANFE não transmite nada — é o que o integrador chama antes
// de emitir, para o contribuinte conferir o documento.
// ==========================================================================

const KEY_STORAGE = 'fiscal.playground.apikey'

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
  aliquota: 18,
}

const totalItem = (i: ItemForm) => Math.round(i.quantidade * i.valorUnitario * 100) / 100

function montarPayload(
  ambiente: string, serie: number, itens: ItemForm[],
  destCnpj: string, destNome: string, natureza: string,
) {
  const itensJson = itens.map((i) => {
    const total = totalItem(i)
    return {
      codigo: i.codigo,
      descricao: i.descricao,
      ncm: i.ncm,
      cfop: i.cfop,
      quantidade: i.quantidade,
      valorUnitario: i.valorUnitario,
      valorTotal: total,
      impostos: [
        {
          cst: '00',
          baseCalculo: total,
          aliquota: i.aliquota,
          valor: Math.round(((total * i.aliquota) / 100) * 100) / 100,
        },
      ],
    }
  })
  const valorProdutos = Math.round(itensJson.reduce((a, i) => a + i.valorTotal, 0) * 100) / 100
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

type Modelo = 'nfe' | 'nfce' | 'nfse'

/**
 * Payload do DPS da NFS-e Nacional (layout 1.01) — contrato DISTINTO do
 * EmissaoRequest de NF-e/NFC-e. A rota é POST /v1/documentos-fiscais/nfse/dps.
 */
function montarDps(
  ambiente: string, serie: number,
  destCnpj: string, destNome: string,
  descricaoServico: string, valorServico: number, aliquotaIssqn: number,
  codigoTributarioNacional: string,
) {
  return {
    ambiente,
    serie,
    tomador: { cnpjCpf: destCnpj, nome: destNome },
    servico: {
      codigoTributarioNacional,
      descricaoServico,
    },
    valores: {
      valorServicos: valorServico,
      tributacaoIssqn: 1,
      retencaoIssqn: 1,
      aliquotaIssqn,
    },
  }
}

export default function Playground() {
  const toast = useToast()
  const { data: tenants } = useQuery({ queryKey: ['tenants'], queryFn: api.listTenants })

  const [tenantId, setTenantId] = useState('')
  const [modelo, setModelo] = useState<Modelo>('nfe')
  const [ambiente, setAmbiente] = useState('homologacao')
  const [serie, setSerie] = useState(1)
  const [apiKey, setApiKey] = useState(() => sessionStorage.getItem(KEY_STORAGE) ?? '')
  const [modo, setModo] = useState<'form' | 'json'>('form')

  const [destCnpj, setDestCnpj] = useState('12345678000199')
  const [destNome, setDestNome] = useState('Destinatário Teste LTDA')
  const [natureza, setNatureza] = useState('Venda de teste')
  const [descServico, setDescServico] = useState('Serviço de teste')
  const [valorServico, setValorServico] = useState(100)
  const [aliquotaIssqn, setAliquotaIssqn] = useState(5)
  const [cTribNac, setCTribNac] = useState('010101')
  const [itens, setItens] = useState<ItemForm[]>([{ ...ITEM_PADRAO }])

  const jsonGerado = useMemo(
    () =>
      JSON.stringify(
        modelo === 'nfse'
          ? montarDps(ambiente, serie, destCnpj, destNome, descServico, valorServico, aliquotaIssqn, cTribNac)
          : montarPayload(ambiente, serie, itens, destCnpj, destNome, natureza),
        null,
        2,
      ),
    [modelo, ambiente, serie, itens, destCnpj, destNome, natureza, descServico, valorServico, aliquotaIssqn, cTribNac],
  )
  const [jsonCru, setJsonCru] = useState('')
  const [erroJson, setErroJson] = useState<string | null>(null)

  const [idempotencyKey, setIdempotencyKey] = useState<string>(() => crypto.randomUUID())
  const [seguindo, setSeguindo] = useState<EmissaoResponse | null>(null)
  const [transicoes, setTransicoes] = useState<Transicao[]>([])
  const [respostaBruta, setRespostaBruta] = useState<string | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [chaveGerada, setChaveGerada] = useState<{ chave: string; aviso: string } | null>(null)

  // Token do acompanhamento: emissão nova invalida o loop anterior, e o loop
  // aborta se o componente desmontou.
  const seguirToken = useRef(0)
  const ativo = useRef(true)
  useEffect(() => {
    ativo.current = true
    return () => {
      ativo.current = false
    }
  }, [])

  const totalNota = itens.reduce((a, i) => a + totalItem(i), 0)

  const salvarKey = (v: string) => {
    setApiKey(v)
    sessionStorage.setItem(KEY_STORAGE, v)
  }

  const mutGerarKey = useMutation({
    mutationFn: () => api.createApiKey(tenantId, { descricao: 'Playground', ambiente: 'homologacao' }),
    onSuccess: (k) => {
      salvarKey(k.chave)
      setChaveGerada({ chave: k.chave, aviso: k.aviso })
    },
    onError: (e) => toast({ tipo: 'erro', titulo: 'Falha ao gerar chave', detalhe: e.message }),
  })

  const mutPreview = useMutation({
    mutationFn: async () => {
      const payload = modo === 'json' ? JSON.parse(jsonCru) : JSON.parse(jsonGerado)
      return api.previewPdf(apiKey.trim(), modelo === 'nfce' ? 'NFCE' : 'NFE', payload)
    },
    onSuccess: (blob) => {
      if (previewUrl) URL.revokeObjectURL(previewUrl)
      setPreviewUrl(URL.createObjectURL(blob))
    },
    onError: (e) =>
      toast({
        tipo: 'erro',
        titulo: 'Falha na pré-visualização',
        detalhe: e instanceof ApiError ? (e.detail ?? e.message) : e.message,
      }),
  })

  const mutEmitir = useMutation({
    mutationFn: async () => {
      const key = apiKey.trim()
      if (!key) throw new Error('Informe a API key do tenant.')
      let body: unknown
      if (modo === 'json') {
        try {
          body = JSON.parse(jsonCru)
          setErroJson(null)
        } catch (e) {
          // Erro de sintaxe é do campo — não vira "emissão rejeitada".
          setErroJson(e instanceof Error ? e.message : 'JSON inválido')
          throw new Error('JSON inválido')
        }
      } else {
        body = JSON.parse(jsonGerado)
      }
      return api.emitir(modelo, key, body, idempotencyKey)
    },
    onSuccess: (resp) => {
      setRespostaBruta(JSON.stringify(resp, null, 2))
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

      if (!STATUS_TERMINAIS.includes(resp.status)) {
        seguirToken.current += 1
        void acompanhar(resp.id, apiKey.trim(), [resp.status], seguirToken.current)
      }
    },
    onError: (e) => {
      if (e.message === 'JSON inválido') return
      const msg = e instanceof ApiError ? (e.detail ?? e.message) : e.message
      setRespostaBruta(msg)
      toast({ tipo: 'erro', titulo: 'Emissão recusada', detalhe: msg })
    },
  })

  async function acompanhar(id: string, key: string, vistos: string[], token: number) {
    for (let i = 0; i < 45; i++) {
      await new Promise((r) => setTimeout(r, 2000))
      if (!ativo.current || seguirToken.current !== token) return
      try {
        const doc = await api.consultarComoTenant(id, key)
        setSeguindo(doc)
        setRespostaBruta(JSON.stringify(doc, null, 2))
        if (doc.status !== vistos[vistos.length - 1]) {
          vistos = [...vistos, doc.status]
          setTransicoes((t) => [...t, { status: doc.status, em: new Date().toISOString(), motivo: doc.motivoStatus }])
        }
        if (STATUS_TERMINAIS.includes(doc.status)) return
      } catch {
        // falha de polling: tenta de novo no próximo ciclo
      }
    }
  }

  const curl = useMemo(() => {
    const corpo = modo === 'json' ? jsonCru : jsonGerado
    const base = window.location.origin
    return [
      `curl -X POST "${base}/v1/documentos-fiscais/${modelo}" \\`,
      `  -H "Authorization: ApiKey ${apiKey || '<SUA_API_KEY>'}" \\`,
      `  -H "Idempotency-Key: ${idempotencyKey}" \\`,
      `  -H "Content-Type: application/json" \\`,
      `  -d '${corpo.replace(/\n\s*/g, ' ')}'`,
    ].join('\n')
  }, [modelo, apiKey, idempotencyKey, modo, jsonCru, jsonGerado])

  const setItem = (idx: number, campo: keyof ItemForm, valor: string) =>
    setItens((arr) =>
      arr.map((it, i) =>
        i === idx
          ? {
              ...it,
              [campo]:
                campo === 'codigo' || campo === 'descricao' || campo === 'ncm' || campo === 'cfop'
                  ? valor
                  : Number(valor) || 0,
            }
          : it,
      ),
    )

  return (
    <>
      <PageHeader
        titulo="Playground"
        descricao="Emite pelo caminho real do integrador: API key de tenant + Idempotency-Key."
      />

      <div className="grid items-start gap-4 xl:grid-cols-[1fr_24rem]">
        {/* ---------- Requisição ---------- */}
        <div className="space-y-4">
          <section className="rounded-lg border border-line bg-surface p-4">
            <h2 className="mb-3 text-xs font-semibold text-ink-muted">Configuração</h2>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <Field label="Empresa" htmlFor="pg-tenant">
                <Select id="pg-tenant" value={tenantId} onChange={(e) => setTenantId(e.target.value)}>
                  <option value="">— selecione —</option>
                  {(tenants ?? []).map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.razaoSocial}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field label="Modelo" htmlFor="pg-modelo">
                <Select id="pg-modelo" value={modelo} onChange={(e) => setModelo(e.target.value as Modelo)}>
                  <option value="nfe">NF-e (55)</option>
                  <option value="nfce">NFC-e (65)</option>
                  <option value="nfse">NFS-e (DPS)</option>
                </Select>
              </Field>
              <Field label="Ambiente" htmlFor="pg-amb">
                <Select id="pg-amb" value={ambiente} onChange={(e) => setAmbiente(e.target.value)}>
                  <option value="homologacao">Homologação</option>
                  <option value="producao">Produção</option>
                </Select>
              </Field>
              <Field label="Série" htmlFor="pg-serie">
                <Input id="pg-serie" type="number" min={1} max={999} value={serie} onChange={(e) => setSerie(Number(e.target.value) || 1)} />
              </Field>
            </div>

            <Field
              label="API key do tenant"
              htmlFor="pg-key"
              className="mt-3"
              hint="Fica só na sessão deste navegador. Nunca vai para a URL."
            >
              <div className="flex gap-2">
                <Input
                  id="pg-key"
                  type="password"
                  value={apiKey}
                  onChange={(e) => salvarKey(e.target.value)}
                  placeholder="fk_test_…"
                  className="font-mono"
                  autoComplete="off"
                />
                <Button
                  onClick={() => mutGerarKey.mutate()}
                  loading={mutGerarKey.isPending}
                  disabled={!tenantId}
                  title={tenantId ? 'Gerar chave de homologação para esta empresa' : 'Selecione a empresa'}
                >
                  <KeyRound className="h-3.5 w-3.5" aria-hidden />
                  Gerar
                </Button>
              </div>
            </Field>

            <div className="mt-3 flex flex-wrap items-center gap-2">
              <Field label="Idempotency-Key" htmlFor="pg-idem" className="min-w-72 flex-1">
                <Input id="pg-idem" value={idempotencyKey} onChange={(e) => setIdempotencyKey(e.target.value)} className="font-mono text-xs" />
              </Field>
              <Button
                className="mt-5"
                size="sm"
                onClick={() => setIdempotencyKey(crypto.randomUUID())}
                title="Gerar nova chave (reusar a mesma demonstra idempotência)"
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden />
                Nova
              </Button>
            </div>
          </section>

          <section className="rounded-lg border border-line bg-surface p-4">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-xs font-semibold text-ink-muted">Documento</h2>
              <div className="flex rounded border border-line p-0.5 text-xs">
                {(['form', 'json'] as const).map((m) => (
                  <button
                    key={m}
                    type="button"
                    onClick={() => {
                      if (m === 'json') setJsonCru(jsonGerado)
                      setModo(m)
                    }}
                    className={cn(
                      'rounded px-2.5 py-1 transition-colors',
                      modo === m ? 'bg-carimbo-soft font-medium text-carimbo' : 'text-ink-muted hover:text-ink',
                    )}
                  >
                    {m === 'form' ? 'Formulário' : 'JSON'}
                  </button>
                ))}
              </div>
            </div>

            {modo === 'form' ? (
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                  <Field label="CNPJ/CPF do destinatário" htmlFor="pg-cnpj">
                    <Input id="pg-cnpj" value={destCnpj} onChange={(e) => setDestCnpj(e.target.value)} className="font-mono" />
                  </Field>
                  <Field label="Nome do destinatário" htmlFor="pg-nome" className="col-span-2">
                    <Input id="pg-nome" value={destNome} onChange={(e) => setDestNome(e.target.value)} />
                  </Field>
                  <Field label="Natureza da operação" htmlFor="pg-nat">
                    <Input id="pg-nat" value={natureza} onChange={(e) => setNatureza(e.target.value)} />
                  </Field>
                </div>

                {modelo === 'nfse' && (
                  <div className="grid grid-cols-2 gap-3 rounded border border-line p-3 lg:grid-cols-4">
                    <Field label="Código tributário nacional" htmlFor="pg-ctrib" className="col-span-2" hint="cTribNac — 6 dígitos (LC 116)">
                      <Input id="pg-ctrib" value={cTribNac} onChange={(e) => setCTribNac(e.target.value)} maxLength={6} className="font-mono" />
                    </Field>
                    <Field label="Valor do serviço" htmlFor="pg-vserv">
                      <Input id="pg-vserv" type="number" min={0} step="any" value={valorServico} onChange={(e) => setValorServico(Number(e.target.value) || 0)} />
                    </Field>
                    <Field label="Alíquota ISSQN %" htmlFor="pg-iss">
                      <Input id="pg-iss" type="number" min={0} max={100} step="any" value={aliquotaIssqn} onChange={(e) => setAliquotaIssqn(Number(e.target.value) || 0)} />
                    </Field>
                    <Field label="Descrição do serviço" htmlFor="pg-dserv" className="col-span-2 lg:col-span-4">
                      <Input id="pg-dserv" value={descServico} onChange={(e) => setDescServico(e.target.value)} />
                    </Field>
                  </div>
                )}

                <div className={cn(modelo === 'nfse' && 'hidden')}>
                  <div className="mb-2 flex items-center justify-between">
                    <span className="text-xs text-ink-muted">Itens</span>
                    <Button size="sm" onClick={() => setItens((a) => [...a, { ...ITEM_PADRAO, codigo: `SKU${a.length + 1}` }])}>
                      + Item
                    </Button>
                  </div>
                  <div className="space-y-2">
                    {itens.map((item, idx) => (
                      <div key={idx} className="grid grid-cols-12 items-end gap-2 rounded border border-line p-2.5">
                        <Field label="Código" htmlFor={`cod-${idx}`} className="col-span-2">
                          <Input id={`cod-${idx}`} value={item.codigo} onChange={(e) => setItem(idx, 'codigo', e.target.value)} />
                        </Field>
                        <Field label="Descrição" htmlFor={`desc-${idx}`} className="col-span-4">
                          <Input id={`desc-${idx}`} value={item.descricao} onChange={(e) => setItem(idx, 'descricao', e.target.value)} />
                        </Field>
                        <Field label="NCM" htmlFor={`ncm-${idx}`} className="col-span-2">
                          <Input id={`ncm-${idx}`} value={item.ncm} onChange={(e) => setItem(idx, 'ncm', e.target.value)} className="font-mono" />
                        </Field>
                        <Field label="CFOP" htmlFor={`cfop-${idx}`} className="col-span-1">
                          <Input id={`cfop-${idx}`} value={item.cfop} onChange={(e) => setItem(idx, 'cfop', e.target.value)} className="font-mono" />
                        </Field>
                        <Field label="Qtd" htmlFor={`qtd-${idx}`} className="col-span-1">
                          <Input id={`qtd-${idx}`} type="number" min={0} step="any" value={item.quantidade} onChange={(e) => setItem(idx, 'quantidade', e.target.value)} />
                        </Field>
                        <Field label="Vlr unit." htmlFor={`vu-${idx}`} className="col-span-1">
                          <Input id={`vu-${idx}`} type="number" min={0} step="any" value={item.valorUnitario} onChange={(e) => setItem(idx, 'valorUnitario', e.target.value)} />
                        </Field>
                        <Field label="ICMS %" htmlFor={`icms-${idx}`} className="col-span-1">
                          <Input id={`icms-${idx}`} type="number" min={0} step="any" value={item.aliquota} onChange={(e) => setItem(idx, 'aliquota', e.target.value)} />
                        </Field>
                        <div className="col-span-12 flex items-center justify-between text-[11px] text-ink-muted">
                          <span className="tabular">
                            Total: <span className="text-ink">{formatarDecimal(totalItem(item))}</span>
                          </span>
                          {itens.length > 1 && (
                            <button
                              type="button"
                              className="text-rejeitado hover:underline"
                              onClick={() => setItens((a) => a.filter((_, i) => i !== idx))}
                            >
                              remover
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                  <p className="mt-2 text-right text-[13px] text-ink-muted tabular">
                    Total da nota: <span className="font-semibold text-ink">{formatarDecimal(totalNota)}</span>
                  </p>
                </div>
              </div>
            ) : (
              <Field label="Payload (JSON)" htmlFor="pg-json" error={erroJson ?? undefined}>
                <Textarea
                  id="pg-json"
                  rows={16}
                  mono
                  value={jsonCru}
                  onChange={(e) => {
                    setJsonCru(e.target.value)
                    setErroJson(null)
                  }}
                  spellCheck={false}
                />
              </Field>
            )}

            <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-line pt-3">
              <Button
                variant="primary"
                loading={mutEmitir.isPending}
                onClick={() => mutEmitir.mutate()}
                disabled={!apiKey.trim()}
              >
                <Play className="h-3.5 w-3.5" aria-hidden />
                Emitir {modelo.toUpperCase()}
              </Button>
              <Button
                loading={mutPreview.isPending}
                onClick={() => mutPreview.mutate()}
                disabled={!apiKey.trim() || modelo === 'nfse'}
                title={modelo === 'nfse' ? 'Preview disponível para NF-e e NFC-e' : 'Ver o DANFE sem transmitir'}
              >
                <Eye className="h-3.5 w-3.5" aria-hidden />
                Pré-visualizar DANFE
              </Button>
              <Button
                variant="ghost"
                onClick={async () => {
                  if (await copiar(curl)) toast({ tipo: 'sucesso', titulo: 'cURL copiado' })
                }}
              >
                <Terminal className="h-3.5 w-3.5" aria-hidden />
                Copiar cURL
              </Button>
            </div>
          </section>
        </div>

        {/* ---------- Resposta ---------- */}
        <div className="space-y-4">
          <section className="rounded-lg border border-line bg-surface p-4">
            <h2 className="mb-3 text-xs font-semibold text-ink-muted">Acompanhamento</h2>
            {!seguindo ? (
              <p className="py-8 text-center text-xs text-ink-faint">
                Emita um documento para acompanhar o status aqui.
              </p>
            ) : (
              <div className="space-y-4">
                <div className="flex items-center justify-between gap-2">
                  <StatusBadge status={seguindo.status} />
                  <code className="truncate font-mono text-[10px] text-ink-faint">{seguindo.id.slice(0, 8)}…</code>
                </div>

                <TrilhoStatus status={seguindo.status} />

                {seguindo.chaveAcesso && (
                  <div className="rounded border border-aprovado/30 bg-aprovado-soft/50 p-2.5 text-xs">
                    {seguindo.numero !== null && (
                      <p className="text-ink">
                        Número{' '}
                        <strong className="tabular">
                          {seguindo.serie}/{String(seguindo.numero).padStart(9, '0')}
                        </strong>
                      </p>
                    )}
                    <p className="mt-1 font-mono text-[10px] break-all text-ink-muted">{seguindo.chaveAcesso}</p>
                  </div>
                )}

                <Timeline transicoes={transicoes} />

                <Button asChild className="w-full">
                  <Link to={`/documentos/${seguindo.id}`}>Abrir no detalhe</Link>
                </Button>
              </div>
            )}
          </section>

          {respostaBruta && (
            <section className="rounded-lg border border-line bg-surface">
              <div className="flex items-center justify-between border-b border-line px-3 py-1.5">
                <h2 className="text-xs font-semibold text-ink-muted">Resposta</h2>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={async () => {
                    if (await copiar(respostaBruta)) toast({ tipo: 'sucesso', titulo: 'Resposta copiada' })
                  }}
                  aria-label="Copiar resposta"
                >
                  <Copy className="h-3.5 w-3.5" aria-hidden />
                </Button>
              </div>
              <pre className="max-h-72 overflow-auto p-3 font-mono text-[10px] leading-relaxed text-ink-muted">
                {respostaBruta}
              </pre>
            </section>
          )}
        </div>
      </div>

      {/* ---------- Preview do DANFE ---------- */}
      <Dialog
        open={!!previewUrl}
        onClose={() => {
          if (previewUrl) URL.revokeObjectURL(previewUrl)
          setPreviewUrl(null)
        }}
        titulo="Pré-visualização do DANFE"
        descricao="Nada foi transmitido, nenhum número foi reservado."
        largura="xl"
      >
        {previewUrl && (
          <object data={previewUrl} type="application/pdf" className="h-[70vh] w-full rounded border border-line">
            <p className="p-4 text-xs text-ink-muted">
              Não foi possível exibir o PDF inline.{' '}
              <a href={previewUrl} download className="text-carimbo underline">
                Baixar
              </a>
            </p>
          </object>
        )}
      </Dialog>

      {/* ---------- Chave gerada ---------- */}
      <Dialog
        open={!!chaveGerada}
        onClose={() => setChaveGerada(null)}
        titulo="Chave de teste gerada"
        descricao="Já preenchida no campo. Guarde-a se for usar fora desta sessão."
        rodape={<Button variant="primary" onClick={() => setChaveGerada(null)}>Fechar</Button>}
      >
        {chaveGerada && <SegredoUnico valor={chaveGerada.chave} aviso={chaveGerada.aviso} />}
      </Dialog>
    </>
  )
}
