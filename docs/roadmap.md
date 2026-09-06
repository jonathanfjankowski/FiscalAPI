# Roadmap público — FiscalAPI

Estado por sprint (as "fases" do planejamento original estão em `docs/`).
Tudo abaixo de "Feito" está implementado e coberto por testes automatizados;
"Próximo" é o que falta antes do 1.0 (release público).

## Feito

| Sprint | Entrega | Release |
|---|---|---|
| 0–2 | Fundação multi-tenant, emissão síncrona sandbox, assíncrono (Hangfire) + contingência + envelopes de eventos | 0.1–0.2 |
| hardening | Falha-alto fora de sandbox, PBKDF2, validators FluentValidation, CI docker multi-arch | 0.3 |
| 1.3 | **Emissão real NFe/NFC-e via Unimake.DFe** (mapper EnviNFe, assinatura, transmissão, recibo/contingência, CSC NFC-e, perfil do emitente) | 0.4 |
| painel | Painel admin (React) + `/v1/admin/*` + JWT de operador | 0.4 |
| 1.4 | **Transmissão real de eventos** (cancelamento 110111, CC-e, inutilização) com retry próprio | 0.5 |
| 1.5 | **Webhooks com outbox + HMAC-SHA256** (replay window, retry separado do SEFAZ) | 0.6 |
| 1.6 | **DANFE/DANFCe** (QuestPDF, layout simplificado) + `GET /pdf` | 0.7 |
| 1.7 | **Distribuição DFe + Manifestação do Destinatário** (NSU, notas recebidas, manifestação com webhook) | 0.8 |
| 1.8 | **Status-servico SEFAZ** (cache 60 s) + alerta de certificado vencendo | 0.9 |
| F4 | **NFS-e Nacional**: envelope completo + sandbox (transmissão DPS real pendente) | 1.0 |
| F5 | **Backup/DR + hardening ops** (scripts, RPO/RTO, checklist go-live) | 1.1 |
| F6 | Comunidade (CONTRIBUTING, badges, roadmap público) | 1.2 |
| v2-F1 + Infra | **ICMS completo + CSOSN** (`impostosV2`: CST 00–90, CSOSN 101–900, ST, FCP, DIFAL — Simples Nacional emite) + `PUT /v1/tenants/webhooks` self-service + secret cifrado em repouso | 1.4 |
| 1.0-1.3 | **NFS-e Nacional DPS real** (`POST /nfse/dps`, layout 1.01 síncrono) + **substituição de NFS-e** (`POST {id}/substituicao`) | 1.5 |
| v2-F2 + F3 | **Item rico + totais** (GTIN, CEST, unidade, desconto, frete/seguro/outras, fórmula v2 do `valorNota`) + **IPI/PIS/COFINS** por item com totais | 1.6 |
| 1.0-4/5 | **OTel/Prometheus** (`/metrics`, métricas de negócio, alertas em `docs/observabilidade.md`) + **Redis** (rate limit distribuído por API key + cache compartilhado) | 1.7 |
| v2-F4 | **NF-ref / devolução** (`finalidade`, `tipoOperacao`, `indPres`, `indFinal`, `nfesReferenciadas` → grupo NFref) + lint do painel no CI | 1.8 |
| 1.0-6 | **PITR/WAL** (RPO ≤ 5 min + `restore-pitr.sh`) + **secret manager plugável** (`CHAVE_FILE`) + pgcrypto no compose | 1.9 |

## Próximo (antes do 1.0 público)

1. **Homologação real NFe/NFC-e/eventos/NFS-e** contra SEFAZ (exige
   certificado A1 — checklist no README) + ajustes de default por UF que
   ela revelar. A NFS-e Nacional exige ainda credenciamento do prestador.
2. **Confirmação da licença da `Unimake.DFe`** (o MIT do projeto depende
   dessa checagem).

## Como votar/propor

Abra uma issue com a label `feature` — itens do "Próximo" aceitam contribuição
(ver CONTRIBUTING.md); as regras de arquitetura do Core são inegociáveis.
