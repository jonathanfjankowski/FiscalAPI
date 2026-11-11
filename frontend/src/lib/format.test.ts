import { describe, expect, it } from 'vitest'
import { apenasDigitos, copiar, formatarCnpj, formatarData, formatarDataCurta, formatarDecimal } from './format'

describe('formatarCnpj', () => {
  it('formata os 14 dígitos no padrão brasileiro', () => {
    expect(formatarCnpj('12345678000199')).toBe('12.345.678/0001-99')
  })

  it('devolve o valor cru quando não tem 14 dígitos', () => {
    expect(formatarCnpj('123')).toBe('123')
  })

  it('devolve travessão para null/undefined', () => {
    expect(formatarCnpj(null)).toBe('—')
    expect(formatarCnpj(undefined)).toBe('—')
  })
})

describe('formatarData', () => {
  it('formata ISO em pt-BR com data e hora', () => {
    const out = formatarData('2026-09-24T13:45:00Z')
    expect(out).not.toBe('—')
    expect(out).toContain('/')
  })

  it('devolve travessão para null/undefined', () => {
    expect(formatarData(null)).toBe('—')
    expect(formatarData(undefined)).toBe('—')
  })

  it('devolve travessão para string inválida', () => {
    expect(formatarData('nao-e-data')).toBe('—')
  })
})

describe('formatarDataCurta', () => {
  it('formata apenas a data', () => {
    const out = formatarDataCurta('2026-09-24T13:45:00Z')
    expect(out).not.toBe('—')
    expect(out).not.toContain(':')
  })

  it('devolve travessão para null', () => {
    expect(formatarDataCurta(null)).toBe('—')
  })
})

describe('formatarDecimal', () => {
  it('formata com 2 casas por padrão', () => {
    expect(formatarDecimal(1234.5)).toBe('1.234,50')
  })

  it('aceita o número de casas', () => {
    expect(formatarDecimal(1234.567, 3)).toBe('1.234,567')
  })

  it('devolve travessão para null/undefined', () => {
    expect(formatarDecimal(null)).toBe('—')
    expect(formatarDecimal(undefined)).toBe('—')
  })

  it('formata zero sem cair no travessão', () => {
    expect(formatarDecimal(0)).toBe('0,00')
  })
})

describe('apenasDigitos', () => {
  it('remove tudo que não é dígito', () => {
    expect(apenasDigitos('12.345.678/0001-99')).toBe('12345678000199')
  })

  it('devolve string vazia quando não há dígitos', () => {
    expect(apenasDigitos('abc')).toBe('')
  })
})

describe('copiar', () => {
  it('devolve false quando a área de transferência não existe', async () => {
    // Sem navigator.clipboard (ambiente node) o catch precisa segurar.
    expect(await copiar('texto')).toBe(false)
  })
})
