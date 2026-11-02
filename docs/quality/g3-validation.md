# Evidências do G3 — CatalogAPI

Validação local em 02/11/2026, Windows/PowerShell, .NET 8 e Docker Desktop Linux. Código e documentação alterados somente neste repositório. Fase 1 preservada. Esta evidência registra execuções locais, não um pipeline remoto.

## Comandos e resultados

| Verificação | Resultado observado |
|---|---|
| dotnet restore FiapCloudGames.Catalog.sln --locked-mode --force | Sucesso sem avisos na execução com acesso ao NuGet |
| dotnet build FiapCloudGames.Catalog.sln --no-restore -m:1 | Sucesso; zero avisos e erros |
| dotnet test FiapCloudGames.Catalog.sln --no-build --logger 'trx;LogFileName=g3-tests.trx' | 48 executados, 48 aprovados, zero falhas/ignorados; execução final de 15:29:53 a 15:31:13, horário de Brasília |
| dotnet format whitespace FiapCloudGames.Catalog.sln --verify-no-changes --no-restore | Código de saída zero |
| dotnet ef migrations has-pending-model-changes, Infrastructure/Api, --no-build | Nenhuma alteração pendente no modelo |
| docker compose config --quiet e docker compose build catalog | Sucesso; restore locked e publish Release no Dockerfile |
| docker compose run --rm catalog --migrate, duas execuções | Ambas concluídas; uma migration registrada no banco fcg_catalog |
| Container Catalog | Em execução, UID 1654, porta 5082 publicada somente em localhost |
| GET /health/live e /health/ready | HTTP 200 |
| Login real na UsersAPI e POST /api/games no Catalog | JWT aceito e jogo criado com HTTP 201 |
| POST /api/orders e consulta posterior | HTTP 202 e estado Pending; snapshot R$ 29,90 |
| Biblioteca após pedido inicial | Zero concessões |
| Entrega da Outbox | Zero OutboxMessage pendentes; uma mensagem em payments-order-placed-v1, sem consumidor Payments ainda |
| Consumer Catalog | catalog-payment-processed-v1 com um consumidor ativo |

Imagem local validada: `sha256:1d1c44521df1196c4de5bfc68e9d11f81c7faffed547b1d1121713c4a867c9a6`. Não publicada em registry.

Pedido demonstrativo criado no ambiente G1: `1ef2ad06-a7be-44d9-b8e2-0449d3aa636f`. Seu estado era Pending na captura; poderá evoluir quando Payments for implantado. Não confundir essa captura com resultado de pagamento ponta a ponta. Credenciais e tokens não foram incluídos na evidência.

## Cobertura da suíte

- Domínio: nome/preço válidos, preço zero, precisão/escala, snapshot, jogo inativo, concessão exclusiva por Approved e imutabilidade terminal.
- Contratos V1: campos, serialização, decimal numérico, UTC, campos obrigatórios, identidade MassTransit e tipos em assembly independente. Transmissão real de OrderPlacedEvent e PaymentProcessedEvent pelo broker.
- HTTP: CRUD, validações, Admin, 401/403, assinatura/issuer/audience/expiração/sub inválidos e isolamento do pedido/biblioteca por proprietário.
- Concorrência: oito compras simultâneas geram um Pending e sete conflitos; índice PostgreSQL impede duplicação mesmo sem lock da aplicação.
- Pagamento: Approved concede; Rejected não concede e permite novo pedido; alteração de preço preserva snapshot; desativação não impede concluir pedido anterior.
- Idempotência: duplicatas concorrentes com MessageId diferente, EventId novo com resultado equivalente e reenvio após reiniciar API não duplicam biblioteca. EventId com payload divergente é recusado.
- Conflitos: resultados simultâneos não revertem o primeiro estado terminal; PaymentId reutilizado em outro pedido falha sem concessão parcial.
- Mensagens inválidas: usuário/jogo/preço/moeda/status/data/identificadores divergentes chegam à fila _error e preservam Pending.
- Atomicidade: falha antes do commit do pedido não deixa pedido/Outbox; falha antes do commit do pagamento não deixa concessão/receipt parcial.
- Retentativas: falha transitória recupera; falha persistente executa quatro tentativas totais, vai a _error e permite recuperação por replay após corrigir a causa.
- Broker indisponível: pedido persiste e entrega é recuperada após reiniciar API/broker. Fila de Payments recriada confirma retenção antes do primeiro consumidor.
- Startup com schema vazio não aplica migrations; liveness 200, readiness 503 e zero tabelas criadas.

## Ajustes encontrados durante a validação

O primeiro startup do broker de testes falhou porque o cookie Erlang havia sido criado como root. Foi corrigida somente sua propriedade/permissão no volume Catalog; health check passou a executar como rabbitmq. Nenhum volume foi apagado. A primeira suíte passou com 45 casos; depois foram acrescentados três cenários de concorrência/unicidade e reforçado o teste de fila recriada, resultando nos 48 casos finais aprovados.

Uma consulta de auditoria NuGet dentro do sandbox retornou NU1900 por indisponibilidade de rede. Restore repetido com acesso à rede concluiu sem avisos. Isso não representa auditoria exaustiva de segurança.

## Reprodução e limites

Executar `scripts/Initialize-Local.ps1` e `scripts/Validate-G3.ps1`, conforme [operação](../operations.md). TRX fica em TestResults do projeto de testes, ignorado pelo Git. O broker de testes é exclusivo e sua fila de erro é consumida pela fixture para verificar roteamento; em produção, _error permanece para investigação e replay controlado.

PaymentsAPI real será implementada no G4. Os testes publicam resultados por fixture independente, sem simular aprovação dentro do Catalog. Notificações, Compose completo e Kubernetes permanecem nos gates G5, G6 e G7. Não foi executado teste de carga nem falha de processo precisamente entre commit e ACK; a revisão de resiliência G8 deverá ampliar essa evidência. A garantia é entrega at-least-once com concessão lógica idempotente.
