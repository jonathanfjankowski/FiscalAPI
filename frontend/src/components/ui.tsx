import { useEffect, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'
import { createPortal } from 'react-dom'
import { Loader2, X } from 'lucide-react'
import { cn, STATUS_COLOR, STATUS_LABEL } from '../lib/utils'

// ---------- Botão ----------

type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  loading?: boolean
}

const BUTTON_VARIANTS: Record<ButtonVariant, string> = {
  primary:
    'bg-blue-600 text-white hover:bg-blue-500 disabled:bg-blue-800/50 disabled:text-blue-200/60 dark:disabled:bg-blue-900/40 dark:disabled:text-blue-300/40',
  secondary:
    'border border-zinc-300 bg-white text-zinc-700 hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700',
  danger: 'bg-red-600 text-white hover:bg-red-500 dark:bg-red-600/90 dark:hover:bg-red-500',
  ghost:
    'text-zinc-500 hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-zinc-100',
}

export function Button({ variant = 'secondary', loading, className, children, disabled, ...rest }: ButtonProps) {
  return (
    <button
      className={cn(
        'inline-flex items-center justify-center gap-2 rounded-md px-3.5 py-1.5 text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/60 disabled:cursor-not-allowed disabled:opacity-60',
        BUTTON_VARIANTS[variant],
        className,
      )}
      disabled={disabled || loading}
      {...rest}
    >
      {loading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
      {children}
    </button>
  )
}

// ---------- Inputs ----------

const INPUT_BASE =
  'w-full rounded-md border border-zinc-300 bg-white px-3 py-1.5 text-sm text-zinc-900 placeholder:text-zinc-400 focus:border-blue-500/70 focus:outline-none focus:ring-1 focus:ring-blue-500/40 disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-100 dark:placeholder:text-zinc-500'

export function Input({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(INPUT_BASE, className)} {...rest} />
}

export function Textarea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={cn(INPUT_BASE, 'font-mono text-xs leading-relaxed', className)} {...rest} />
}

export function Select({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select className={cn(INPUT_BASE, 'appearance-none', className)} {...rest}>
      {children}
    </select>
  )
}

export function Field({
  label,
  hint,
  children,
  className,
}: {
  label: string
  hint?: string
  children: ReactNode
  className?: string
}) {
  return (
    <label className={cn('block', className)}>
      <span className="mb-1 block text-xs font-medium text-zinc-500 dark:text-zinc-400">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-[11px] text-zinc-400 dark:text-zinc-500">{hint}</span>}
    </label>
  )
}

// ---------- Badge ----------

export function Badge({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded border px-1.5 py-0.5 text-[11px] font-medium whitespace-nowrap',
        className ?? 'border-zinc-200 bg-zinc-100 text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800/60 dark:text-zinc-300',
      )}
    >
      {children}
    </span>
  )
}

export function StatusBadge({ status }: { status: string }) {
  return (
    <Badge className={STATUS_COLOR[status] ?? 'border-zinc-200 bg-zinc-100 text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800/60 dark:text-zinc-300'}>
      {STATUS_LABEL[status] ?? status}
    </Badge>
  )
}

// ---------- Card ----------

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn('rounded-lg border border-zinc-200 bg-white shadow-xs dark:border-zinc-800 dark:bg-zinc-900/60', className)}>
      {children}
    </div>
  )
}

export function StatCard({
  titulo,
  valor,
  destaque,
  alerta,
}: {
  titulo: string
  valor: number | string
  destaque?: string
  alerta?: boolean
}) {
  return (
    <Card className="p-4">
      <p className="text-xs font-medium text-zinc-500 dark:text-zinc-400">{titulo}</p>
      <p className={cn('mt-1.5 text-2xl font-semibold tabular-nums', alerta ? 'text-amber-600 dark:text-amber-300' : 'text-zinc-900 dark:text-zinc-100')}>
        {valor}
      </p>
      {destaque && <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{destaque}</p>}
    </Card>
  )
}

// ---------- Tabela densa (estilo ERP desktop) ----------

export function Table({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className="overflow-x-auto">
      <table className={cn('w-full text-left text-sm', className)}>{children}</table>
    </div>
  )
}

export function Th({ children, className }: { children?: ReactNode; className?: string }) {
  return (
    <th
      className={cn(
        'border-b border-zinc-200 bg-zinc-50 px-3 py-2 text-[11px] font-semibold tracking-wide text-zinc-500 uppercase dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-400',
        className,
      )}
    >
      {children}
    </th>
  )
}

