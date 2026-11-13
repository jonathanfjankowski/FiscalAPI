import { useState } from 'react'
import { Check, Copy, TriangleAlert } from 'lucide-react'
import { copiar } from '../../lib/format'
import { cn } from '../../lib/utils'
import { Button } from './button'

// ==========================================================================
// Segredo exibido uma única vez.
//
// API key e chave master só aparecem na criação — depois, só o prefixo. O
// aviso precisa ser inequívoco e a cópia precisa dar feedback: o operador
// que fecha o modal sem copiar perde a chave e tem que gerar outra.
// ==========================================================================

export function SegredoUnico({
  valor,
  aviso,
  className,
}: {
  valor: string
  aviso?: string
  className?: string
}) {
  const [copiado, setCopiado] = useState(false)

  return (
    <div className={cn('space-y-3', className)}>
      <div className="flex items-start gap-2 rounded border border-contingencia/30 bg-contingencia-soft/50 px-3 py-2">
        <TriangleAlert className="mt-px h-3.5 w-3.5 shrink-0 text-contingencia" aria-hidden />
        <p className="text-xs leading-relaxed text-ink">
          {aviso ?? 'Copie agora — esta é a única vez que o valor completo é exibido.'}
        </p>
      </div>

      <div className="flex items-start gap-2">
        <code className="min-w-0 flex-1 rounded border border-line bg-surface-sunken px-3 py-2 font-mono text-xs break-all text-ink select-all">
          {valor}
        </code>
        <Button
          variant={copiado ? 'secondary' : 'primary'}
          onClick={async () => {
            if (await copiar(valor)) {
              setCopiado(true)
              setTimeout(() => setCopiado(false), 2500)
            }
          }}
          aria-label={copiado ? 'Valor copiado' : 'Copiar valor'}
        >
          {copiado ? (
            <>
              <Check className="h-3.5 w-3.5 text-aprovado" aria-hidden />
              Copiado
            </>
          ) : (
            <>
              <Copy className="h-3.5 w-3.5" aria-hidden />
              Copiar
            </>
          )}
        </Button>
      </div>
    </div>
  )
}
