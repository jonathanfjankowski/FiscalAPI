// ==========================================================================
// Núcleo fiscal do painel — status de documento e anatomia da chave.
//
// Fica em `lib/` (e não em `components/`) porque é lógica pura e testável:
// nada aqui depende de React, DOM ou da API.
//
// A chave de acesso de 44 dígitos é a identidade do documento e a SEFAZ a
// define em 9 campos de tamanho fixo. Decompor a chave mostra ao operador
// o que ele precisaria contar nos dígitos à mão (série, CNPJ, DV).
// ==========================================================================

export interface ChaveAcesso {
  /** Código da UF (2 dígitos) — 35 = São Paulo. */
  uf: string
  /** Ano e mês de emissão, AAMM (4 dígitos) — 2408 = agosto de 2024. */
  aamm: string
  /** CNPJ do emitente (14 dígitos). */
  cnpj: string
  /** Modelo do documento (2 dígitos) — 55 = NF-e, 65 = NFC-e. */
  modelo: string
  /** Série (3 dígitos). */
  serie: string
  /** Número do documento (9 dígitos). */
  numero: string
  /** Forma de emissão (1 dígito) — 1 = normal. */
  tipoEmissao: string
  /** Código numérico do documento (8 dígitos). */
  codigoNumerico: string
  /** Dígito verificador (1 dígito). */
  dv: string
}

/** Tamanho fixo de cada campo da chave, na ordem oficial. */
const CAMPOS_CHAVE: ReadonlyArray<{ chave: keyof ChaveAcesso; tamanho: number }> = [
  { chave: 'uf', tamanho: 2 },
  { chave: 'aamm', tamanho: 4 },
  { chave: 'cnpj', tamanho: 14 },
  { chave: 'modelo', tamanho: 2 },
  { chave: 'serie', tamanho: 3 },
  { chave: 'numero', tamanho: 9 },
  { chave: 'tipoEmissao', tamanho: 1 },
  { chave: 'codigoNumerico', tamanho: 8 },
  { chave: 'dv', tamanho: 1 },
]

/** 44 dígitos: 2+4+14+2+3+9+1+8+1. */
const TAMANHO_CHAVE = CAMPOS_CHAVE.reduce((t, c) => t + c.tamanho, 0)

/**
 * Quebra a chave de acesso nos 9 campos da SEFAZ.
 * Devolve `null` quando a chave não tem 44 dígitos (ou tem letra) — a UI
 * cai então na representation crua, em vez de mostrar campos inventados.
 */
export function decomporChave(chave: string | null | undefined): ChaveAcesso | null {
  if (!chave) return null
  const digitos = chave.replace(/\s+/g, '')
  if (digitos.length !== TAMANHO_CHAVE) return null
  if (!/^\d+$/.test(digitos)) return null

  const partes: Partial<ChaveAcesso> = {}
  let posicao = 0
  for (const campo of CAMPOS_CHAVE) {
    partes[campo.chave] = digitos.slice(posicao, posicao + campo.tamanho)
    posicao += campo.tamanho
  }
  return partes as ChaveAcesso
}

// ==========================================================================
// Status do documento
//
// Fonte: StatusDocumento (src/Fiscal.Core/Enums/StatusDocumento.cs) — 11
// valores. A versão anterior do painel tinha 10: FALHA_EMISSAO (documento
// que esgotou as tentativas de contingência) aparecia cru na tela e, por
// não constar em STATUS_TERMINAIS, fazia o Playground pollar por 90s um
// documento que já estava morto.
// ==========================================================================

export const STATUS_LABEL: Record<string, string> = {
  PENDENTE: 'Pendente',
  PROCESSANDO: 'Processando',
  AUTORIZADA: 'Autorizada',
  REJEITADA: 'Rejeitada',
  CONTINGENCIA: 'Contingência',
  CANCELAMENTO_PENDENTE: 'Cancelamento pendente',
  CANCELADA: 'Cancelada',
  ERRO_CANCELAMENTO: 'Erro no cancelamento',
  DENEGADA: 'Denegada',
  ERRO_INTERNO: 'Erro interno',
  FALHA_EMISSAO: 'Falha na emissão',
}

/**
 * Classes de cor por status. Três famílias semânticas (aprovado, rejeitado,
 * contingência) mais o neutro — em vez das dez famílias de matiz que a
 * versão anterior usava e que disputavam a mesma atenção.
 *
 * As cores vêm de custom properties (ver styles/tokens.css), então o
 * tema claro/escuro troca sem repetir o par de classes aqui.
 */
