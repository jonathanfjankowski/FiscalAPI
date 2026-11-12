import { useMemo, useState } from 'react'
import {
  CircleAlert,
  Copy,
  Download,
  Eraser,
  Pause,
  Play,
  Search,
  Terminal,
} from 'lucide-react'
import { useTrafego } from '../hooks/useTrafego'
import { comoCurl, type RegistroTrafego } from '../lib/trafego'
import { copiar } from '../lib/format'
import { cn } from '../lib/utils'
import {
  Badge,
  Button,
  Dialog,
  EmptyState,
  Input,
  Select,
  Table,
  Td,
  Th,
  useToast,
} from '../components/ui'
import { PageHeader } from '../components/ui/estados'

// ==========================================================================
// Tráfego HTTP — o "Network tab" dentro do painel.
//
// Toda chamada que o painel faz à API fica aqui: método, rota, status,
// duração, headers e corpos. É o que responde "o que a API realmente
// devolveu?" sem abrir o DevTools — e o que se cola num chamado de suporte.
//
// Os segredos são redigidos ANTES de entrar no buffer (Authorization, senha,
// CSC, webhook secret). Nada é enviado ao backend nem persistido.
// ==========================================================================

const METODOS = ['GET', 'POST', 'PUT', 'DELETE'] as const

function corStatus(r: RegistroTrafego): string {
  if (r.status === null) return 'border-rejeitado/30 bg-rejeitado-soft text-rejeitado'
  if (r.status >= 500) return 'border-rejeitado/30 bg-rejeitado-soft text-rejeitado'
  if (r.status >= 400) return 'border-contingencia/30 bg-contingencia-soft text-contingencia'
  if (r.status >= 300) return 'border-carimbo/25 bg-carimbo-soft text-carimbo'
  return 'border-aprovado/30 bg-aprovado-soft text-aprovado'
}

function corMetodo(metodo: string): string {
  if (metodo === 'GET') return 'text-ink-muted'
  if (metodo === 'POST') return 'text-carimbo'
  if (metodo === 'PUT') return 'text-contingencia'
  return 'text-rejeitado'
}

function hora(epoch: number): string {
  return new Date(epoch).toLocaleTimeString('pt-BR', { hour12: false })
}

