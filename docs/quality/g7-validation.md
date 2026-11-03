# G7 — Kubernetes Docker Desktop

Validado em 03/11/2026 no contexto docker-desktop, namespace fcg-phase2, Kubernetes v1.36.1. Manifestos em /k8s; credenciais externas em Secret, sem valores reais versionados.

- Build Docker multi-stage com restore locked/publish .NET 8 concluído; nenhuma mudança no código de negócio.
- Manifestos aceitos pelo API server em dry-run.
- Job de migration executado sobre banco novo e reaplicado; uma linha no histórico EF após a segunda execução.
- Deployment com uma réplica pronta, Service ClusterIP, DNS postgres/rabbitmq, probes HTTP e UID efetivo 1654.
- Imagem local identificada por SHA256 no script central; sem publicação externa.
- Fluxo integrado no cluster: cadastro/login, boas-vindas, Pending sem concessão com Payments parado, aprovação após retomada, rejeição sem concessão/confirmação, nova tentativa aprovada.
- Pods substituídos e dados preservados nos PVCs: pedidos, duas entradas na biblioteca e notificações técnicas corretas. Payments restaurado para Approved.

Validação executada por Start-Kubernetes e Validate-G7 da infraestrutura, ambos concluídos com código 0 na versão final. Não houve reexecução da suíte completa de testes do serviço; os resultados do gate funcional permanecem históricos. Evidências transversais, IDs dos pedidos, problemas corrigidos e limites em `fiap-cloud-games-infrastructure/docs/quality/g7-validation.md`.

Uma réplica local e PVC não representam alta disponibilidade. Não há garantia de exactly-once físico nos logs. Próxima etapa transversal: G8, resiliência e revisão. Fase 1 preservada; nenhum push realizado.