export const STATUS_COLOR: Record<string, string> = {
  PENDENTE: 'text-carimbo bg-carimbo-soft border-carimbo/25',
  PROCESSANDO: 'text-carimbo bg-carimbo-soft border-carimbo/25',
  AUTORIZADA: 'text-aprovado bg-aprovado-soft border-aprovado/25',
  REJEITADA: 'text-rejeitado bg-rejeitado-soft border-rejeitado/25',
  CONTINGENCIA: 'text-contingencia bg-contingencia-soft border-contingencia/25',
  CANCELAMENTO_PENDENTE: 'text-contingencia bg-contingencia-soft border-contingencia/25',
  CANCELADA: 'text-neutro bg-neutro-soft border-neutro/25',
  ERRO_CANCELAMENTO: 'text-rejeitado bg-rejeitado-soft border-rejeitado/25',
  DENEGADA: 'text-rejeitado bg-rejeitado-soft border-rejeitado/25',
  ERRO_INTERNO: 'text-rejeitado bg-rejeitado-soft border-rejeitado/25',
  FALHA_EMISSAO: 'text-rejeitado bg-rejeitado-soft border-rejeitado/25',
}

/** Terminais: o documento não muda mais de estado sozinho. */
export const STATUS_TERMINAIS: readonly string[] = [
  'AUTORIZADA',
  'REJEITADA',
  'DENEGADA',
  'CANCELADA',
  'ERRO_CANCELAMENTO',
  'ERRO_INTERNO',
  'FALHA_EMISSAO',
]

/** Transitórios: ainda há mudança de estado possível (auto-refresh/poll). */
export const STATUS_TRANSITORIOS: readonly string[] = [
  'PENDENTE',
  'PROCESSANDO',
  'CONTINGENCIA',
  'CANCELAMENTO_PENDENTE',
]

// ==========================================================================
// Trilho de status — o elemento-assinatura do painel.
//
// Quatro estações: criado → fila → transmitido → destino. O destino
// carrega a cor do desfecho (autorizado, rejeitado, contingência, ...).
// ==========================================================================

export type CorTrilho = 'neutro' | 'carimbo' | 'aprovado' | 'rejeitado' | 'contingencia'

export interface EstagioTrilho {
  /** 0 = criado, 1 = fila, 2 = transmitido, 3 = destino. */
  posicao: number
  /** Família semântica do desfecho. */
  cor: CorTrilho
  /** Rótulo por extenso — vai para o aria-label do trilho. */
  rotulo: string
}

const ESTAGIO_PADRAO: EstagioTrilho = {
  posicao: 3,
  cor: 'neutro',
  rotulo: 'Estado desconhecido',
}

const ESTAGIOS: Record<string, EstagioTrilho> = {
  PENDENTE: { posicao: 1, cor: 'carimbo', rotulo: 'Na fila de emissão' },
  PROCESSANDO: { posicao: 2, cor: 'carimbo', rotulo: 'Transmitindo para a SEFAZ' },
  AUTORIZADA: { posicao: 3, cor: 'aprovado', rotulo: 'Autorizada pela SEFAZ' },
  REJEITADA: { posicao: 3, cor: 'rejeitado', rotulo: 'Rejeitada pela SEFAZ' },
  DENEGADA: { posicao: 3, cor: 'rejeitado', rotulo: 'Denegada pela SEFAZ' },
  CONTINGENCIA: { posicao: 3, cor: 'contingencia', rotulo: 'Em contingência' },
  CANCELAMENTO_PENDENTE: { posicao: 3, cor: 'contingencia', rotulo: 'Cancelamento em andamento' },
  CANCELADA: { posicao: 3, cor: 'neutro', rotulo: 'Cancelada' },
  ERRO_CANCELAMENTO: { posicao: 3, cor: 'rejeitado', rotulo: 'Erro no cancelamento' },
  ERRO_INTERNO: { posicao: 3, cor: 'rejeitado', rotulo: 'Erro interno' },
  FALHA_EMISSAO: { posicao: 3, cor: 'rejeitado', rotulo: 'Falha na emissão' },
}

/** Estágio do trilho para um status. Status desconhecido vai ao destino, neutro. */
export function estagioTrilho(status: string): EstagioTrilho {
  return ESTAGIOS[status] ?? ESTAGIO_PADRAO
}
