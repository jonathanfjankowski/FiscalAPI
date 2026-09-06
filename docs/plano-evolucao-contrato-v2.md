# Plano de evolução do contrato de emissão (v2)

> **Status (2026-09-06)**: **F1 (ICMS completo + CSOSN) e Infra
> (self-service de webhook, G10) — implementadas** na `1.4.0-alpha`.
> **F2 (item rico + totais) e F3 (IPI/PIS/COFINS) — implementadas** na
> `1.6.0-alpha`. **F4 (NF-ref/devolução) — implementada** na `1.8.0-alpha`.
> F5–F6 permanecem planejadas, na ordem recomendada abaixo.
>
> **Escopo**: emissão de **NF-e (modelo 55) e NFC-e (modelo 65)**. A NFS-e
> Nacional segue plano próprio (ver `docs/roadmap.md`).
>
> **Premissa inalterada**: a API **não calcula tributos**. O sistema
> integrador (ERP) calcula e envia todos os valores prontos; a API valida a
> aritmética informada e monta/assina/transmite o XML.
>
> **Fonte de verdade atual**: `src/Fiscal.Core/Contracts/EmissaoDtos.cs`
> (contrato REST) e `src/Fiscal.Adapters.Unimake/MapperEnviNFe.cs`
> (montagem do XML layout 4.00).

## 1. Por que evoluir o contrato

O contrato atual atende o fluxo básico de emissão, mas cobre um subconjunto
do layout 4.00. Os gaps abaixo impedem perfis reais de adoção e estão
ordenados por impacto:

| # | Gap | Consequência |
|---|---|---|
| G1 | ICMS apenas com CST `00/40/41/50` (grupos `10/20/51/60/70/90` falham alto) | Sem ST, sem redução de base, sem diferimento — regra normal incompleta |
| G2 | Sem CSOSN | **Simples Nacional não emite** — bloqueia a maior fatia do varejo |
| G3 | Um único grupo de imposto por item (lista plana `{cst, base, alíquota, valor}`) | Impossível informar ICMS + IPI + PIS/COFINS no mesmo item |
| G4 | Sem IPI, PIS e COFINS | NF-e de regime normal sai sem destaque obrigatório |
| G5 | Sem desconto, frete, seguro e outras despesas | Total da nota = soma bruta; operações com esses valores não representam o total real |
| G6 | Unidade fixa `"UN"` e GTIN fixo `"SEM GTIN"` | Vendas por kg/m/l saem erradas; sem EAN |
| G7 | Sem NF-ref (chave referenciada) nem `finNFe` configurável | Devolução e nota complementar impossíveis de emitir |
| G8 | Sem campos da reforma tributária (IBS/CBS, `cClassTrib`, IS — LC 214/2025) | 2026 é ano de teste obrigatório da reforma; risco regulatório |
| G9 | DANFE simplificado (sem código de barras da chave nem QR Code do DANFCe) | PDF não serve para circulação legal |
| G10 | Webhook (`webhookUrl`/`webhookSecret`) configurável só pelo painel admin | Integrador não consegue self-service |

## 2. Princípios da evolução

| Tema | Decisão |
|---|---|
| Compatibilidade | **Evolução aditiva no mesmo endpoint** (`/v1/documentos-fiscais/nfe` \| `/nfce`). Campos novos são opcionais; o payload atual continua emitindo igual. Não haverá rota `/v2`. |
| `impostos` legado | A lista plana atual (`impostos[]`) continua aceita e mapeada para ICMS `00/40/41/50`. Item com **ambos** `impostos[]` e `impostosV2` → `400` (ambíguo). |
| Fail-loud | Combinação ainda não suportada pelo mapper → `ERRO_INTERNO` com mensagem explícita, nunca transmissão errada (comportamento atual preservado até a fase correspondente implementá-la). |
| API não calcula | Continua recebendo valores prontos por grupo de imposto; a validação aritmética é estendida a cada grupo novo (tolerância de R$ 0,01). |
| Regime do emitente | O tenant tem `regime_tributario`, mas a validação **não cruza** regime × CST/CSOSN — emitente do Simples com excesso usa ICMS normal. Cada item declara o grupo de imposto que usa. |
| Reforma tributária | Campos da reforma dependem das NT 2025.x do layout 4.00 e de atualização do `Unimake.DFe` — fase isolada, com verificação de versão mínima do pacote. |

## 3. Contrato v2 (DTOs propostos)

### 3.1 Item

