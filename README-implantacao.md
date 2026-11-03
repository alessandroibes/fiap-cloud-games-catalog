# Preparação e implantação

Dockerfile multi-stage e execução local descritos no [README](README.md) e em [operação](docs/operations.md). A orquestração completa do Compose foi validada no G6 no repositório de infraestrutura.

Os [manifestos Kubernetes](k8s/README.md) incluem Deployment, Service, ConfigMap e Job de migration, com referências a Secret externo. A implantação deve seguir os scripts de infraestrutura para respeitar a ordem de migrations/bootstrap. G7 concluído; [evidências locais](docs/quality/g7-validation.md) e registro transversal na infraestrutura.

Não versionar .env, AGENTS.md, specs/, .codex/ ou docs/course/. Histórico independente, sem push ou publicação automática de imagens.