import { request, type AuthInfo } from '../http'

export const auth = {
  login: (email: string, senha: string) =>
    request<AuthInfo>('/v1/admin/auth/login', { body: { email, senha } }),
}