```csharp
public record ItemDtoV2(
    string Codigo,
    string Descricao,
    string? Ncm,
    string? Cest,                 // novo (7 dígitos)
    string? Cfop,
    string? Gtin,                 // novo (EAN 8/12/13/14) — default "SEM GTIN"
    string? Unidade,              // novo (uCom, máx 6) — default "UN"
    [Range(0.0001, double.MaxValue)] decimal Quantidade,   // 4 decimais
    decimal ValorUnitario,
    decimal? ValorDesconto,       // novo — desconto no item
    decimal ValorTotal,           // semântica mantida: qtd × unitário (bruto, sem desconto)
    ItemImpostosDtoV2? ImpostosV2);
```

### 3.2 Grupos de imposto do item

```csharp
public record ItemImpostosDtoV2(
    IcmsDto? Icms,
    IpiDto? Ipi,
    PisDto? Pis,
    CofinsDto? Cofins,
    IbsCbsDto? IbsCbs,    // reforma — fase F5
    IsDto? Is);           // reforma — fase F5

public record IcmsDto(
    [Range(0, 8)] int Origem,               // 0–8 (tabela A) — default 0
    string? Cst,                             // 00,10,20,40,41,51,60,70,90
    string? Csosn,                           // 101,102,103,201,202,203,300,400,500,900
    string? ModBc,                           // 0–4 — default 3
    decimal? BaseCalculo, decimal? Aliquota, decimal? Valor,
    decimal? PercentualReducaoBc,            // CST 20/51/70 e CSOSN 201/900
    decimal? FcpPercentual, decimal? ValorFcp,
    IcmsStDto? St,                           // CST 10/60/70/90 e CSOSN 201/202/203/500
    DifalDto? Difal);                        // interestadual consumidor final

public record IcmsStDto(
    string? ModBcSt, decimal? PercentualMva,
    decimal? BaseCalculoSt, decimal? AliquotaSt, decimal? ValorSt,
    decimal? PercentualReducaoBcSt,
    decimal? FcpPercentualSt, decimal? ValorFcpSt);

public record DifalDto(                      // partilha 100% destino (Convênio 190/2017 vigente)
    decimal? BaseDestino, decimal? AliquotaDestino,
    decimal? ValorIcmsDestino, decimal? ValorIcmsOrigem,
    decimal? FcpPercentualDestino, decimal? ValorFcpDestino);

public record IpiDto(string Cst, decimal? BaseCalculo, decimal? Aliquota, decimal? Valor);     // CSTs 00–05/49/50–55/99
public record PisDto(string Cst, decimal? BaseCalculo, decimal? Aliquota, decimal? Valor);     // CSTs 01–99 (entrada/saída)
public record CofinsDto(string Cst, decimal? BaseCalculo, decimal? Aliquota, decimal? Valor);  // CSTs 01–99
```

### 3.3 Reforma tributária (LC 214/2025) — fase F5

```csharp
public record IbsCbsDto(
    string CstIbsCbs,                // 3 dígitos (ex.: 000 tributada integral, 400 isenta/não tributada)
    string CClassTrib,               // 6 dígitos — classificação SEPEC
    decimal? BaseCalculo,
    decimal? AliquotaCbs, decimal? PercentualReducaoCbs, decimal? ValorCbs,
    decimal? AliquotaIbsEstadual, decimal? PercentualReducaoIbsEstadual, decimal? ValorIbsEstadual,
    decimal? AliquotaIbsMunicipal, decimal? PercentualReducaoIbsMunicipal, decimal? ValorIbsMunicipal,
    decimal? PercentualDiferimentoCbs, decimal? ValorDiferidoCbs,
    decimal? PercentualDiferimentoIbs, decimal? ValorDiferidoIbs,
    string? CstCreditoPresumido, decimal? ValorCreditoPresumido);   // gIBSCredPres

public record IsDto(                 // Imposto Seletivo
    string CstIs,                    // 2 dígitos (SEPEC)
    decimal? BaseCalculo, decimal? Aliquota, decimal? Valor,
    string? TipoBaseCalculo);        // valor|quantidade|area|volume
```

### 3.4 Cabeçalho e totais

