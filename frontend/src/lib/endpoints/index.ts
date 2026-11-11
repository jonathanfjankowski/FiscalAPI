// ==========================================================================
// Endpoints do painel.
//
// O objeto `api` é a fachada que as páginas consomem. Os nomes dos métodos
// existentes foram preservados — a migração das páginas para cá é
// incremental e não pode quebrar o que já funciona.
//
// A organização é por domínio (empresas, documentos, notas, …) e não por
// verbo HTTP: o painel pergunta por ações ("gerar API key"), não por
// rotas.
// ==========================================================================

import { auth } from './auth'
import { auditoria } from './auditoria'
import { bootstrap } from './bootstrap'
import { dashboard } from './dashboard'
import { documentos } from './documentos'
import { empresas } from './empresas'
import { notas } from './notas'
import { playground } from './playground'
import { statusServico } from './statusServico'

export const api = {
  // ---------- Autenticação ----------
  login: auth.login,

  // ---------- Dashboard ----------
  dashboard: dashboard.resumo,

  // ---------- Empresas ----------
  listTenants: empresas.listTenants,
  getTenant: empresas.getTenant,
  createTenant: empresas.createTenant,
  updateTenant: empresas.updateTenant,
  disableTenant: empresas.disableTenant,

  // ---------- API keys ----------
  listApiKeys: empresas.listApiKeys,
  createApiKey: empresas.createApiKey,
  revokeApiKey: empresas.revokeApiKey,

  // ---------- Certificados ----------
  listCertificados: empresas.listCertificados,
  uploadCertificado: empresas.uploadCertificado,
  disableCertificado: empresas.disableCertificado,
  ativarCertificado: empresas.ativarCertificado,

  // ---------- Documentos fiscais ----------
  listDocumentos: documentos.listDocumentos,
  getDocumento: documentos.getDocumento,
  baixarPdf: documentos.baixarPdf,
  listEventos: documentos.listEventos,
  cancelarDocumento: documentos.cancelarDocumento,
  cartaCorrecao: documentos.cartaCorrecao,
  reenviarDocumento: documentos.reenviarDocumento,

  // ---------- Notas recebidas (DFe) ----------
  listNotasRecebidas: notas.listNotasRecebidas,
  getNotaRecebida: notas.getNotaRecebida,
  xmlCompletoNota: notas.xmlCompleto,
  manifestar: notas.manifestar,

  // ---------- Auditoria ----------
  listarAuditoria: auditoria.listar,

  // ---------- Status do serviço SEFAZ ----------
  statusServico: statusServico.consultar,

  // ---------- Webhooks ----------
  listWebhookEntregas: empresas.listWebhookEntregas,
  reenviarWebhookEntrega: empresas.reenviarWebhookEntrega,

  // ---------- Chave master ----------
  getBootstrapKey: bootstrap.getBootstrapKey,
  rotacionarBootstrapKey: bootstrap.rotacionarBootstrapKey,

  // ---------- Playground (caminho do integrador) ----------
  emitir: playground.emitir,
  consultarComoTenant: playground.consultarComoTenant,
  validarApiKey: playground.validarApiKey,
  previewPdf: playground.previewPdf,
}
