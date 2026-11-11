// ==========================================================================
// Fachada de compatibilidade.
//
// O painel consome `api` daqui. A implementação vive em lib/endpoints/*
// e os tipos em lib/types.ts; este arquivo só re-exporta, para as páginas
// existentes continuarem funcionando durante a migração.
// ==========================================================================

export { api } from './endpoints'
export * from './types'

export {
  ApiError,
  UNAUTHORIZED_EVENT,
  clearAuth,
  getAuth,
  request,
  requestBlob,
  setAuth,
  type AuthInfo,
  type RequestOptions,
} from './http'
