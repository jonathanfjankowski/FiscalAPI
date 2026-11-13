import { Loader2 } from 'lucide-react'
import { Slot } from '@radix-ui/react-slot'
import {
  forwardRef,
  type ButtonHTMLAttributes,
  type ReactNode,
} from 'react'
import { cn } from '../../lib/utils'

// ==========================================================================
// Botão
//
// A variante `primary` usa o acento único (carimbo). `danger` só existe
// para ação destrutiva irreversível (cancelar, desativar, revogar).
// ==========================================================================

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost'

const BUTTON_VARIANTS: Record<ButtonVariant, string> = {
  primary:
    'bg-carimbo text-on-carimbo hover:bg-carimbo-hover disabled:bg-carimbo/40',
  secondary:
    'border border-line-strong bg-surface text-ink hover:bg-surface-sunken hover:border-ink-faint',
  danger: 'bg-rejeitado text-white hover:bg-rejeitado/90',
  ghost: 'text-ink-muted hover:bg-surface-sunken hover:text-ink',
}

const BUTTON_SIZES = {
  sm: 'h-7 px-2.5 text-xs gap-1.5',
  md: 'h-8 px-3 text-[13px] gap-2',
} as const

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: keyof typeof BUTTON_SIZES
  loading?: boolean
  /** Renderiza como <a> mantendo o mesmo visual (para links que agem). */
  asChild?: boolean
  icon?: ReactNode
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'secondary', size = 'md', loading, className, children, disabled, asChild, icon, ...rest },
  ref,
) {
  const classes = cn(
    'inline-flex shrink-0 items-center justify-center rounded font-medium transition-colors',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-carimbo',
    'disabled:pointer-events-none disabled:opacity-50',
    BUTTON_SIZES[size],
    BUTTON_VARIANTS[variant],
    className,
  )

  // Com asChild o Slot exige UM único filho: não injeta spinner/ícone, que
  // virariam um segundo nó e quebrariam o Slot ("Expected a single React
  // element child").
  if (asChild) {
    return (
      <Slot ref={ref} className={classes} {...rest}>
        {children}
      </Slot>
    )
  }

  return (
    <button
      ref={ref}
      className={classes}
      disabled={disabled || loading}
      {...rest}
    >
      {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden /> : icon}
      {children}
    </button>
  )
})
