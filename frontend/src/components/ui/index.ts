// Kit de componentes do painel — Radix para o comportamento acessível,
// estilo próprio por cima.
//
// Importar do barrel: `import { Button, Dialog } from '../components/ui'`.

export { Badge, AmbienteBadge, StatusBadge, StatusDot } from './badge'
export { Button, type ButtonProps, type ButtonVariant } from './button'
export {
  Dialog,
  ConfirmDialog,
  type DialogProps,
  type ConfirmDialogProps,
} from './dialog'
export {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './dropdown-menu'
export {
  EmptyState,
  ErrorState,
  PageHeader,
  PageLoading,
  Skeleton,
  Spinner,
  WarningState,
  type EmptyStateProps,
  type ErrorStateProps,
} from './estados'
export { Field, Input, Select, Textarea } from './input'
export { Popover, PopoverAnchor, PopoverContent, PopoverTrigger } from './popover'
export { Table, TableBody, Td, Th } from './table'
export { Tabs } from './tabs'
export { Switch, type SwitchProps } from './switch'
export { Tooltip, TooltipProvider } from './tooltip'
export { ToastProvider } from './toast'
export { useToast, type ToastPayload, type ToastType } from '../../lib/toastContext'
