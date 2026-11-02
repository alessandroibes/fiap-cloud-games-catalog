# Operação local — CatalogAPI

## Preparação

Pré-requisitos: SDK .NET 8.0.414 ou patch compatível, Docker Desktop Linux, infraestrutura G1 em execução e configuração JWT de Users preparada. Não importar dados da Fase 1.

Na raiz deste repositório:

```powershell
./scripts/Initialize-Local.ps1
docker compose config --quiet
docker compose build catalog
docker compose run --rm catalog --migrate
docker compose up -d catalog
```

Initialize-Local lê os arquivos .env locais de Infraestrutura e Users apenas para preparar configuração: usa a credencial exclusiva fcg_catalog e a mesma chave/issuer/audience JWT de Users. Caminhos alternativos: -InfrastructureEnv e -UsersEnv. Não copia credenciais do banco Users. Gera segredos de testes e preserva .env existente, sem imprimir valores. Não há referência de projeto ou leitura de arquivos irmãos pela aplicação em execução.

Compose conecta à rede externa fiap-cloud-games_default criada no G1. API em http://127.0.0.1:5082; somente loopback é publicado. Dockerfile multi-stage com bases fixadas por digest e runtime não privilegiado. Imagem fiap-cloud-games-catalog:local; nenhuma publicação automática. `docker compose stop catalog` para a API sem remover dados.

Alternativa pelo SDK:

```powershell
./scripts/Invoke-Catalog.ps1 -Action migrate
./scripts/Invoke-Catalog.ps1 -Action run
```

O script importa .env somente no processo e restaura o ambiente ao terminar. Se a política local bloquear scripts, usar -ExecutionPolicy Bypass somente no processo, sem alterar a política global.

## Configuração e segurança

| Chave | Uso |
|---|---|
| ConnectionStrings__Catalog | PostgreSQL exclusivo fcg_catalog; localhost:55432 |
| CATALOG_DOCKER_CONNECTION | Mesmo banco e credencial via postgres:5432 no Compose |
| Jwt__Key | Mesma chave simétrica da UsersAPI, mínimo 32 bytes |
| Jwt__Issuer / Jwt__Audience | fiap-cloud-games-users / fiap-cloud-games |
| RabbitMq__Host / Port / VirtualHost | Broker externo; no Compose rabbitmq:5672, vhost fcg |
| RabbitMq__Username / Password | Credenciais externas; não usar guest |
| TEST_DB_PASSWORD / TEST_RABBIT_PASSWORD | Segredos exclusivos de testes |
| TEST_DB_PORT / TEST_RABBIT_PORT | 55434 / 5674 por padrão |
| ASPNETCORE_URLS | Endereço do processo SDK; container usa 8080 |

Nunca versionar .env ou JWTs. `docker compose config` sem --quiet expande segredos; não compartilhar essa saída. A chave simétrica é distribuída operacionalmente aos serviços autorizados; rotação exige atualizar Users e validadores coordenadamente. Tokens refletem o papel no momento de emissão e expiram conforme Users. Logs JSON da aplicação usam identificadores técnicos, sem corpo de requisição, token ou string de conexão. HTTP somente local; exposição externa exige terminação TLS.

## Persistência e migrations

CatalogDbContext possui Games, Orders, Library, PaymentReceipts e tabelas técnicas MassTransit. GUIDs de usuários não possuem FK nem acesso ao banco Users. Relacionamentos locais usam Restrict, sem cascata que apague histórico. Migration InitialCatalogAndMessaging é própria do Catalog e não reutiliza migrations SQLite.

Executar --migrate explicitamente antes da API. Esse comando aplica somente o banco Catalog e encerra sem iniciar o bus/servidor. Startup normal não chama Migrate/EnsureCreated; readiness retorna 503 quando faltam migrations. A ferramenta dotnet-ef 9.0.10 está fixada em .config/dotnet-tools.json. Para futuras migrations, configurar ConnectionStrings__Catalog no processo e usar Infrastructure como project e Api como startup-project; revisar e testar antes de aplicar. Não utilizar reset/drop de banco como atualização.

## Concorrência e confiabilidade

Criar pedido, alterar/desativar jogo e processar pagamento usam `SELECT ... FOR UPDATE` no jogo dentro da transação PostgreSQL. Esse bloqueio conservador serializa operações por jogo, inclusive entre réplicas. É adequado ao volume acadêmico; poderá limitar throughput em jogos muito disputados. Não é um lock apenas em memória.

