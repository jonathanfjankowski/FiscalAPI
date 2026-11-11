import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Building2, Check, ChevronsUpDown, Search } from 'lucide-react'
import { api } from '../../lib/api'
import { useEmpresaContexto } from '../../hooks/useEmpresaContexto'
import { AmbienteBadge, Badge } from '../ui'
import { Popover, PopoverContent, PopoverTrigger } from '../ui/popover'
import { cn } from '../../lib/utils'
import { formatarCnpj } from '../../lib/format'

// ==========================================================================
// Seletor de empresa.
//
// Escreve no `?empresa=` (ver useEmpresaContexto) — trocar aqui afeta
// Documentos, Notas recebidas, Auditoria e Playground, em qualquer página.
// É o contexto que a versão anterior não tinha: o operador tinha que
// navegar Empresas → clicar → abas toda vez.
// ==========================================================================

export function EmpresaPicker() {
  const { empresaId, empresa, definirEmpresa } = useEmpresaContexto()
  const [aberto, setAberto] = useState(false)
  const [busca, setBusca] = useState('')

  const { data: empresas, isLoading } = useQuery({
    queryKey: ['tenants'],
    queryFn: api.listTenants,
    staleTime: 60_000,
  })

  const filtradas = useMemo(() => {
    const termo = busca.trim().toLowerCase()
    if (!termo) return empresas ?? []
    return (empresas ?? []).filter(
      (t) =>
        t.razaoSocial.toLowerCase().includes(termo) ||
        t.cnpj.includes(termo.replace(/\D/g, '')) ||
        t.uf.toLowerCase() === termo,
    )
  }, [empresas, busca])

  const rotulo = empresa ? empresa.razaoSocial : 'Todas as empresas'

  return (
    <Popover open={aberto} onOpenChange={setAberto}>
      <PopoverTrigger asChild>
        <button
          type="button"
          className={cn(
            'flex h-8 max-w-72 min-w-0 items-center gap-2 rounded border border-line-strong bg-surface px-2.5',
            'text-[13px] text-ink transition-colors hover:border-ink-faint',
            'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-carimbo',
          )}
          aria-label="Selecionar empresa"
        >
          <Building2 className="h-3.5 w-3.5 shrink-0 text-ink-faint" aria-hidden />
          <span className={cn('truncate', !empresa && 'text-ink-muted')}>{rotulo}</span>
          {empresa && (
            <span className="shrink-0" title={empresa.ambientePadrao}>
              <span
                className={cn(
                  'block h-1.5 w-1.5 rounded-full',
                  empresa.ambientePadrao === 'producao' ? 'bg-rejeitado' : 'bg-carimbo',
                )}
                aria-hidden
              />
              <span className="sr-only">
                {empresa.ambientePadrao === 'producao' ? 'produção' : 'homologação'}
              </span>
            </span>
          )}
          <ChevronsUpDown className="ml-auto h-3 w-3 shrink-0 text-ink-faint" aria-hidden />
        </button>
      </PopoverTrigger>

      <PopoverContent className="w-[22rem] p-0" align="start">
        <div className="flex items-center gap-2 border-b border-line px-3 py-2">
          <Search className="h-3.5 w-3.5 shrink-0 text-ink-faint" aria-hidden />
          <input
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Razão social, CNPJ ou UF…"
            className="h-6 w-full bg-transparent text-[13px] text-ink placeholder:text-ink-faint focus:outline-none"
            autoFocus
          />
        </div>

        <div className="max-h-80 overflow-y-auto p-1">
          <button
            type="button"
            onClick={() => {
              definirEmpresa(null)
              setAberto(false)
              setBusca('')
            }}
            className={cn(
              'flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-[13px]',
              'hover:bg-surface-sunken focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-carimbo',
              !empresaId && 'bg-surface-sunken',
            )}
          >
            <span className="flex-1 text-ink">Todas as empresas</span>
            {!empresaId && <Check className="h-3.5 w-3.5 text-carimbo" aria-hidden />}
          </button>

          <div className="my-1 h-px bg-line" />

          {isLoading && (
            <p className="px-2 py-3 text-center text-xs text-ink-faint">Carregando…</p>
          )}

          {!isLoading && filtradas.length === 0 && (
            <p className="px-2 py-3 text-center text-xs text-ink-faint">
              Nenhuma empresa encontrada.
            </p>
          )}

          {filtradas.map((t) => {
            const ativa = t.id === empresaId
            return (
              <button
                key={t.id}
                type="button"
                onClick={() => {
                  definirEmpresa(t.id)
                  setAberto(false)
                  setBusca('')
                }}
                className={cn(
                  'flex w-full items-start gap-2 rounded px-2 py-1.5 text-left',
                  'hover:bg-surface-sunken focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-carimbo',
                  ativa && 'bg-surface-sunken',
                )}
              >
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-[13px] text-ink">{t.razaoSocial}</span>
                  <span className="mt-0.5 flex items-center gap-1.5">
                    <span className="font-mono text-[10px] text-ink-faint">
                      {formatarCnpj(t.cnpj)}
                    </span>
                    <span className="text-[10px] text-ink-faint">{t.uf}</span>
                    <AmbienteBadge ambiente={t.ambientePadrao} />
                    {t.sandbox && <Badge className="text-[9px]">sandbox</Badge>}
                    {!t.ativo && <Badge className="text-[9px]">inativa</Badge>}
                  </span>
                </span>
                {ativa && <Check className="mt-1 h-3.5 w-3.5 shrink-0 text-carimbo" aria-hidden />}
              </button>
            )
          })}
        </div>
      </PopoverContent>
    </Popover>
  )
}