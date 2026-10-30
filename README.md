# fiap-cloud-games-catalog

CRUD de jogos, pedidos Pending, biblioteca e concessão somente após Approved.

## Estado

Preparação documental G0 em 30/10/2026. Ainda não há aplicação executável, Dockerfile ou manifests Kubernetes. Nenhum teste de aplicação foi executado. Os gates seguintes entregarão essas capacidades com evidências.

## Documentação

- [ADRs aceitos](docs/architecture/decisions/README.md).
- [Decisões funcionais confirmadas](docs/architecture/functional-decisions.md).
- [Especificação de referência](docs/specification.md).
- Contratos canônicos e roadmap: repositório fiap-cloud-games-infrastructure, docs/contracts/v1/ e docs/implementation/roadmap.md. Tipos C# serão locais, sem dependência de projetos irmãos.

## Execução e configuração

Comandos, variáveis, migrations, Docker e Kubernetes serão documentados quando implementados e verificados. PostgreSQL isolado por serviço persistente, RabbitMQ/MassTransit e Docker Desktop Kubernetes são decisões aprovadas. Migrations serão explícitas antes das APIs. Não utilizar SQLite ou Kind.

## Git e segurança

Clone independente fornecido pelo responsável; manter o origin existente. Commits separados, sem push ou publicação de imagens sem autorização. Documentação técnica é versionada. AGENTS.md, specs/, .codex/ e docs/course/ são locais e ignorados; segredos fora do Git.
