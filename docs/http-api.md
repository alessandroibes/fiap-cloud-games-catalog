# API HTTP — Catalog

JSON camelCase; GUIDs como strings; valores monetários numéricos em BRL, não negativos e com até duas casas decimais. Erros usam ProblemDetails, sem detalhes internos de persistência.

| Método e rota | Acesso | Entrada e resultado |
|---|---|---|
| GET /api/games | Público | 200, jogos ativos |
| GET /api/games/{id} | Público | 200; 404 se inexistente ou desativado |
| POST /api/games | Admin | { name, price }; 201 com jogo e Location; 400 inválido |
| PUT /api/games/{id} | Admin | { name, price }; 204; 400 inválido; 404 inexistente/desativado |
| DELETE /api/games/{id} | Admin | 204; desativa sem excluir dados; repetir é idempotente; 404 inexistente |
| POST /api/orders | Autenticado | { gameId }; 202 { orderId, status: "Pending" } e Location; 400 GUID vazio; 404 jogo indisponível; 409 adquirido/pendente |
| GET /api/orders/{orderId} | Proprietário | 200 com id, userId, gameId, price histórico, currency, status, paymentId e createdAt; 404 inexistente ou de outro usuário |
| GET /api/library | Autenticado | 200, biblioteca própria: gameId, name, price atual, isActive, orderId, acquiredAt |
| GET /health/live | Público | 200 com status Healthy |
| GET /health/ready | Público | 200 banco/schema disponíveis; 503 indisponível ou migration pendente |

Nome e preço são obrigatórios; preço zero é permitido, como na Fase 1. Precisão persistida numeric(18,2). Não há reativação nem promoções no mínimo aprovado. Exclusão lógica preserva bibliotecas e conclusão de pedidos anteriores; novas compras de jogo desativado retornam 404.

Enviar `Authorization: Bearer <token>` obtido da UsersAPI. Catalog valida HS256, assinatura, issuer, audience, expiração sem tolerância e sub GUID não vazio. Escritas em jogos exigem papel Admin. A identidade de compra vem exclusivamente do sub; preço e status enviados pelo comprador não controlam o pedido. Falta de autenticação retorna 401 e falta do papel exigido retorna 403. Admin não recebe acesso excepcional a pedidos ou bibliotecas alheios.

O preço é capturado ao criar o pedido. Mudar o preço atual não altera pedido nem pagamento. A biblioteca inclui jogos desativados, mostrando seu preço atual. HTTP 202 confirma pedido + Outbox persistidos, não pagamento aprovado. Consulte o pedido até obter Approved ou Rejected; durante falhas ele pode permanecer Pending. Após Rejected, outro POST cria novo pedido; histórico anterior é preservado. Não há cancelamento neste escopo.

Não há endpoint HTTP para aprovar pagamento ou conceder jogo. A concessão depende de [PaymentProcessedEvent V1](contracts/v1/payment-processed-v1.md), validado contra o pedido. [OrderPlacedEvent V1](contracts/v1/order-placed-v1.md) é publicado de forma assíncrona pela Outbox.