export function Td({ children, className, title }: { children?: ReactNode; className?: string; title?: string }) {
  return (
    <td title={title} className={cn('border-b border-zinc-100 px-3 py-2 text-zinc-700 dark:border-zinc-800/60 dark:text-zinc-300', className)}>
      {children}
    </td>
  )
}

// ---------- Abas ----------

export function Tabs({
  abas,
  ativa,
  onChange,
}: {
  abas: { id: string; label: string }[]
  ativa: string
  onChange: (id: never) => void
}) {
  return (
    <div className="mb-5 flex gap-1 border-b border-zinc-200 dark:border-zinc-800">
      {abas.map((a) => (
        <button
          key={a.id}
          onClick={() => onChange(a.id as never)}
          className={cn(
            '-mb-px border-b-2 px-3.5 py-2 text-sm transition-colors',
            a.id === ativa
              ? 'border-blue-600 font-medium text-blue-700 dark:border-blue-500 dark:text-blue-300'
              : 'border-transparent text-zinc-500 hover:border-zinc-300 hover:text-zinc-800 dark:text-zinc-400 dark:hover:border-zinc-700 dark:hover:text-zinc-200',
          )}
        >
          {a.label}
        </button>
      ))}
    </div>
  )
}

// ---------- Toggle ----------

export function Toggle({
  checked,
  onChange,
  label,
  disabled,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label?: string
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative h-5 w-9 shrink-0 rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        checked ? 'bg-blue-600' : 'bg-zinc-300 dark:bg-zinc-700',
      )}
    >
      <span
        className={cn(
          'absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform',
          checked ? 'translate-x-4.5' : 'translate-x-0.5',
        )}
      />
    </button>
  )
}

// ---------- Modal ----------

export function Modal({
  open,
  onClose,
  titulo,
  children,
  rodape,
  largura,
}: {
  open: boolean
  onClose: () => void
  titulo: string
  children: ReactNode
  rodape?: ReactNode
  largura?: string
}) {
  useEffect(() => {
    if (!open) return
    const handler = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [open, onClose])

  if (!open) return null
  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-zinc-950/50 p-4 backdrop-blur-xs sm:items-center dark:bg-black/70"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        className={cn(
          'my-auto w-full rounded-lg border border-zinc-200 bg-white shadow-2xl dark:border-zinc-800 dark:bg-zinc-900',
          largura ?? 'max-w-lg',
        )}
      >
        <div className="flex items-center justify-between border-b border-zinc-200 px-5 py-3 dark:border-zinc-800">
          <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">{titulo}</h2>
          <button
            onClick={onClose}
            className="rounded-md p-1 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
            aria-label="Fechar"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="px-5 py-4">{children}</div>
        {rodape && (
          <div className="flex items-center justify-end gap-2 border-t border-zinc-200 px-5 py-3 dark:border-zinc-800">
            {rodape}
          </div>
        )}
      </div>
    </div>,
    document.body,
  )
}

// ---------- Diálogo de confirmação ----------

export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  titulo,
  mensagem,
  confirmarLabel = 'Confirmar',
  perigoso,
  loading,
}: {
  open: boolean
  onClose: () => void
  onConfirm: () => void
  titulo: string
  mensagem: string
  confirmarLabel?: string
  perigoso?: boolean
  loading?: boolean
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      titulo={titulo}
      rodape={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant={perigoso ? 'danger' : 'primary'} loading={loading} onClick={onConfirm}>
            {confirmarLabel}
          </Button>
        </>
      }
    >
      <p className="text-sm text-zinc-600 dark:text-zinc-300">{mensagem}</p>
    </Modal>
  )
}

// ---------- Spinner ----------

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={cn('h-5 w-5 animate-spin text-zinc-400', className)} />
}

export function PageLoading() {
  return (
    <div className="flex items-center justify-center gap-3 py-24 text-sm text-zinc-500">
      <Spinner /> Carregando…
    </div>
  )
}

// ---------- Vazio / Erro ----------

export function EmptyState({ titulo, descricao, acao }: { titulo: string; descricao?: string; acao?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-16 text-center">
      <p className="text-sm font-medium text-zinc-700 dark:text-zinc-300">{titulo}</p>
      {descricao && <p className="max-w-md text-xs text-zinc-500">{descricao}</p>}
      {acao && <div className="mt-3">{acao}</div>}
    </div>
  )
}

// ---------- Header de página ----------

export function PageHeader({
  titulo,
  descricao,
  acoes,
}: {
  titulo: string
  descricao?: string
  acoes?: ReactNode
}) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">{titulo}</h1>
        {descricao && <p className="mt-0.5 text-xs text-zinc-500">{descricao}</p>}
      </div>
      {acoes && <div className="flex items-center gap-2">{acoes}</div>}
    </div>
  )
}
