# Preparação e implantação

O serviço possui Dockerfile multi-stage e Compose local conectado à infraestrutura G1. Consulte [operação](docs/operations.md) para configuração, migration explícita e execução; [README](README.md) para estado técnico e testes. Compose completo pertence ao G6 e manifests /k8s ao G7, usando Docker Desktop Kubernetes.

Não versionar .env, AGENTS.md, specs/, .codex/ ou docs/course/. Cada serviço mantém histórico Git independente. Não publicar imagens nem fazer push automaticamente.
