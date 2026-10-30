# SPEC-P2-CATALOG — CatalogAPI

**Estado:** Especificação de planejamento, alinhada aos ADRs aceitos; implementação ainda não iniciada.

## Responsabilidade
CRUD de jogos, pedido de compra e biblioteca. CatalogAPI é dono dos dados de jogos, pedidos e biblioteca. Nenhuma concessão direta na requisição inicial de compra.

## Fluxo e aceite
1. CRUD de jogos mantém validações pertinentes da Fase 1 e autorização administrativa.
2. Solicitação de compra válida registra pedido `Pending`, com `OrderId`, `UserId`, `GameId` e preço capturado, e publica `OrderPlacedEvent`.
3. Resposta aprovada no ADR-P2-005: HTTP 202 Accepted + `orderId` e `status=Pending`. Entrada confirmada: POST /api/orders com { gameId }; consulta por `GET /api/orders/{orderId}` já aprovada, com autorização por proprietário.
4. Ao consumir `PaymentProcessedEvent`, correlacionar `OrderId` e validar transição. `Approved` concede jogo uma única vez; `Rejected` não concede.
5. Duplicatas, mensagens fora de ordem e falhas transitórias não concedem jogos indevidamente.
6. Consulta de pedido e biblioteca demonstra consistência eventual; testar aprovação e rejeição ponta a ponta.

## Integrações
Publica `order-placed-v1.md`, consome `payment-processed-v1.md`; não lê banco do UsersAPI nem do PaymentsAPI.

## Decisões confirmadas e detalhamento técnico
Confirmado em 30/10/2026: POST /api/orders com { gameId }, 202 { orderId, status }; nova tentativa após Rejected com novo pedido; exclusão lógica preservando biblioteca/pedidos; preço atual na biblioteca e histórico no pedido. Identidade pelo JWT, snapshot BRL e proteção contra duplicidade permanecem obrigatórios.

## Baseline obrigatória para implementação

As decisões dos ADR-P2-001 a ADR-P2-007 estão **Aceitas**. Implementar com PostgreSQL e Docker Desktop Kubernetes; contratos V1 conforme `docs/contracts/`.
