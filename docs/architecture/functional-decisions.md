# Decisões funcionais confirmadas

Todos os conjuntos abaixo foram confirmados pelo responsável nesta conversa em 30/10/2026. Complementam os ADRs aceitos, sem alterar os contratos V1.

| ID | Decisão | Consequência e aceite |
|---|---|---|
| DF-01 | Iniciar sem importar dados da Fase 1 | Não criar pagamentos fictícios para bibliotecas legadas; origem intacta |
| DF-02 | POST /api/orders, entrada { gameId }, resposta 202 { orderId, status } | UserId vem do JWT; preço vem do catálogo; status inicial Pending; consulta GET /api/orders/{orderId} autorizada |
| DF-03 | Nova tentativa após Rejected cria outro pedido | Preservar histórico; bloquear jogo já adquirido ou pedido Pending para o mesmo usuário/jogo |
| DF-04 | Exclusão lógica de jogos | Impedir novas compras; preservar biblioteca e pedidos anteriores, inclusive sua conclusão após aprovação |
| DF-05 | Biblioteca exibe preço atual; pedido expõe preço histórico | Alterações de preço não mudam o snapshot do pedido |
| DF-06 | E-mail normalizado com Trim + ToLowerInvariant | Aplicar no cadastro/login; unicidade PostgreSQL, inclusive sob concorrência |
| DF-07 | Preservar administração de usuários e bootstrap; promoções fora do mínimo | Listar, consultar e alterar papéis protegidos por Admin; bootstrap seguro e idempotente |
| DF-08 | Migrations por comando/job explícito antes das APIs | Sem migrations automáticas no startup; operação independente por serviço |
| DF-09 | Notificação lógica durável única e emissão recuperável | Unicidade por tipo/usuário para boas-vindas e tipo/pedido para confirmação; log físico pode repetir na janela entre emissão e confirmação |

## Garantia observável de notificações

Não existe transação atômica entre PostgreSQL e console. Não prometer exactly-once. Testar unicidade lógica, reentrega comum sem repetição e recuperação após falha entre log e confirmação, admitindo repetição física nessa janela. Rejected nunca produz confirmação de compra.

## Limites da autorização

Execução incremental e commits locais separados autorizados. Remotos foram criados e clonados pelo responsável. Não fazer push ou publicar imagens sem autorização. Novas decisões funcionais relevantes devem ser discutidas diretamente com ele.
