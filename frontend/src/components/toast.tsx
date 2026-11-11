// Reexporta o novo kit de toast (components/ui/toast.tsx).
//
// O provider agora usa `aria-live="polite"` e `role="status"` — a versão
// anterior não tinha região viva, então a notificação era invisível para
// quem usa leitor de tela.
//
// Este arquivo existe para o import antigo (`../components/toast`) continuar
// funcionando durante a migração das páginas; há só uma implementação.
export { ToastProvider, useToast, type ToastPayload, type ToastType } from './ui/toast'
