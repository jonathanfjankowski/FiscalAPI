import { describe, expect, it } from 'vitest'
import { comoCurl, prepararCorpo } from './trafego'

// ==========================================================================
// O registro de tráfego guarda corpos de request e response. Se um segredo
// entrasse no buffer, ele apareceria na tela, no "Copiar cURL" e no JSON
// exportado — três caminhos de vazamento. Estes testes travam a redação.
// ==========================================================================

describe('prepararCorpo — redação de segredos', () => {
  it('redige a senha no corpo de login', () => {
    const { texto } = prepararCorpo(JSON.stringify({ email: 'a@b.c', senha: 'super-secreta' }))
    expect(texto).toContain('[redigido]')
    expect(texto).not.toContain('super-secreta')
  })

  it('redige o token JWT no response do login', () => {
    const { texto } = prepararCorpo(JSON.stringify({ token: 'eyJhbGciOiJIUzI1NiJ9.segredo', email: 'a@b.c' }))
    expect(texto).not.toContain('eyJ')
    expect(texto).toContain('[redigido]')
  })

  it('redige CSC, webhook secret e chave de API', () => {
    const { texto } = prepararCorpo(
      JSON.stringify({ csc: 'csc-da-sefaz', webhookSecret: 'hmac-secreto', chave: 'fk_live_abc123' }),
    )
    expect(texto).not.toContain('csc-da-sefaz')
    expect(texto).not.toContain('hmac-secreto')
    expect(texto).not.toContain('fk_live_abc123')
  })

  it('redige em profundidade — dentro de itens aninhados', () => {
    const { texto } = prepararCorpo(
      JSON.stringify({ empresa: { nome: 'Acme', csc: 'segredo-aninhado' } }),
    )
    expect(texto).not.toContain('segredo-aninhado')
    expect(texto).toContain('Acme')
  })

  it('redige dentro de arrays', () => {
    const { texto } = prepararCorpo(JSON.stringify([{ senha: 'a' }, { senha: 'b' }]))
    expect(texto).not.toContain('"a"')
    expect(texto).not.toContain('"b"')
  })

  it('é case-insensitive no nome do campo', () => {
    const { texto } = prepararCorpo(JSON.stringify({ Senha: 'x', CSC: 'y', WebhookSecret: 'z' }))
    expect(texto).not.toContain('"x"')
    expect(texto).not.toContain('"y"')
    expect(texto).not.toContain('"z"')
  })

  it('preserva o dado fiscal que não é segredo', () => {
    const { texto } = prepararCorpo(
      JSON.stringify({ numero: 42, chaveAcesso: '35240800000000000191550010000000011000000018', valorNota: 100 }),
    )
    expect(texto).toContain('35240800000000000191550010000000011000000018')
    expect(texto).toContain('42')
    expect(texto).toContain('100')
  })

  it('não quebra com JSON inválido — devolve o texto cru', () => {
    const { texto } = prepararCorpo('{isso nao e json')
    expect(texto).toBe('{isso nao e json')
  })

  it('devolve null para corpo vazio', () => {
    expect(prepararCorpo(null).texto).toBeNull()
    expect(prepararCorpo('').texto).toBeNull()
    expect(prepararCorpo(undefined).texto).toBeNull()
  })

  it('trunca corpos acima de 64 KB e marca o corte', () => {
    const grande = JSON.stringify({ xml: 'x'.repeat(80 * 1024) })
    const { texto, truncado, bytes } = prepararCorpo(grande)
    expect(truncado).toBe(true)
    expect(texto!.length).toBeLessThanOrEqual(64 * 1024)
    expect(bytes).toBeGreaterThan(64 * 1024)
  })

  it('não trata XML como JSON', () => {
    const xml = '<nfeProc><NFe><infNFe Id="NFe35"/></NFe></nfeProc>'
    const { texto } = prepararCorpo(xml)
    expect(texto).toBe(xml)
  })
})

describe('comoCurl', () => {
  const base = {
    id: 'r1',
    inicio: Date.now(),
    metodo: 'POST',
    rota: '/v1/documentos-fiscais/nfe',
    status: 202,
    ok: true,
    duracaoMs: 10,
    bytes: 0,
    origem: 'tenant' as const,
    requestHeaders: { Authorization: 'ApiKey fk_test_abc… [redigido]', 'Content-Type': 'application/json' },
    responseHeaders: {},
    requestBody: '{"a":1}',
    responseBody: null,
    erro: null,
    truncado: false,
  }

  it('inclui método, rota e headers', () => {
    const curl = comoCurl(base)
    expect(curl).toContain('curl -X POST')
    expect(curl).toContain('/v1/documentos-fiscais/nfe')
    expect(curl).toContain('-H "Content-Type: application/json"')
    expect(curl).toContain("-d '{\"a\":1}'")
  })

  it('escapa aspas simples do corpo para não quebrar o shell', () => {
    const curl = comoCurl({ ...base, requestBody: '{"nome":"O\'Brien"}' })
    // ' → '\'' (fecha, escapa, reabre)
    expect(curl).toContain(`'\\''`)
  })

  it('não deixa barra pendurada quando não há corpo', () => {
    const curl = comoCurl({ ...base, metodo: 'GET', requestBody: null })
    expect(curl.trimEnd().endsWith('\\')).toBe(false)
  })
})
