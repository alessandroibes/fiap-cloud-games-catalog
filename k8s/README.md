# Kubernetes — catalog

Manifestos para o namespace `fcg-phase2` no contexto `docker-desktop`. G7 validado em 03/11/2026: Job de migration concluído, Deployment pronto, fluxo integrado e persistência após substituição dos pods. Evidências em [validação G7](../docs/quality/g7-validation.md).

## Arquivos e operação

- `configmap.yaml`: configuração não sensível e DNS interno `postgres`/`rabbitmq`.
- `deployment.yaml`: uma réplica, imagem local `fiap-cloud-games-catalog:g7`, estratégia Recreate, UID/GID 1654, sem token de ServiceAccount, requests/limits e probes HTTP.
- `service.yaml`: ClusterIP na porta 8080; não expõe endpoints à rede externa.
- `migration-job.yaml`: Job explícito com `--migrate`, sem retry automático, limite de 180 segundos. Não há migrations no startup da API.

O Secret `catalog-secret` é provisionado localmente pelo script Initialize-Kubernetes do repositório irmão de infraestrutura. Contém apenas configuração sensível necessária a este serviço: conexão do próprio banco e senha RabbitMQ; Users/Catalog também recebem chave JWT, Users recebe configuração de bootstrap. Os arquivos não contêm valores secretos. Não usar `kubectl get secret -o yaml` em evidências compartilhadas.

Execute, a partir do clone irmão `fiap-cloud-games-infrastructure`:

```powershell
./scripts/Start-Kubernetes.ps1
./scripts/Validate-G7.ps1 -SkipStart
```

A infraestrutura aguarda PostgreSQL/RabbitMQ, executa migrations e bootstrap e somente então aplica os Deployments. Não executar `kubectl apply -f k8s/` indiscriminadamente, pois isso inicia Jobs e API juntos, sem garantir a ordem. Para atualizar, use o script de implantação: pausa as APIs durante DDL e substitui somente Jobs de execução anteriores, preservando PVCs.

Para inspeção deste serviço:

```powershell
kubectl --context docker-desktop -n fcg-phase2 get deployment,service catalog
kubectl --context docker-desktop -n fcg-phase2 logs deployment/catalog --tail=30
kubectl --context docker-desktop -n fcg-phase2 port-forward service/catalog 5182:8080 --address 127.0.0.1
```

`/health/live` verifica processo e `/health/ready` verifica banco/schema. Não afirmam disponibilidade do broker; fluxo assíncrono é verificado pelo teste integrado. Imagens devem existir no armazenamento acessível ao Kubernetes do Docker Desktop; não há publicação em registry externo. Ao reconstruir a tag local, use Start-Kubernetes para a implantação controlada.

Detalhes de configuração, PVCs, acesso e evidências transversais: `docs/implementation/kubernetes-desktop.md` e `docs/quality/g7-validation.md` no repositório de infraestrutura. Falhas, concorrência e duplicação já possuem testes do serviço; o registro G7 distingue a execução integrada dos testes históricos do serviço.