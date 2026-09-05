# Ajustes Finais — Errata e Revisões do Planejamento

Este documento consolida os ajustes identificados na revisão crítica. Trate como **adendo** aos documentos anteriores — os pontos abaixo substituem/complementam trechos específicos já definidos.

---

## 1. Geração de PDF — tornar plugável

**Ajuste em `Fiscal.Core`:** adicionar interface, igual já fizemos com o emissor fiscal.

```csharp
namespace Fiscal.Core.Interfaces;

public interface IGeradorPdf
{
    Task<byte[]> GerarDanfeAsync(DocumentoFiscal documento);
    Task<byte[]> GerarDanfceAsync(DocumentoFiscal documento);
    Task<byte[]> GerarDanfseAsync(DocumentoFiscal documento);
}
```

- `Fiscal.Pdf` continua existindo como a implementação **padrão** (usando QuestPDF), registrada via DI: `services.AddScoped<IGeradorPdf, QuestPdfGeradorPdf>();`
- Quem clonar o projeto e tiver restrição com a licença Community do QuestPDF (teto de faturamento) pode escrever sua própria implementação (ex.: usando outra lib 100% permissiva, ou até chamando um serviço externo) e trocar só essa linha de registro no DI — nada mais no sistema muda.
- Vale deixar isso **documentado no README** como ponto de extensibilidade intencional, junto da observação sobre a licença do QuestPDF — assim quem tiver essa restrição já sabe exatamente onde mexer, sem precisar entender o resto do código.
- Estrutura de pastas não muda (`Fiscal.Pdf` continua sendo a implementação default), só ganha a interface em `Fiscal.Core.Interfaces` e o registro fica isolado no `Program.cs`/composition root.

---

## 2. NFS-e — documentar limitação e mover para última fase do roadmap

**Ajuste no roadmap** (documento de detalhamento original): NFS-e deixa de ser Fase 3 e passa a ser a **última fase de desenvolvimento**, depois de NF-e/NFC-e estarem maduras (síncrono → assíncrono → contingência → produção/observabilidade).

**Roadmap revisado:**
1. Fase 0 — Fundação (tenants, certificados, CI básico)
2. Fase 1 — NF-e/NFC-e síncrono simples (homologação)
3. Fase 2 — Assíncrono + fila + contingência + cancelamento/CCe/inutilização
4. Fase 3 — Produção/observabilidade (logs, métricas, alertas, DANFE/DANFCe em PDF)
5. Fase 4 — **NFS-e (padrão Nacional)** — última fase
6. Fase 5 — Documentação e comunidade

**Motivo de mover pro final** (documentar isso explicitamente no README/roadmap público):
- NF-e/NFC-e seguem um padrão nacional único e estável — dá pra levar até produção com previsibilidade.
- NFS-e Nacional ainda está em adesão gradual pelos municípios — nem todo tenant vai conseguir emitir por esse padrão hoje, dependendo do município. Amadurecer primeiro o que já é sólido (NF-e/NFC-e) evita que a parte mais instável do domínio atrase a entrega do que já está pronto pra uso real.

**Texto sugerido para o README (seção de limitações conhecidas):**
> **NFS-e**: suporte via padrão Nacional apenas. Municípios que ainda não aderiram ao padrão Nacional não são suportados nesta versão. Acompanhe a adesão do seu município em [link oficial do Sistema Nacional NFS-e]. Suporte a layouts municipais legados (ABRASF/próprios) não está no roadmap atual — contribuições são bem-vindas caso a demanda justifique.

---

## 3. Pontos salvos para referência futura (sem ação imediata)

Estes ficam registrados como "ressalvas conhecidas" a considerar quando a implementação chegar nesses pontos — não exigem mudança de desenho agora:

### 3.1 Certificado A1 apenas
- Todo o desenho de certificados (upload, envelope encryption, cache em memória) pressupõe **A1**.
- **A3/HSM não é suportado** nesta versão — exigiria integração com hardware/token, fora de escopo.
- Documentar no README como limitação conhecida.

### 3.2 Reforma tributária (IBS/CBS) — isolar em camada versionada
- Campos de IBS/CBS ainda mudam com frequência (visto nos changelogs da Unimake).
- Ao implementar, isolar esses campos em um DTO/value object versionado (ex.: `TributosReformaV1` dentro de `Fiscal.Core`), separado do restante do modelo tributário — mudanças de layout da reforma ficam contidas nesse ponto, sem propagar quebra pro resto do domínio.

### 3.3 Webhook — proteção contra replay
- HMAC garante autenticidade, mas não impede reenvio de uma notificação capturada.
- Incluir **timestamp** no payload assinado (dentro do escopo do HMAC); o ERP deve validar que a notificação não é mais antiga que uma janela aceitável (ex.: 5 minutos) antes de processá-la.
- Documentar essa validação como recomendação no contrato REST/webhook, já que é o ERP consumidor quem precisa implementá-la do lado dele.

### 3.4 Idempotency-Key — amarrar ao tipo de documento
- Ajuste na constraint definida no schema:
```sql
-- Antes:
UNIQUE (tenant_id, idempotency_key)

-- Depois:
UNIQUE (tenant_id, tipo, idempotency_key)
```
- Evita que reuso acidental da mesma chave entre um NF-e e uma NFC-e do mesmo pedido devolva o documento errado.
- Mesmo ajuste vale para `eventos_fiscais` se fizer sentido (amarrar ao `tipo_evento`, o que já estava correto na constraint original dessa tabela).

---

## 4. Resumo do estado final do planejamento

| Item | Status |
|---|---|
| PDF (QuestPDF) | Plugável via `IGeradorPdf`, licença documentada no README |
| NFS-e | Última fase do roadmap, limitação de município documentada |
| Certificado A3/HSM | Fora de escopo, documentado como limitação |
| Reforma tributária | Isolar em DTO versionado ao implementar |
| Webhook replay | Timestamp assinado + validação de janela no consumidor |
| Idempotency-Key | Constraint inclui `tipo` do documento |

O planejamento está fechado. Próximo passo natural é começar a implementação por `Fiscal.Core` (entidades + interfaces, incluindo `IGeradorPdf` e `IEmissorFiscal`) e o primeiro adapter Unimake para NF-e em homologação.
