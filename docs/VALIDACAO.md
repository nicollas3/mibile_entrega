# Evidências de validação

Validação local em **30/10/2026**, Linux x64, PostgreSQL real, SDK .NET 10.0.403, JDK 21.0.13 e Android API 36. Resultados se referem à máquina desta tarefa, não a uma futura publicação de ambiente ou execução de CI no GitHub.

| Verificação | Resultado |
|---|---|
| Restore servidor com `--locked-mode`, inclusive após builds Android | Passou |
| Restore `Brasa.Mobile.slnx` com `--locked-mode` no Linux | Passou |
| `dotnet build Brasa.slnx -c Release --no-restore` | Passou, zero erros e zero avisos |
| `dotnet test tests/Brasa.Tests -c Release --no-build` | **24 aprovados, 0 falhas, 0 ignorados** |
| Migrations em PostgreSQL | `Initial` e `ConfirmHandover` aplicadas; nenhum modelo pendente de migration |
| MAUI Android, targets x64 e ARM64 | Compilados, zero erros e zero avisos |
| APK final | Pacote Debug com `arm64-v8a` e `x86_64`; assinatura de depuração validada |
| `dotnet publish` da API e web | Passou; ambos os pacotes foram iniciados localmente |
| Pacote Release da API | `/health`, OpenAPI, Swagger UI e categorias responderam HTTP 200 |
| OpenAPI | 56 caminhos; autenticação bearer descrita e aplicada aos endpoints protegidos |
| Chromium: cliente e restaurante | Login, busca, adicionais/carrinho, checkout, aceite, preparo e pronto para retirada passaram |
| Chromium: atualização SignalR | O cliente exibiu a mudança da loja automaticamente, sem recarga manual |
| Chromium: administração | Contas e relatório financeiro carregaram sem erro de interface |
| Chromium: sessão e responsividade | Sessão preservada após recarga; tela de pedido em 390 px sem rolagem horizontal; nenhum erro JavaScript na rodada final |
| Compose: configuração e PostgreSQL | Configuração válida; serviço iniciado em projeto/volume de teste, healthcheck saudável e `SELECT 1` retornou 1 |
| Imagens Docker de API/web | **Bloqueadas antes da compilação**, por saída negada para `centralus.data.mcr.microsoft.com`; não marcadas como aprovadas |
| Android em emulador/aparelho | **Não executado**; GPS, armazenamento seguro e navegação nativa precisam de validação em dispositivo |
| iOS | Fontes e configuração incluídas; **não compilado nem executado** neste Linux |
| Pagamentos, email, geocodificação, ETA de rota, push reais | **Não configurados ou validados**; desenvolvimento usa substitutos explicitamente identificados |
| Produção / publicação / CI remota | **Não executadas** |

## O que os testes exercitam

Os testes de domínio verificam cálculo e equilíbrio das parcelas, arredondamento e rejeição de valores inválidos, transições de pedido e sequência de entrega.

A integração usa um banco PostgreSQL exclusivo de cada rodada, migrations reais, Identity e requisições HTTP ao aplicativo. Inclui isolamento de lojas, entregadores e endereços; permissões de funcionário/suporte; proteção de endpoints anônimos; carrinho de um único restaurante; validação; checkout concorrente, consulta por chave e conflito de repetição; snapshot de preços; pagamento recusado; assinatura e duplicidade de webhook; reembolso parcial idempotente; retirada confirmada pela loja; entrega completa, avaliação e nota no histórico do entregador; rejeição de GPS após conclusão; rotação/reutilização/revogação de tokens; recuperação de senha; bloqueio; loja indisponível após aprovação; prazo de pagamento; relatórios com fuso brasileiro; filtros opcionais do cliente compartilhado; limite de autenticação; autorização de grupos SignalR.

Os 24 testes executaram e terminaram. O `.trx` da última rodada fica em `tests/Brasa.Tests/TestResults/brasa.trx`, ignorado pelo Git. A CI só rodará quando o código for enviado ao GitHub.

## Artefatos e limites

O APK de desenvolvimento fica em `artifacts/Brasa-dev.apk`, com checksum em `artifacts/Brasa-dev.json`. Contém as duas ABIs; os nomes de targets de build não significam que cada pacote foi limitado a uma única ABI. O endereço padrão é o host do emulador (`10.0.2.2:5070`). O certificado de depuração não serve para publicação em loja. O verificador confirmou esquemas v1/v2/v3; emitiu avisos sobre metadados `META-INF` fora da cobertura do esquema JAR v1. Não foram removidos metadados nem desabilitada a verificação para ocultar esses avisos.

Os workloads foram instalados offline após verificação individual das assinaturas NuGet. O verificador aceitou os pacotes, com avisos de indisponibilidade da consulta de revogação na rede. O SDK .NET e as ferramentas Android foram conferidos com os checksums dos fornecedores. Nenhuma validação TLS foi desativada.

O PostgreSQL usado para o smoke test Compose teve porta e volume próprios e foi removido após a consulta, sem alterar o banco local da aplicação. O bloqueio das imagens Microsoft está descrito em `OPERACAO.md`, junto ao destino de rede necessário. Não houve alteração da política de rede publicada.

As capacidades móveis precisam de testes em dispositivo antes de distribuição. O provedor financeiro não envia dinheiro nem cobra cartões. O servidor recusa iniciar em produção enquanto estiver configurado somente com integrações demonstrativas.
