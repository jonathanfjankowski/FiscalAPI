import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { KeyRound, TriangleAlert } from 'lucide-react'
import { api, type BootstrapKeyRotacionada } from '../lib/api'
import { Badge, Button, Card, ConfirmDialog, Modal, PageHeader, PageLoading } from '../components/ui'
import { useToast } from '../components/toast'
import { copiar, formatarData } from '../lib/utils'

export default function Configuracoes() {
  const qc = useQueryClient()
  const toast = useToast()
  const [confirmar, setConfirmar] = useState(false)
  const [gerada, setGerada] = useState<BootstrapKeyRotacionada | null>(null)

  const { data: chave, isLoading } = useQuery({ queryKey: ['bootstrap-key'], queryFn: api.getBootstrapKey })

  const rotacionarMut = useMutation({
    mutationFn: api.rotacionarBootstrapKey,
    onSuccess: (k) => {
      qc.invalidateQueries({ queryKey: ['bootstrap-key'] })
      setConfirmar(false)
      setGerada(k)
    },
    onError: (e) => {
      setConfirmar(false)
      toast({ tipo: 'erro', titulo: 'Falha ao rotacionar', detalhe: e.message })
    },
  })

  return (
    <>
      <PageHeader
        titulo="Configurações"
        descricao="Chave master de provisionamento e preferências do painel"
      />

      <div className="max-w-2xl space-y-4">
        <Card>
          <div className="flex items-center justify-between border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
            <div className="flex items-center gap-2">
              <KeyRound className="h-4 w-4 text-zinc-500" />
              <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-200">
                Chave master (provisionamento de empresas)
              </h2>
            </div>
            {chave && (
              <Badge
                className={
                  chave.configurada === false
                    ? 'border-red-200 bg-red-50 text-red-700 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-300'
                    : 'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300'
                }
              >
                {chave.configurada === false ? 'Não configurada' : 'Configurada'}
              </Badge>
            )}
          </div>

          {isLoading ? (
            <PageLoading />
          ) : (
            <div className="px-4 py-4">
              <p className="text-xs leading-relaxed text-zinc-500">
                Segredo compartilhado que o ERP Okto envia no header <code className="font-mono">X-Bootstrap-Key</code>{' '}
                ao chamar <code className="font-mono">POST /v1/empresas</code> para criar/atualizar empresas
                automaticamente (idempotente por CNPJ, com rotação de API keys e token de webhook).
              </p>
              <div className="mt-3 grid grid-cols-2 gap-3">
                <div className="rounded-md border border-zinc-200 px-3 py-2.5 dark:border-zinc-800">
                  <p className="text-[11px] font-medium text-zinc-500">Prefixo</p>
                  <p className="mt-0.5 font-mono text-sm text-zinc-900 dark:text-zinc-100">
                    {chave?.configurada === false || !chave?.prefixo ? '—' : `${chave.prefixo}…`}
                  </p>
                </div>
                <div className="rounded-md border border-zinc-200 px-3 py-2.5 dark:border-zinc-800">
                  <p className="text-[11px] font-medium text-zinc-500">Criada em</p>
                  <p className="mt-0.5 text-sm text-zinc-900 dark:text-zinc-100">{formatarData(chave?.criadoEm)}</p>
                </div>
              </div>
              <p className="mt-3 text-[11px] text-zinc-400 dark:text-zinc-500">
                A chave completa nunca é exibida depois de criada — apenas o prefixo. Ao rotacionar, a chave
                anterior deixa de valer imediatamente.
              </p>
              <div className="mt-4 flex justify-end">
                <Button variant="primary" onClick={() => setConfirmar(true)}>
                  Rotacionar chave
                </Button>
              </div>
            </div>
          )}
        </Card>
      </div>

      <ConfirmDialog
        open={confirmar}
        onClose={() => setConfirmar(false)}
        onConfirm={() => rotacionarMut.mutate()}
        titulo="Rotacionar chave master"
        mensagem="Uma nova chave é gerada e a atual deixa de valer imediatamente — o ERP precisa ser atualizado na hora, senão o provisionamento automático para."
        confirmarLabel="Rotacionar"
        perigoso
        loading={rotacionarMut.isPending}
      />

      <ModalChaveGerada chave={gerada} onClose={() => setGerada(null)} />
    </>
  )
}

function ModalChaveGerada({ chave, onClose }: { chave: BootstrapKeyRotacionada | null; onClose: () => void }) {
  const toast = useToast()
  if (!chave) return null
  return (
    <Modal
      open
      onClose={onClose}
      titulo="Nova chave master"
      rodape={
        <>
          <Button onClick={onClose}>Fechar</Button>
          <Button
            variant="primary"
            onClick={async () => {
              if (await copiar(chave.chave)) toast({ tipo: 'sucesso', titulo: 'Chave copiada' })
            }}
          >
            Copiar chave
          </Button>
        </>
      }
    >
      <p className="flex items-start gap-2 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
        <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        {chave.aviso}
      </p>
      <code className="mt-3 block rounded-md border border-zinc-200 bg-zinc-50 px-3 py-2.5 font-mono text-xs break-all text-blue-700 select-all dark:border-zinc-800 dark:bg-zinc-950 dark:text-blue-300">
        {chave.chave}
      </code>
    </Modal>
  )
}