function formatarBytes(n: number): string {
  if (n === 0) return '—'
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

export default function Trafego() {
  const { registros, gravando, alternar, limpar } = useTrafego()
  const toast = useToast()
  const [busca, setBusca] = useState('')
  const [metodo, setMetodo] = useState('')
  const [classe, setClasse] = useState('')
  const [aberto, setAberto] = useState<RegistroTrafego | null>(null)

  const filtrados = useMemo(() => {
    const termo = busca.trim().toLowerCase()
    return registros.filter((r) => {
      if (metodo && r.metodo !== metodo) return false
      if (classe === 'erro' && r.ok) return false
      if (classe === 'ok' && !r.ok) return false
      if (classe === 'rede' && r.status !== null) return false
      if (classe === 'tenant' && r.origem !== 'tenant') return false
      if (!termo) return true
      return (
        r.rota.toLowerCase().includes(termo) ||
        (r.responseBody?.toLowerCase().includes(termo) ?? false) ||
        (r.requestBody?.toLowerCase().includes(termo) ?? false)
      )
    })
  }, [registros, busca, metodo, classe])

  const estatisticas = useMemo(() => {
    const falhas = registros.filter((r) => !r.ok).length
    const tempos = registros.map((r) => r.duracaoMs)
    const media = tempos.length ? Math.round(tempos.reduce((a, b) => a + b, 0) / tempos.length) : 0
    const pior = tempos.length ? Math.max(...tempos) : 0
    return { falhas, media, pior }
  }, [registros])

  function exportar() {
    const blob = new Blob([JSON.stringify(filtrados, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `trafego-fiscalapi-${Date.now()}.json`
    a.click()
    URL.revokeObjectURL(url)
    toast({ tipo: 'sucesso', titulo: `${filtrados.length} chamada(s) exportada(s)` })
  }

  return (
    <>
      <PageHeader
        titulo="Tráfego HTTP"
        descricao="Cada chamada do painel à API, com request e response. Segredos redigidos; nada sai deste navegador."
        acoes={
          <>
            <Button onClick={alternar} variant={gravando ? 'secondary' : 'primary'}>
              {gravando ? (
                <>
                  <Pause className="h-3.5 w-3.5" aria-hidden />
                  Pausar
                </>
              ) : (
                <>
                  <Play className="h-3.5 w-3.5" aria-hidden />
                  Retomar
                </>
              )}
            </Button>
            <Button onClick={exportar} disabled={filtrados.length === 0}>
              <Download className="h-3.5 w-3.5" aria-hidden />
              Exportar
            </Button>
            <Button variant="ghost" onClick={limpar} disabled={registros.length === 0}>
              <Eraser className="h-3.5 w-3.5" aria-hidden />
              Limpar
            </Button>
          </>
        }
      />

      {/* ---------- Fita de estatísticas ---------- */}
      <div className="mb-4 flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border border-line bg-surface px-4 py-3">
        <Estatistica rotulo="Chamadas" valor={registros.length} />
        <Separador />
        <Estatistica rotulo="Falhas" valor={estatisticas.falhas} alerta={estatisticas.falhas > 0} />
        <Separador />
        <Estatistica rotulo="Tempo médio" valor={`${estatisticas.media} ms`} />
        <Separador />
        <Estatistica rotulo="Mais lenta" valor={`${estatisticas.pior} ms`} />
        {!gravando && (
          <Badge className="ml-auto border-contingencia/30 bg-contingencia-soft text-contingencia">
            gravação pausada
          </Badge>
        )}
      </div>

      {/* ---------- Filtros ---------- */}
      <div className="mb-4 flex flex-wrap items-center gap-2 rounded-lg border border-line bg-surface p-3">
        <div className="relative min-w-64 flex-1">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 h-3.5 w-3.5 -translate-y-1/2 text-ink-faint" aria-hidden />
          <Input
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Rota, corpo do request ou do response…"
            aria-label="Buscar no tráfego"
            className="h-8 pl-8"
          />
        </div>
        <Select value={metodo} onChange={(e) => setMetodo(e.target.value)} aria-label="Filtrar por método" className="h-8 w-32">
          <option value="">Todos os métodos</option>
          {METODOS.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </Select>
        <Select value={classe} onChange={(e) => setClasse(e.target.value)} aria-label="Filtrar por resultado" className="h-8 w-44">
          <option value="">Todos os resultados</option>
          <option value="ok">Só sucesso (2xx)</option>
          <option value="erro">Só falhas</option>
          <option value="rede">Só falha de rede</option>
          <option value="tenant">Só caminho do integrador</option>
        </Select>
      </div>

      {/* ---------- Lista ---------- */}
      <div className="rounded-lg border border-line bg-surface">
        {filtrados.length === 0 ? (
          <EmptyState
            titulo={registros.length === 0 ? 'Nenhuma chamada registrada' : 'Nenhuma chamada com estes filtros'}
            descricao={
              registros.length === 0
                ? 'Navegue pelo painel — cada requisição à API aparece aqui.'
                : 'Ajuste os filtros para ver as chamadas.'
            }
          />
        ) : (
          <Table label="Chamadas HTTP do painel">
            <thead>
              <tr>
                <Th className="w-24">Hora</Th>
                <Th className="w-20">Método</Th>
                <Th>Rota</Th>
                <Th className="w-24">Status</Th>
                <Th className="w-24 text-right">Duração</Th>
                <Th className="w-24 text-right">Tamanho</Th>
                <Th className="w-24">Origem</Th>
              </tr>
            </thead>
            <tbody>
              {filtrados.map((r) => (
                <tr
                  key={r.id}
                  onClick={() => setAberto(r)}
                  className="cursor-pointer transition-colors hover:bg-surface-sunken/60"
                >
                  <Td className="font-mono text-[11px] text-ink-muted tabular">{hora(r.inicio)}</Td>
                  <Td>
                    <span className={cn('font-mono text-[11px] font-medium', corMetodo(r.metodo))}>
                      {r.metodo}
                    </span>
                  </Td>
                  <Td className="max-w-0 truncate font-mono text-[11px] text-ink" title={r.rota}>
                    {r.rota}
                  </Td>
                  <Td>
                    <Badge className={corStatus(r)}>{r.status ?? 'rede'}</Badge>
                  </Td>
                  <Td
                    className={cn(
                      'text-right text-xs tabular',
                      r.duracaoMs > 1000 ? 'text-contingencia' : 'text-ink-muted',
                    )}
                  >
                    {r.duracaoMs} ms
                  </Td>
                  <Td className="text-right text-xs text-ink-muted tabular">{formatarBytes(r.bytes)}</Td>
                  <Td>
                    <Badge className={r.origem === 'tenant' ? 'border-carimbo/25 bg-carimbo-soft text-carimbo' : undefined}>
                      {r.origem === 'tenant' ? 'integrador' : 'operador'}
                    </Badge>
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </div>

      <p className="mt-3 text-[11px] leading-relaxed text-ink-faint">
        Buffer de 300 chamadas em memória — as mais antigas caem. Corpos acima de 64 KB são
        truncados. <code className="font-mono">Authorization</code>, senha, CSC e webhook secret
        são redigidos antes de entrar no registro.
      </p>

      {aberto && <DetalheChamada registro={aberto} onClose={() => setAberto(null)} />}
    </>
  )
}

function Estatistica({ rotulo, valor, alerta }: { rotulo: string; valor: number | string; alerta?: boolean }) {
  return (
    <div className="flex items-baseline gap-2">
      <span className="text-[11px] text-ink-muted">{rotulo}</span>
      <span className={cn('text-lg font-semibold tabular', alerta ? 'text-rejeitado' : 'text-ink')}>
        {valor}
      </span>
    </div>
  )
}

function Separador() {
  return <span className="h-4 w-px bg-line" aria-hidden />
}

// ---------- Detalhe ----------

type AbaDetalhe = 'response' | 'request' | 'headers'

function DetalheChamada({ registro, onClose }: { registro: RegistroTrafego; onClose: () => void }) {
  const toast = useToast()
  const [aba, setAba] = useState<AbaDetalhe>('response')

  return (
    <Dialog
      open
      onClose={onClose}
      titulo={`${registro.metodo} ${registro.rota}`}
      descricao={`${registro.status ?? 'falha de rede'} · ${registro.duracaoMs} ms · ${hora(registro.inicio)}`}
      largura="xl"
      rodape={
        <>
          <Button
            variant="ghost"
            onClick={async () => {
              if (await copiar(comoCurl(registro))) toast({ tipo: 'sucesso', titulo: 'cURL copiado' })
            }}
          >
            <Terminal className="h-3.5 w-3.5" aria-hidden />
            Copiar cURL
          </Button>
          <Button onClick={onClose}>Fechar</Button>
        </>
      }
    >
      {registro.erro && (
        <div className="mb-3 flex items-start gap-2 rounded border border-rejeitado/30 bg-rejeitado-soft/50 px-3 py-2">
          <CircleAlert className="mt-px h-3.5 w-3.5 shrink-0 text-rejeitado" aria-hidden />
          <p className="text-xs text-ink">{registro.erro}</p>
        </div>
      )}

      <div className="mb-3 flex gap-1 border-b border-line">
        {(['response', 'request', 'headers'] as const).map((a) => (
          <button
            key={a}
            type="button"
            onClick={() => setAba(a)}
            className={cn(
              '-mb-px border-b-2 px-3 py-1.5 text-xs transition-colors',
              aba === a
                ? 'border-carimbo font-medium text-ink'
                : 'border-transparent text-ink-muted hover:text-ink',
            )}
          >
            {a === 'response' ? 'Response' : a === 'request' ? 'Request' : 'Headers'}
          </button>
        ))}
      </div>

      {aba === 'headers' ? (
        <div className="grid gap-4 sm:grid-cols-2">
          <BlocoHeaders titulo="Request" headers={registro.requestHeaders} />
          <BlocoHeaders titulo="Response" headers={registro.responseHeaders} />
        </div>
      ) : (
        <Corpo
          titulo={aba === 'response' ? 'Corpo da resposta' : 'Corpo da requisição'}
          texto={aba === 'response' ? registro.responseBody : registro.requestBody}
          truncado={registro.truncado}
        />
      )}
    </Dialog>
  )
}

function BlocoHeaders({ titulo, headers }: { titulo: string; headers: Record<string, string> }) {
  const entradas = Object.entries(headers)
  return (
    <div>
      <p className="mb-1.5 text-[11px] font-medium text-ink-muted">{titulo}</p>
      {entradas.length === 0 ? (
        <p className="text-xs text-ink-faint">Nenhum header.</p>
      ) : (
        <dl className="divide-y divide-line rounded border border-line">
          {entradas.map(([chave, valor]) => (
            <div key={chave} className="px-2.5 py-1.5">
              <dt className="font-mono text-[10px] text-ink-faint">{chave}</dt>
              <dd className="font-mono text-[11px] break-all text-ink">{valor}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}

function Corpo({ titulo, texto, truncado }: { titulo: string; texto: string | null; truncado: boolean }) {
  const toast = useToast()

  if (!texto) {
    return <p className="py-6 text-center text-xs text-ink-faint">Sem corpo.</p>
  }

  return (
    <div>
      <div className="mb-1.5 flex items-center justify-between gap-2">
        <p className="text-[11px] font-medium text-ink-muted">
          {titulo}
          {truncado && <span className="ml-1.5 text-contingencia">(truncado em 64 KB)</span>}
        </p>
        <Button
          variant="ghost"
          size="sm"
          onClick={async () => {
            if (await copiar(texto)) toast({ tipo: 'sucesso', titulo: 'Corpo copiado' })
          }}
          aria-label="Copiar corpo"
        >
          <Copy className="h-3.5 w-3.5" aria-hidden />
        </Button>
      </div>
      <pre className="max-h-[50vh] overflow-auto rounded border border-line bg-surface-sunken p-3 font-mono text-[11px] leading-relaxed text-ink">
        {texto}
      </pre>
    </div>
  )
}
