# fiap-cloud-games-catalog

CRUD de jogos, pedidos Pending, biblioteca e concessão somente após Approved.

## Estado

G3 implementado em .NET 8 com PostgreSQL/EF Core, validação JWT de Users, CRUD de jogos, pedidos assíncronos, Outbox e consumo idempotente de pagamentos. Suíte local atual: 51 testes aprovados no G8 (48 no registro histórico G3). [Evidências e limites](docs/quality/g3-validation.md).

Testes isolados usam uma fixture de pagamento independente. Integração com Payments real validada no Compose (G6) e Kubernetes Docker Desktop (G7).

## Documentação

- [ADRs aceitos](docs/architecture/decisions/README.md).
- [Decisões funcionais confirmadas](docs/architecture/functional-decisions.md).
- [Especificação de referência](docs/specification.md).
- [Rotas HTTP](docs/http-api.md) e [operação local](docs/operations.md).
- [OrderPlacedEvent V1](docs/contracts/v1/order-placed-v1.md) e [PaymentProcessedEvent V1](docs/contracts/v1/payment-processed-v1.md).
- Contratos canônicos e roadmap: repositório fiap-cloud-games-infrastructure, docs/contracts/v1/ e docs/implementation/roadmap.md. Tipos C# locais, sem dependência de projetos irmãos.

## Repositório e solução completa

Código deste serviço: [GitHub](https://github.com/alessandroibes/fiap-cloud-games-catalog). Para executar os quatro serviços juntos, siga o [README da infraestrutura](https://github.com/alessandroibes/fiap-cloud-games-infrastructure), com clones irmãos e `scripts/Start-Local.ps1` ou `scripts/Start-Kubernetes.ps1`.

Os comandos abaixo descrevem o modo de desenvolvimento individual. Não iniciar o Compose individual ao mesmo tempo que a API correspondente do Compose central, pois compartilham portas e dependências. As suítes de teste usam ambientes exclusivos.

## Execução e configuração

Com infraestrutura G1 e configuração local de Users disponíveis, na raiz deste repositório:

```powershell
./scripts/Initialize-Local.ps1
docker compose build catalog
docker compose run --rm catalog --migrate
docker compose up -d catalog
```

API em http://localhost:5082; saúde em /health/live e /health/ready. A preparação cria .env ignorado, com credencial exclusiva do Catalog e JWT compatível com Users. Não há migration automática no startup. Consulte [operação](docs/operations.md) para variáveis, execução pelo SDK, retentativas e recuperação.

```powershell
./scripts/Validate-G3.ps1
```

Testes usam PostgreSQL e RabbitMQ próprios, nas portas 55434 e 5674. A suíte interrompe seu broker e recria sua fila de Payments para validar recuperação. Não executar suítes simultâneas nesse ambiente.

## Fluxo de compra

POST /api/orders com { gameId } usa identidade do JWT e preço do catálogo, persiste Pending + Outbox e retorna 202. Somente PaymentProcessedEvent Approved concede o jogo. Rejected permite outro pedido; compra de jogo já adquirido ou com pedido pendente retorna 409. Exclusão lógica preserva pedidos e biblioteca. Pedido mantém preço histórico; biblioteca mostra preço atual.

## Organização

- Domain: Game, Order, LibraryEntry e validação monetária.
- Application: casos de uso, porta de persistência e validação do resultado de pagamento.
- Infrastructure: PostgreSQL, migration, Outbox, Inbox lógica e consumer RabbitMQ.
- Api: HTTP, JWT, autorização e health checks.
- Contracts: eventos V1 locais; tests: domínio, contratos independentes e integração real.

## Git e segurança

Clone independente fornecido pelo responsável; manter o origin existente. Commits separados, sem push ou publicação de imagens sem autorização. Documentação técnica é versionada. AGENTS.md, specs/, .codex/ e docs/course/ são locais e ignorados; segredos fora do Git.

## Kubernetes local

Manifestos e sequência de implantação em [k8s/README.md](k8s/README.md). G7 concluído; [evidências deste serviço](docs/quality/g7-validation.md) e registro transversal na infraestrutura.

## Resiliência — G8

Regressão concluída com 51 testes aprovados; [evidências e limites](docs/quality/g8-validation.md). Recuperação integrada no Kubernetes registrada na infraestrutura. Preparação documental da entrega G9 na infraestrutura; vídeo e relatório acadêmico dependem dos dados e da gravação do responsável.

## Variáveis de ambiente

Exemplo sem credenciais reais: [.env.example](.env.example). Gere a configuração com Initialize-Local; segredos efetivos ficam em .env ignorado. A infraestrutura central possui configuração própria e traduz suas variáveis para estas chaves da aplicação.

| Chave | Finalidade |
|---|---|
| ConnectionStrings__Catalog | Conexão PostgreSQL exclusiva deste serviço |
| CATALOG_DOCKER_CONNECTION | Conexão usada pelo Compose individual, com DNS postgres |
| RabbitMq__Host / RabbitMq__Port | Endereço e porta AMQP |
| RabbitMq__VirtualHost / RabbitMq__Username / RabbitMq__Password | Vhost e credencial externa do broker |
| ASPNETCORE_URLS | Endereço HTTP na execução local pelo SDK |
| Jwt__Key / Jwt__Issuer / Jwt__Audience | Assinatura e validação compatíveis entre Users e Catalog |

Portas e credenciais exclusivas de testes estão descritas em .env.example e [operação](docs/operations.md). Consulte [k8s/README.md](k8s/README.md) para ConfigMap/Secret, probes e Jobs. Nunca usar credenciais de outro banco nem executar migrations automaticamente no startup.
