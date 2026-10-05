-- Criptografia em repouso no nível de aplicação (checklist go-live,
-- docs/backup-dr.md): pgcrypto fica disponível para funções hash/cifra.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
