import {
  forwardRef,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react'
import { cn } from '../../lib/utils'

// ==========================================================================
// Campos de formulário
//
// O `Field` liga label, dica e erro ao controle por id — `aria-describedby`
// é o que faz o leitor de tela anunciar a mensagem de erro junto do campo,
// não solta no fim da página.
// ==========================================================================

const CONTROL_BASE =
  'w-full rounded border bg-surface px-2.5 text-[13px] text-ink placeholder:text-ink-faint ' +
  'transition-colors focus:outline-2 focus:outline-offset-0 focus:outline-carimbo ' +
  'disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-ink-faint'

const CONTROL_TONE = (invalid?: boolean) =>
  invalid
    ? 'border-rejeitado focus:outline-rejeitado'
    : 'border-line-strong hover:border-ink-faint focus:border-carimbo'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { className, invalid, ...rest },
  ref,
) {
  return (
    <input
      ref={ref}
      aria-invalid={invalid || undefined}
      className={cn(CONTROL_BASE, CONTROL_TONE(invalid), 'h-8', className)}
      {...rest}
    />
  )
})

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  invalid?: boolean
  /** Fonte monoespaçada — para payload JSON. */
  mono?: boolean
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { className, invalid, mono, ...rest },
  ref,
) {
  return (
    <textarea
      ref={ref}
      aria-invalid={invalid || undefined}
      className={cn(
        CONTROL_BASE,
        CONTROL_TONE(invalid),
        'py-2 leading-relaxed',
        mono ? 'font-mono text-xs' : 'text-[13px]',
        className,
      )}
      {...rest}
    />
  )
})

export interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  invalid?: boolean
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { className, invalid, children, ...rest },
  ref,
) {
  // Chevron próprio: o `appearance-none` sem seta deixava o select com cara
  // de controle quebrado em alguns navegadores.
  return (
    <div className="relative">
      <select
        ref={ref}
        aria-invalid={invalid || undefined}
        className={cn(
          CONTROL_BASE,
          CONTROL_TONE(invalid),
          'h-8 cursor-pointer appearance-none pr-7',
          className,
        )}
        {...rest}
      >
        {children}
      </select>
      <svg
        aria-hidden
        viewBox="0 0 12 12"
        className="pointer-events-none absolute top-1/2 right-2 h-3 w-3 -translate-y-1/2 text-ink-faint"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
      >
        <path d="M3 4.5 6 7.5 9 4.5" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </div>
  )
})

export interface FieldProps {
  label: string
  hint?: string
  error?: string
  /** Id do controle — usado para ligar label, dica e erro. */
  htmlFor?: string
  required?: boolean
  className?: string
  children: ReactNode
}

/** Rótulo + dica + erro, ligados ao controle por id. */
export function Field({
  label,
  hint,
  error,
  htmlFor,
  required,
  className,
  children,
}: FieldProps) {
  const hintId = htmlFor ? `${htmlFor}-hint` : undefined
  const errorId = htmlFor ? `${htmlFor}-erro` : undefined

  return (
    <div className={cn('min-w-0', className)}>
      <label
        htmlFor={htmlFor}
        className="mb-1 block text-xs font-medium text-ink-muted"
      >
        {label}
        {required && <span className="ml-0.5 text-rejeitado">*</span>}
      </label>
      {children}
      {hint && !error && (
        <p id={hintId} className="mt-1 text-[11px] leading-snug text-ink-faint">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} role="alert" className="mt-1 text-[11px] leading-snug text-rejeitado">
          {error}
        </p>
      )}
    </div>
  )
}
