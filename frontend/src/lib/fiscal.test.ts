import { describe, expect, it } from 'vitest'
import {
  STATUS_COLOR,
  STATUS_LABEL,
  STATUS_TERMINAIS,
  STATUS_TRANSITORIOS,
  decomporChave,
  estagioTrilho,
} from './fiscal'

// Chave de acesso NF-e: 44 dígitos = cUF(2) AAMM(4) CNPJ(14) mod(2)
// serie(3) nNF(9) tpEmis(1) cNF(8) cDV(1). Montada campo a campo para o
// esperado não depender de contagem manual.
const CHAVE =
  '35' + '2408' + '00000000000191' + '55' + '001' + '000000001' + '1' + '00000001' + '2'

// Os 11 valores de StatusDocumento (src/Fiscal.Core/Enums/StatusDocumento.cs).
const TODOS_OS_STATUS = [
  'PENDENTE',
  'PROCESSANDO',
  'AUTORIZADA',
  'REJEITADA',
  'CONTINGENCIA',
  'CANCELAMENTO_PENDENTE',
  'CANCELADA',
  'ERRO_CANCELAMENTO',
  'DENEGADA',
  'ERRO_INTERNO',
  'FALHA_EMISSAO',
]

describe('decomporChave', () => {
  it('quebra a chave de 44 dígitos nos 9 campos da SEFAZ', () => {
    expect(decomporChave(CHAVE)).toEqual({
      uf: '35',
      aamm: '2408',
      cnpj: '00000000000191',
      modelo: '55',
      serie: '001',
      numero: '000000001',
      tipoEmissao: '1',
      codigoNumerico: '00000001',
      dv: '2',
    })
  })

  it('aceita chave com espaços separadores', () => {
    const comEspacos = '35 2408 00000000000191 55 001 000000001 1 00000001 2'
    expect(decomporChave(comEspacos)).toEqual(decomporChave(CHAVE))
  })

  it('devolve null para chave com menos de 44 dígitos', () => {
    expect(decomporChave('3524080000000000019155001000000001100000001')).toBeNull()
  })

  it('devolve null para chave com mais de 44 dígitos', () => {
    expect(decomporChave(CHAVE + '0')).toBeNull()
  })

  it('devolve null para string vazia', () => {
    expect(decomporChave('')).toBeNull()
  })

  it('devolve null para chave com letras', () => {
    expect(decomporChave('35' + 'x'.repeat(42))).toBeNull()
  })
})

describe('matriz de status', () => {
  it('rotula os 11 status do enum, incluindo FALHA_EMISSAO', () => {
    for (const status of TODOS_OS_STATUS) {
      expect(STATUS_LABEL[status], `rótulo ausente para ${status}`).toBeTruthy()
      expect(STATUS_LABEL[status]).not.toBe(status)
    }
  })

  it('FALHA_EMISSAO é terminal — não pode ficar em transitorios', () => {
    expect(STATUS_TERMINAIS).toContain('FALHA_EMISSAO')
    expect(STATUS_TRANSITORIOS).not.toContain('FALHA_EMISSAO')
  })

  it('separa terminais de transitorios sem sobreposição', () => {
    for (const status of TODOS_OS_STATUS) {
      const terminal = STATUS_TERMINAIS.includes(status)
      const transitorio = STATUS_TRANSITORIOS.includes(status)
      expect(terminal !== transitorio, `${status} está nos dois ou em nenhum`).toBe(true)
    }
  })

  it('cobre os 11 status em STATUS_COLOR', () => {
    for (const status of TODOS_OS_STATUS) {
      expect(STATUS_COLOR[status], `cor ausente para ${status}`).toBeTruthy()
    }
  })

  it('usa só cores do sistema — nenhum matiz fora dos tokens', () => {
    // Invariante real: toda cor de status sai do conjunto fechado de tokens
    // (3 famílias semânticas + neutro + o acento para estado em curso).
    // A versão anterior do painel usava 10 matizes distintos (emerald,
    // indigo, violet, sky, orange, fuchsia, zinc...) disputando atenção.
    const permitidas = new Set([
      'text-aprovado',
      'text-rejeitado',
      'text-contingencia',
      'text-neutro',
      'text-carimbo',
    ])
    for (const status of TODOS_OS_STATUS) {
      const cor = STATUS_COLOR[status].split(' ').find((c) => c.startsWith('text-'))
      expect(permitidas.has(cor!), `cor fora do sistema em ${status}: ${cor}`).toBe(true)
    }
  })
})

describe('estagioTrilho', () => {
  it('muda o estágio conforme o documento avança', () => {
    expect(estagioTrilho('PENDENTE').posicao).toBe(1)
    expect(estagioTrilho('PROCESSANDO').posicao).toBe(2)
    expect(estagioTrilho('AUTORIZADA').posicao).toBe(3)
  })

  it('leva os desfechos terminais ao destino com a cor do desfecho', () => {
    expect(estagioTrilho('AUTORIZADA').cor).toBe('aprovado')
    expect(estagioTrilho('REJEITADA').cor).toBe('rejeitado')
    expect(estagioTrilho('DENEGADA').cor).toBe('rejeitado')
    expect(estagioTrilho('CONTINGENCIA').cor).toBe('contingencia')
    expect(estagioTrilho('FALHA_EMISSAO').cor).toBe('rejeitado')
    expect(estagioTrilho('CANCELADA').cor).toBe('neutro')
  })

  it('cai no destino para status desconhecido, sem quebrar a UI', () => {
    const est = estagioTrilho('STATUS_INEXISTENTE')
    expect(est.posicao).toBe(3)
    expect(est.cor).toBe('neutro')
  })
})
