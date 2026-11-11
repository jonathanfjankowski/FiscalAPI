// ==========================================================================
// Contratos da API — tipos das respostas do backend.
//
// Ficam separados do transporte (lib/http.ts) e dos endpoints
// (lib/endpoints/*) para que uma mudança de rota não arraste o arquivo de
// tipos, e vice-versa.
//
// Nenhum tipo aqui é calculado no painel: a API não calcula tributos e o
// painel não inventa valores — exibe o que a API devolve.
// ==========================================================================

// ---------- Autenticação ----------

export interface AuthInfo {
  token: string
  expiraEm: string
  email: string
}

// ---------- Empresas (tenants) ----------

export interface Tenant {
  id: string
  cnpj: string
  razaoSocial: string
  uf: string
  codigoMunicipioIbge?: string | null
  regimeTributario: number
  ambientePadrao: 'producao' | 'homologacao'
  inscricaoEstadual?: string | null
  logradouro?: string | null
  numero?: string | null
  complemento?: string | null
  bairro?: string | null
  cep?: string | null
  nomeMunicipio?: string | null
  webhookUrl?: string | null
  sandbox: boolean
  /** IdCSC da NFC-e. Vem do backend desde a Task 8 (bug: não vinha). */
  cscId?: string | null
  ativo: boolean
  criadoEm: string
  apiKeysAtivas: number
  certificadosAtivos: number
}

export interface TenantInput {
  cnpj: string
  razaoSocial: string
  uf: string
  codigoMunicipioIbge?: string
  regimeTributario?: number
  ambientePadrao?: string
  inscricaoEstadual?: string
  logradouro?: string
  numero?: string
  complemento?: string
  bairro?: string
  cep?: string
  nomeMunicipio?: string
  webhookUrl?: string
  webhookSecret?: string
  ativo?: boolean
  sandbox?: boolean
  cscId?: string
  csc?: string
  /** Cria a primeira API key na mesma transação (só na criação). */
  criarApiKey?: 'producao' | 'homologacao'
}

/** Resposta da criação de empresa com `criarApiKey` — chave aparece 1x. */
export interface TenantCriadoComApiKey {
  tenant: Tenant
  apiKey: {
    id: string
    chave: string
    prefixo: string
    ambiente: string
    aviso: string
  }
}

// ---------- API keys ----------

export interface ApiKey {
  id: string
  prefixo: string
  descricao?: string | null
  ambiente: string
  ativa: boolean
  criadoEm: string
}

export interface ApiKeyCriada extends ApiKey {
  chave: string
  aviso: string
}

// ---------- Certificados ----------

export interface Certificado {
  id: string
  thumbprint: string
  validoAte: string
  ativo: boolean
  criadoEm: string
}

// ---------- Documentos fiscais ----------

export interface DocListItem {
  id: string
  tenantId: string
  tenantRazaoSocial: string
  tipo: string
  status: string
  ambiente: string
  modelo: number | null
  serie: number | null
  numero: number | null
  chaveAcesso: string | null
  motivoStatus: string | null
  criadoEm: string
  atualizadoEm: string
}

export interface DocListResp {
  total: number
  page: number
  pageSize: number
  itens: DocListItem[]
}

export interface DocDetail extends DocListItem {
  protocoloAutorizacao: string | null
  reciboLote: string | null
  xmlAssinado: string | null
  xmlRetornoSefaz: string | null
  tentativas: number
  modoContingencia: string | null
  proximaTentativaEm: string | null
  /** Payload original de emissão — base do reenvio com correção. */
  payloadEntrada?: string | null
}

export interface EventoResp {
  documentoId: string
  eventoId: string
  tipo: string
  status: string
  criadoEm: string
}

/** Evento fiscal de um documento (cancelamento, CC-e, inutilização). */
export interface DocumentoEvento {
  id: string
  tipo: string
  status: string
  motivo: string | null
  protocolo: string | null
  tentativas: number
  proximaTentativaEm: string | null
  criadoEm: string
  /** O XML protocolado já está disponível para download. */
  xmlDisponivel: boolean
}

/** Resposta do reenvio de documento com payload corrigido. */
export interface ReenvioResp {
  id: string
  status: string
  numero: number | null
  serie: number | null
  atualizadoEm: string
  links?: { consulta?: string }
}