```csharp
public record EmissaoRequest(
    string Ambiente, short Serie,
    DestinatarioDto? Destinatario,
    List<ItemDtoV2> Itens,
    TotaisDtoV2 Totais,
    List<PagamentoDto>? Pagamento,
    string? NaturezaOperacao,
    string? Finalidade,                    // novo: normal|complementar|ajuste|devolucao → finNFe (default normal)
    string? TipoOperacao,                  // novo: saida|entrada → tpNF (default saida)
    string? IndicadorPresenca,             // novo: presencial|internet|teleatendimento|... → indPres
    string? IndicadorConsumidorFinal,      // novo: sim|nao → indFinal (default sim, mantém atual)
    List<NfRefDto>? NfesReferenciadas,     // novo — F4
    TransporteDto? Transporte);            // novo — backlog (ver §7)

public record NfRefDto(string ChaveAcesso);   // 44 dígitos; CNPJ/chave do emitente = tenant

public record TotaisDtoV2(
    decimal ValorProdutos,
    decimal ValorDesconto,                 // novo
    decimal ValorFrete,                    // novo — compõe o total da nota
    decimal ValorSeguro,                   // novo
    decimal OutrasDespesas,                // novo
    // totais de impostos para conferência aritmética e DANFE
    decimal? ValorIcms, decimal? ValorIcmsSt, decimal? ValorFcp, decimal? ValorIpi,
    decimal? ValorPis, decimal? ValorCofins,
    decimal? ValorCbs, decimal? ValorIbs, decimal? ValorIs,
    decimal ValorNota);                    // fórmula v2: produtos − desconto + frete + seguro + outras
                                           // + ICMS-ST + FCP + IPI + IS (ver §5.3)
```

## 4. Regras de validação novas

- **Aritmética por grupo**: `base × alíquota/100 = valor` (tolerância 0,01)
  para ICMS, ST, FCP, IPI, PIS, COFINS, CBS, IBS e IS.
- **CST isento com valor**: CST `40/41/50/60`, CSOSN `300/400/500` sem ST e
  PIS/COFINS `04–09` com valor > 0 → `422`.
- **CST ou CSOSN**: apenas um por item (os dois → `422`). Origem obrigatória.
- **DIFAL**: `DifalDto` completo quando CST interestadual + consumidor
  final — validação por declaração (presença do campo), sem cruzar a UF do
  tenant.
- **Total da nota**: fórmula do §5.3 conferida campo a campo.
- **NF-ref**: obrigatória quando `finalidade = devolucao` (`422` se vazia).
  Chave com 44 dígitos e `cUF`/`YYMM` coerentes.
- **Reforma**: `CClassTrib` obrigatório sempre que `IbsCbs != null`;
  `CstIbsCbs` com 3 dígitos; IS apenas com tipo de base declarado.

## 5. Impacto por camada

### 5.1 Mapper (`MapperEnviNFe`, layout 4.00)

- Grupos ICMS `ICMS00/10/20/40/51/60/70/90` + `ICMSSN101/102/103/201/202/
  203/300/400/500/900` + ICMS-ST retido (`ICMSPart` fica fora do escopo).
- Detalhe do item: `qCom` (4 decimais), `uCom`, `vDesc`, `cEAN` (GTIN), `CEST`.
- Totais do `ICMSTot`: `vDesc`, `vFrete`, `vSeg`, `vOutro`, `vICMSDeson`,
  `vICMSST`, `vFCP*`, `vIPI`, `vPIS`, `vCOFINS`.
- NF-ref → grupo `NFref` (chave de 44 dígitos).
- Reforma: grupos `gIBSCBS`/`gIBSCredPres`/`gCBSCredPres` e IS conforme NT
  2025.0002+ — se a versão do `Unimake.DFe` em uso não expuser os grupos, a
  fase fica bloqueada e a mensagem de erro informa a versão mínima exigida.
- `indPres`/`indFinal`/`finNFe`/`tpNF` deixam de ser fixos e passam a vir do
  request (defaults = valores atuais).

### 5.2 Semântica do `valorNota` (mudança de fórmula)

- **Payload atual** (sem campos novos): regra vigente —
  `valorNota = Σ valorTotal dos itens`.
- **Payload v2** (qualquer campo novo de total presente):
  `valorNota = Σ(brutos) − Σ(descontos) + frete + seguro + outras + ST +
  FCP + IPI + IS`.
- A escolha é determinística: se `TotaisDtoV2` tiver qualquer propriedade
  nova preenchida, aplica-se a fórmula v2. Regra documentada em
  `docs/integracao-api.md`.

### 5.3 PDF (`GeradorPdfQuestPdf`) — fase F6

- Código de barras CODE-128 da chave de acesso no padrão visual do DANFE
  (dependência de lib de barcode — avaliar licença; QR já coberto por
  QRCoder).
