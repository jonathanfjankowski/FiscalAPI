import { request, requestBlob } from '../http'
import type {
  DocDetail,
  DocListItem,
  DocListResp,
  DocumentoEvento,
  EventoResp,
  InutilizacaoInput,
  InutilizacaoResp,
  ReenvioResp,
} from '../types'

export interface ListarDocumentosParams {
  tenantId?: string
  status?: string
  modelo?: string
  de?: string
  ate?: string
  page?: number
  pageSize?: number
  /** Busca livre: chave de acesso, número ou razão social. */
  q?: string
  /** Campo de ordenação (ex.: `criadoEm`). */
  ordenarPor?: string
  /** `asc` ou `desc`. */
  ordem?: 'asc' | 'desc'
}

export const documentos = {
  listDocumentos: (params: ListarDocumentosParams) => {
    const qs = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
    }
    return request<DocListResp>(`/v1/admin/documentos-fiscais?${qs.toString()}`)
  },

  getDocumento: (id: string) => request<DocDetail>(`/v1/admin/documentos-fiscais/${id}`),

  /**
   * Baixa o PDF (DANFE/DANFCe/DANFSe) do documento, autenticado.
   * Devolve um Blob para o painel abrir numa object URL — abrir a rota
   * direto não funcionaria (o header Authorization não acompanha navegação).
   */
  baixarPdf: (id: string) => requestBlob(`/v1/admin/documentos-fiscais/${id}/pdf`),

  /** Eventos fiscais do documento (cancelamento, CC-e, inutilização). */
  listEventos: (id: string) =>
    request<DocumentoEvento[]>(`/v1/admin/documentos-fiscais/${id}/eventos`),

  /** Cancela um documento autorizado (modelo 55/65). */
  cancelarDocumento: (id: string, justificativa: string, idempotencyKey?: string) =>
    request<EventoResp>(`/v1/admin/documentos-fiscais/${id}/cancelamento`, {
      body: { justificativa },
      idempotencyKey,
    }),

  /** Carta de correção — só NF-e (modelo 55). */
  cartaCorrecao: (id: string, correcao: string, idempotencyKey?: string) =>
    request<EventoResp>(`/v1/admin/documentos-fiscais/${id}/carta-correcao`, {
      body: { correcao },
      idempotencyKey,
    }),

  /**
   * Reenvia um documento REJEITADA/DENEGADA/ERRO_INTERNO com o payload
   * corrigido. Mantém o **mesmo número** — a rejeição não consumiu
   * numeração na SEFAZ.
   */
  reenviarDocumento: (id: string, payload: unknown, idempotencyKey?: string) =>
    request<ReenvioResp>(`/v1/admin/documentos-fiscais/${id}/reenviar`, {
      body: payload,
      idempotencyKey,
    }),

  /**
   * Inutiliza uma faixa de numeração que nunca foi transmitida à SEFAZ.
   * Operação DISTINTA do reenvio: reenvio corrige o payload do mesmo
   * documento; inutilização libera números. O ambiente é derivado do tenant.
   */
  inutilizar: (body: InutilizacaoInput, idempotencyKey?: string) =>
    request<InutilizacaoResp>('/v1/admin/inutilizacoes', { body, idempotencyKey }),

  /** Estado de uma inutilização. */
  obterInutilizacao: (eventoId: string) =>
    request<InutilizacaoResp>(`/v1/admin/inutilizacoes/${eventoId}`),
}

/** Itens de uma listagem de documentos (atalho tipado). */
export type { DocListItem }