/** Requisição de inutilização de faixa de numeração. */
export interface InutilizacaoInput {
  tenantId: string
  modelo: 55 | 65
  serie: number
  numeroInicial: number
  numeroFinal: number
  justificativa: string
  ambiente: string
}

/** Estado de uma inutilização. */
export interface InutilizacaoResp {
  eventoId: string
  tipo: string
  status: string
  criadoEm: string
  modelo?: number
  serie?: number
  numeroInicial?: number
  numeroFinal?: number
}

// ---------- Notas recebidas (distribuição DFe) ----------

export interface NotaRecebida {
  id: string
  tenantId: string
  tenantRazaoSocial: string
  chave: string
  nsu: number
  ambiente: string
  cnpjEmitente: string
  nomeEmitente: string
  valor: number
  emitidaEm: string
  manifestacaoAtual: string | null
  tipoSchema: number | null
  recebidaEm: string
}

export interface NotasRecebidasResp {
  page: number
  pageSize: number
  total: number
  itens: NotaRecebida[]
}

/** Resposta da manifestação do destinatário. */
export interface ManifestacaoResp {
  manifestacaoId: string
  tipo: string
  status: string
  criadoEm: string
}

// ---------- Dashboard ----------

export interface Dashboard {
  documentosHoje: number
  documentos7Dias: number
  emContingencia: number
  porStatus: { status: string; total: number }[]
  tenantsAtivos: number
  apiKeysAtivas: number
  certificadosVencendo30Dias: number
  /** Documentos em trânsito — fila de emissão. */
  fila?: DocListItem[]
  /** Certificados ativos que vencem em 30 dias (com a empresa). */
  certificadosVencendo?: AlertaCertificado[]
  /** Entregas de webhook em falha (com a empresa). */
  webhooksEmFalha?: AlertaWebhook[]
  /** Rejeições recentes, com o motivo da SEFAZ. */
  rejeicoesRecentes?: AlertaRejeicao[]
}

/** Item de alerta do dashboard — sempre com o link para agir. */
export interface AlertaCertificado {
  id: string
  tenantId: string
  tenantRazaoSocial: string
  thumbprint: string
  validoAte: string
  diasRestantes: number
}

export interface AlertaWebhook {
  id: string
  tenantId: string
  tenantRazaoSocial: string
  tipoEvento: string
  tentativas: number
  ultimoStatusCode: number | null
  ultimoErro: string | null
  criadoEm: string
}

export interface AlertaRejeicao {
  id: string
  tenantId: string
  tenantRazaoSocial: string
  serie: number | null
  numero: number | null
  motivo: string | null
  criadoEm: string
}

// ---------- Webhooks (outbox) ----------

export interface WebhookEntrega {
  id: string
  tipoEvento: string
  status: 'PENDENTE' | 'ENTREGANDO' | 'ENTREGUE' | 'FALHA'
  documentoId: string | null
  tentativas: number
  ultimoStatusCode: number | null
  ultimoErro: string | null
  proximaTentativaEm: string | null
  entregueEm: string | null
  criadoEm: string
}

export interface WebhookEntregasResp {
  page: number
  pageSize: number
  itens: WebhookEntrega[]
}

// ---------- Auditoria ----------

export interface AuditoriaItem {
  id: string
  tenantId: string | null
  tenantRazaoSocial?: string | null
  apiKeyId: string | null
  acao: string
  recursoId: string | null
  ipOrigem: string | null
  detalhe: string | null
  criadoEm: string
}

export interface AuditoriaResp {
  total: number
  page: number
  pageSize: number
  itens: AuditoriaItem[]
}

// ---------- Status do serviço SEFAZ ----------

export interface StatusServicoResp {
  cStat: number
  xMotivo: string
  tMed: number
  consultadoEm: string
  doCache: boolean
}

// ---------- Chave master (bootstrap) ----------

export interface BootstrapKeyInfo {
  prefixo?: string
  criadoEm?: string
  configurada?: boolean
}

export interface BootstrapKeyRotacionada {
  id: string
  chave: string
  prefixo: string
  aviso: string
}

// ---------- Emissão (Playground) ----------

export interface EmissaoResponse {
  id: string
  tipo: string
  status: string
  ambiente: string
  serie: number | null
  numero: number | null
  chaveAcesso: string | null
  protocoloAutorizacao: string | null
  xmlAssinado: string | null
  xmlRetornoSefaz: string | null
  motivoStatus: string | null
  criadoEm: string
  atualizadoEm: string
}