- QR Code do DANFCe (URL da SEFAZ da UF + chaves concatenadas + CSC —
  reusa `csc_id`/`csc` já armazenados do tenant).
- Destaques novos: desconto/frete/seguro/outras, ST/FCP/IPI/PIS/COFINS,
  bloco CBS/IBS/IS, NF-e referenciadas e quadro "Dados da NF-e" com
  `indPres`/`finNFe`.
- Fonte de dados passa a ser o payload **tipado** (v2), não mais apenas o
  JSON de entrada serializado.

### 5.4 Infra de integração (G10)

- `PUT /v1/tenants/webhooks` para o próprio integrador configurar
  `webhookUrl` + `webhookSecret` (mesmo formato do painel) — self-service
  sem depender do admin. A assinatura HMAC-SHA256 e a janela anti-replay
  de 5 min permanecem como estão.

## 6. Fases de implantação

| Fase | Entrega | Depende de | Critério de aceite (homologação) | Risco |
|---|---|---|---|---|
| **F1 — ICMS completo + CSOSN** | G1+G2+G3 (grupos tipados de ICMS + CSOSN + origem/modBC/redução/FCP + DIFAL por declaração) | — | Matriz de emissão: cada CST (00–90) e cada CSOSN (101–900) autorizada contra SEFAZ homologação; zero rejeição por grupo ausente | Médio — mapper é o arquivo mais sensível do projeto |
| **F2 — Item rico + totais** | G5+G6 (GTIN, unidade, desconto, frete/seguro/outras, fórmula de total v2) | F1 | NF-e com desconto por item + frete autoriza; `ICMSTot` bate com os totais informados; PDF exibe os novos campos | Baixo |
| **F3 — IPI/PIS/COFINS** | G4 (grupos por item + totais) | F1 | NF-e de regime normal com IPI + PIS + COFINS destacados autoriza; aritmética por grupo em `422` | Baixo |
| **F4 — NF-ref / devolução** | G7 (`finalidade`, `tipoOperacao`, `NfRefDto` → grupo `NFref`) | F2 | Devolução (`finNFe=4`) com chave referenciada autoriza; devolução sem NF-ref → `422` | Baixo |
| **F5 — Reforma (IBS/CBS + IS)** | G8 (`IbsCbsDto` + `IsDto` + totais + validação) | F1 + versão mínima do `Unimake.DFe` com NT 2025.x | NF-e com `gIBSCBS` + `cClassTrib` autoriza em ambiente de teste da reforma; ano-teste 2026 com alíquotas 0,9/0,1 aceitas | **Alto** — NTs ainda em evolução; fase isolada para conter o impacto |
| **F6 — DANFE oficial** | G9 (barcode + QR + destaques) | F1–F3 (dados) | DANFE no layout visual padrão; QR do DANFCe escaneado com sucesso na consulta de homologação da SEFAZ | Médio — dependência de lib de barcode |
| **Infra — self-service de webhook** | G10 (`PUT /v1/tenants/webhooks`) | — | Integrador configura webhook sem painel; assinatura HMAC inalterada | Baixo |

**Ordem recomendada**: F1 → Infra → F2 → F3 → F4 → F5 → F6. A F1 destrava o
Simples Nacional (maior impacto prático); a F5 é a única com risco
regulatório e fica deliberadamente por último entre as fases de emissão.

## 7. Backlog (fora do escopo v2)

- **Bloco de transporte completo** (`TransporteDto`: modal, transportadora,
  volumes, lacres, placa) — o **valor** do frete já entra na F2; os dados de
  transporte são separados.
- Exportação, lote de emissão (batch), NF-e de importação (`DI`/`DUE`),
  grupo `ICMSPart`, dados de transação de cartão (`tBand`/`cAut`).
- NFS-e Nacional real (transmissão DPS) — tracked no `docs/roadmap.md`.

## 8. Compatibilidade — resumo

| Payload enviado | Comportamento |
|---|---|
| Atual (lista plana `impostos[]`, sem campos novos) | Inalterado — ICMS `00/40/41/50`, total = soma dos itens |
| `impostosV2` presente | Novos grupos entram no XML; validação estendida; `valorNota` pela fórmula v2 |
| `impostos[]` **e** `impostosV2` no mesmo item | `400` — ambíguo |
| Campo novo ainda não suportado pela fase implantada | `ERRO_INTERNO` com mensagem explícita (fail-loud, como hoje) |
