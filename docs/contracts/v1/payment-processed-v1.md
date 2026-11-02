# PaymentProcessedEvent — contrato V1 aceito

- **Produtor:** PaymentsAPI; **consumidores:** CatalogAPI e NotificationsAPI.
- **Namespace C#:** `FiapCloudGames.Contracts.V1`.
- **Fonte canônica após criação dos repositórios:** `fiap-cloud-games-infrastructure/docs/contracts/v1/`.

| Campo | Tipo C# | Obrigatório |
|---|---|---|
| `EventId` | `Guid` | Sim |
| `OccurredAt` | `DateTimeOffset UTC` | Sim |
| `OrderId` | `Guid` | Sim |
| `PaymentId` | `Guid` | Sim |
| `UserId` | `Guid` | Sim |
| `GameId` | `Guid` | Sim |
| `Status` | `string: Approved ou Rejected` | Sim |
| `Price` | `decimal` | Sim |
| `Currency` | `string: BRL` | Sim |

## JSON ilustrativo
```json
{
  "eventId": "11111111-1111-4111-8111-111111111111",
  "occurredAt": "2026-10-29T18:00:00Z",
  "orderId": "11111111-1111-4111-8111-111111111111",
  "paymentId": "11111111-1111-4111-8111-111111111111",
  "userId": "11111111-1111-4111-8111-111111111111",
  "gameId": "11111111-1111-4111-8111-111111111111",
  "status": "Approved",
  "price": 59.9,
  "currency": "BRL"
}
```

Regras: JSON camelCase; UTC ISO 8601; preço numérico `decimal` BRL. `EventId` identifica evento lógico, `OrderId` correlaciona pedido e resultado. Testar compatibilidade de identidade MassTransit. Sem senha, hash ou JWT. Confirmação de compra somente em `Approved`.
