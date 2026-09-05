# Política de Segurança

## Reportando uma vulnerabilidade

**Não abra issue pública** com detalhes de falhas de segurança.

Envie e-mail para **jonathanflorianojankowski@gmail.com** com:

- Descrição do problema
- Passos para reproduzir
- Impacto potencial
- Versão afetada

Você receberá uma resposta em até 72h. Após confirmação, vamos coordenar disclosure e correção antes de qualquer divulgação pública.

## Versões suportadas

Apenas a versão mais recente recebe patches de segurança. Atualize sempre que possível.

## Práticas de segurança já implementadas

- Certificados A1 cifrados em repouso com envelope encryption (AES-GCM DEK/KEK).
- KEK mestra nunca persistida — vem de variável de ambiente / secret manager.
- API Key armazenada como hash PBKDF2-SHA256 (100k iterações, salt aleatório);
  fallback transparente para SHA-256 herdado de chaves antigas. Chave em
  texto puro retornada uma única vez na criação.
- Ambiente atrelado à API Key (homolog/prod são universos fisicamente separados).
- Tabela `auditoria` é append-only (sem `UPDATE`/`DELETE` pelo role da app —
  enforced via `REVOKE UPDATE, DELETE, TRUNCATE ON auditoria` no role do
  banco em produção; ver migration `20260902000000_EnforceAuditoriaAppendOnly`).
- Rate limiting global por IP (`Microsoft.AspNetCore.RateLimiting`).
- Em homologação real, o adapter Unimake (`EmissorNFe`/`EmissorNFCe`)
  recusa emitir (lança `NotImplementedException`); sem certificado A1 real
  a única opção é `ModoSandbox=true` (EmissorMock).