O índice parcial UX_Orders_Pending impede dois pedidos Pending do mesmo usuário/jogo; Library tem PK (UserId, GameId) e OrderId único; Orders tem PaymentId único. A transação verifica posse e pendência antes de criar pedido. `numeric(18,2)` e validação de duas casas impedem arredondamento silencioso pela API. Pedido e OrderPlacedEvent são persistidos na mesma transação, com MassTransit EF Bus Outbox.

Fila durável payments-order-placed-v1 é provisionada pelo binding de publicação com DeployPublishTopology antes da entrega da Outbox, mesmo se Payments ainda não foi implantado. Catalog consome catalog-payment-processed-v1. Notifications terá sua própria fila, sem competir com Catalog.

PaymentReceipts implementa Inbox lógica durável pelo EventId: advisory lock transacional protege concorrência desse identificador, e fingerprint SHA-256 detecta reutilização com conteúdo divergente. A mesma transação grava resultado do pedido, concessão quando Approved e receipt. A tabela InboxState do modelo MassTransit não é utilizada como mecanismo de deduplicação deste consumer; PaymentReceipts mantém a garantia independente do MessageId e da janela de retenção do transporte. Não remover receipts sem planejar a retenção/idempotência.

Antes de alterar estado, o consumer valida GUIDs não vazios, data UTC, status exato Approved/Rejected, BRL e preço, depois confere usuário/jogo/preço/moeda contra o pedido. Pedido inexistente, dados divergentes ou resultado terminal conflitante geram erro permanente, sem novas tentativas. Resultado equivalente, inclusive com outro EventId, não duplica concessão. Rejected nunca adiciona biblioteca; aprovação de pedido anterior à desativação continua válida.

Falhas transitórias têm três retentativas em 1, 5 e 15 segundos (quatro tentativas totais); após esgotamento, mensagem segue para catalog-payment-processed-v1_error. Não há consumidor automático da fila de erro em produção. Corrigir a causa, inspecionar identificadores/headers e reenviar a mensagem original preservando EventId e dados; não editar resultado para forçar aprovação. Reenvio por outra identidade de transporte é seguro pela Inbox lógica. Eventos inválidos exigem investigação do produtor/contrato, sem loop automático de replay.

A Outbox mantém entregas pendentes durante indisponibilidade do broker e retoma após recuperação/reinício. Readiness mede banco/schema, permitindo novos pedidos com broker indisponível; não indica pagamento concluído nem backlog vazio. Observar logs MassTransit, OutboxMessage e contagens das filas. Entrega é at-least-once; uma falha após commit antes do ACK pode repetir consumo/log, mas não a concessão lógica.

## Testes e limites do gate

```powershell
./scripts/Validate-G3.ps1
```

O script sobe projeto Compose fcg-catalog-g3-tests com banco fcg_catalog_test e broker/vhost fcg_test próprios, restaura com lockfiles, compila e testa. Portas 55434 e 5674 não conflitam com G1/Users. A suíte interrompe e reinicia somente seu broker e recria a fila payments-order-placed-v1 de testes. Não executar duas suítes simultâneas nesse ambiente. Volumes e dados permanecem; testes usam GUIDs novos. A fixture observa e consome a fila de erro apenas nesse broker para comprovar roteamento; a API de produção não consome _error.

Health check do broker executa como usuário rabbitmq, evitando criar cookie Erlang pertencente a root durante a primeira inicialização. Não imprimir o conteúdo do cookie. Não limpar volumes de outros projetos para resolver falhas locais.

`dotnet test FiapCloudGames.Catalog.sln --filter 'Category!=Integration'` executa domínio/contratos sem containers. TRX fica em TestResults ignorado pelo Git. Contratos são testados com assembly independente; os eventos de resultado dos testes são publicados pela fixture, que não substitui PaymentsAPI. G4 implementa Payments real; G6 valida ponta a ponta; /k8s e Docker Desktop Kubernetes ficam para G7.

.NET 8, EF Core 9.0.10, Npgsql EF 9.0.4 e MassTransit 8.5.8 mantêm compatibilidade com G1/G2. Lockfiles fixam dependências diretas/transitivas.
