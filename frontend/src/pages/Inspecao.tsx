// Página temporária de inspeção do kit de componentes.
// Existe só durante a Fase A (Task 7) para conferir o visual no browser;
// some na Task 14, quando o shell real entra.
import { useState } from 'react'
import {
  AmbienteBadge,
  Badge,
  Button,
  ConfirmDialog,
  Dialog,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  EmptyState,
  ErrorState,
  Field,
  Input,
  PageHeader,
  Select,
  StatusBadge,
  StatusDot,
  Switch,
  Table,
  TableBody,
  Tabs,
  Td,
  Textarea,
  Th,
  Tooltip,
  TooltipProvider,
  WarningState,
} from '../components/ui'

export default function Inspecao() {
  const [dialogo, setDialogo] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [aba, setAba] = useState('a')
  const [ligado, setLigado] = useState(true)

  return (
    <TooltipProvider>
      <div className="mx-auto max-w-4xl p-8">
        <PageHeader
          titulo="Inspeção do kit"
          descricao="Página temporária da Fase A — some na Task 14."
          acoes={<Button variant="primary">Ação principal</Button>}
        />

        <section className="mb-8">
          <h2 className="mb-2 text-xs font-semibold text-ink-muted">Botões</h2>
          <div className="flex flex-wrap gap-2">
            <Button variant="primary">Primário</Button>
            <Button>Secundário</Button>
            <Button variant="danger">Destrutivo</Button>
            <Button variant="ghost">Fantasma</Button>
            <Button loading>Carregando</Button>
            <Button disabled>Desabilitado</Button>
            <Tooltip content="Dica com o motivo">
              <Button variant="ghost">Com dica</Button>
            </Tooltip>
          </div>
        </section>

        <section className="mb-8">
          <h2 className="mb-2 text-xs font-semibold text-ink-muted">Etiquetas</h2>
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status="AUTORIZADA" />
            <StatusBadge status="REJEITADA" />
            <StatusBadge status="FALHA_EMISSAO" />
            <StatusBadge status="CONTINGENCIA" />
            <StatusBadge status="PENDENTE" />
            <StatusDot status="AUTORIZADA" />
            <StatusDot status="REJEITADA" withLabel />
            <AmbienteBadge ambiente="producao" />
            <AmbienteBadge ambiente="homologacao" />
            <Badge>Neutra</Badge>
          </div>
        </section>

        <section className="mb-8">
          <h2 className="mb-2 text-xs font-semibold text-ink-muted">Campos</h2>
          <div className="grid grid-cols-3 gap-3">
            <Field label="Texto" htmlFor="t1">
              <Input id="t1" placeholder="placeholder" />
            </Field>
            <Field label="Com erro" htmlFor="t2" error="Campo obrigatório">
              <Input id="t2" invalid defaultValue="x" />
            </Field>
            <Field label="Seleção" htmlFor="t3">
              <Select id="t3" defaultValue="a">
                <option value="a">Opção A</option>
                <option value="b">Opção B</option>
              </Select>
            </Field>
          </div>
          <div className="mt-3">
            <Field label="Área de texto (JSON)" htmlFor="t4" hint="Dica de uso">
              <Textarea id="t4" mono rows={3} defaultValue='{"a": 1}' />
            </Field>
          </div>
        </section>

        <section className="mb-8">
          <h2 className="mb-2 text-xs font-semibold text-ink-muted">Abas e interruptor</h2>
          <Tabs
            abas={[
              { id: 'a', label: 'Dados fiscais' },
              { id: 'b', label: 'API Keys', contagem: 3 },
              { id: 'c', label: 'Webhooks' },
            ]}
            ativa={aba}
            onChange={setAba}
          />
          <div className="mt-4 flex items-center gap-3">
            <Switch checked={ligado} onChange={setLigado} label="Modo sandbox" />
            <span className="text-[13px] text-ink">Sandbox {ligado ? 'ligado' : 'desligado'}</span>
          </div>
        </section>

        <section className="mb-8">
          <h2 className="mb-2 text-xs font-semibold text-ink-muted">Tabela</h2>
          <Table label="Documentos">
            <thead>
              <tr>
                <Th>Número</Th>
                <Th>Empresa</Th>
                <Th>Status</Th>
                <Th>Ambiente</Th>
                <Th>Valor</Th>
              </tr>
            </thead>
            <TableBody>
              {[
                { n: '1/000000012', emp: 'Acme LTDA', st: 'AUTORIZADA', amb: 'homologacao', v: '1.234,50' },
                { n: '1/000000013', emp: 'Distribuidora X', st: 'REJEITADA', amb: 'producao', v: '890,00' },
                { n: '1/000000014', emp: 'Acme LTDA', st: 'CONTINGENCIA', amb: 'homologacao', v: '2.500,00' },
              ].map((l) => (
                <tr key={l.n}>
                  <Td className="font-mono text-xs">{l.n}</Td>
                  <Td>{l.emp}</Td>
                  <Td>
                    <StatusBadge status={l.st} />
                  </Td>
                  <Td>
                    <AmbienteBadge ambiente={l.amb} />
                  </Td>
                  <Td className="tabular">{l.v}</Td>
                </tr>
              ))}
            </TableBody>
          </Table>
        </section>

        <section className="mb-8 grid grid-cols-2 gap-4">
          <EmptyState
            titulo="Nenhuma empresa cadastrada"
            descricao="Crie a primeira empresa para poder gerar API keys e emitir documentos."
            acao={<Button variant="primary">Nova empresa</Button>}
          />
          <ErrorState
            titulo="Não foi possível carregar os documentos"
            detalhe="Não foi possível conectar à API. Verifique se a API está rodando."
            aoTentarNovamente={() => {}}
          />
          <WarningState titulo="Documento em contingência" className="col-span-2">
            Nova tentativa em 14:30 (tentativa 3). O Worker reenfileira a cada 30s.
          </WarningState>
        </section>

        <section className="mb-8 flex gap-2">
          <Button onClick={() => setDialogo(true)}>Abrir diálogo</Button>
          <Button variant="danger" onClick={() => setConfirm(true)}>
            Abrir confirmação
          </Button>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button>Menu de ações</Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent>
              <DropdownMenuItem>Editar</DropdownMenuItem>
              <DropdownMenuItem>Duplicar</DropdownMenuItem>
              <DropdownMenuItem destructive>Desativar</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </section>

        <Dialog
          open={dialogo}
          onClose={() => setDialogo(false)}
          titulo="Enviar certificado A1"
          descricao="O certificado é armazenado cifrado com AES-GCM."
          rodape={
            <>
              <Button onClick={() => setDialogo(false)}>Cancelar</Button>
              <Button variant="primary" onClick={() => setDialogo(false)}>
                Enviar
              </Button>
            </>
          }
        >
          <p className="text-[13px] text-ink-muted">Conteúdo do diálogo.</p>
        </Dialog>

        <ConfirmDialog
          open={confirm}
          onClose={() => setConfirm(false)}
          onConfirm={() => setConfirm(false)}
          titulo="Revogar API key"
          mensagem="A revogação é imediata: integrações usando esta chave passam a receber 401."
          confirmarLabel="Revogar definitivamente"
          perigoso
        />
      </div>
    </TooltipProvider>
  )
}
