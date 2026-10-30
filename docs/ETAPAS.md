# Etapas implementadas e como revisar

O ambiente inicial não tinha arquivos, commits ou SDK .NET. Foi utilizado o checkout existente. O trabalho foi organizado nas etapas abaixo; os comandos partem da raiz do repositório e pressupõem o PostgreSQL local em execução. Todos os dados demonstrativos são fictícios.

| Etapa | Arquivos e comportamento entregues | Verificação / comando |
|---|---|---|
| 1. Inspeção e arquitetura | `global.json`, `Directory.Packages.props`, `ARQUITETURA.md`; .NET 10 LTS e monólito modular | Versões consultadas nas fontes oficiais; pré-requisitos no README |
| 2. Solução e persistência | Soluções `.slnx`, projetos, `compose.yaml`, `.env.example`, `AppDb`, migrations `Initial` e `ConfirmHandover` | `dotnet build Brasa.slnx`; `dotnet ef database update --project src/Brasa.Api` |
| 3. Autenticação e autorização | `AuthService`, `Access`, Identity, sessões e rotação de refresh tokens, permissões por recurso e estabelecimento | `dotnet test tests/Brasa.Tests --filter FullyQualifiedName~FlowTests` |
| 4. Restaurantes e cardápios | `CatalogService`, endpoints de busca/categorias/cardápios/equipe, preços, adicionais, fotos por URL e alérgenos | Busca/menu HTTP e edição exercitados em PostgreSQL; painéis ligados à API |
| 5. Carrinho, pedido e pagamento | `CartService`, `OrderService`, snapshots, cupons, cálculos, idempotência, provider financeiro de demonstração e webhook HMAC | Checkout concorrente, repetição, recusa e reembolso testados; nenhuma cobrança real |
| 6. Restaurante e entregador | `DeliveryService`, ofertas, aceite único, etapas, confirmação de retirada pela loja, aprovação, histórico, valores previstos e avaliações | Fluxo de entrega completo por HTTP e bloqueio de acesso de outro entregador |
| 7. Tempo real e localização | `OrderHub`, worker, outbox, notificações internas, posição do dispositivo e interfaces externas | Autorização SignalR testada; atualização automática comprovada no Chromium; GPS físico e push nativo não executados |
| 8. Painéis e relatórios | `Brasa.Web`, `Brasa.Mobile`, administração, loja, cliente e suporte; relatórios e auditoria | Build web, pacotes Release e fluxo real no navegador; build MAUI Android |
| 9. Testes e demonstração | `Brasa.Tests`, `DemoSeed`, CI, README, documentação de operação | **24 testes passaram**, sem falhas ou testes ignorados; PostgreSQL real |
| 10. Revisão e execução | `scripts/init-dev.sh`, `scripts/dev.sh`, `scripts/check.sh`, Dockerfiles, lockfiles por arquitetura, APK Debug e documentação | `bash scripts/check.sh`; builds Android x64/ARM64; `dotnet publish` API/web; pendências externas listadas em `VALIDACAO.md` |

## Configurações externas por etapa

As etapas locais usam `.env` com segredos aleatórios de desenvolvimento, PostgreSQL e dados opcionais de demonstração. Não foi preciso obter uma credencial real de pagamento ou mapas. O README contém os comandos exatos de execução e compilação, inclusive parâmetros de Android SDK/JDK.

Para operar com dados reais, faltam provedores e credenciais de pagamento, email, geocodificação/ETA e FCM/APNs, além de assinatura de aplicativos e validação em aparelhos. iOS requer macOS/Xcode. Os Dockerfiles estão entregues, mas a criação de suas imagens foi bloqueada pela rede na distribuição regional da Microsoft. Essas condições não foram marcadas como concluídas ou testadas.

Não houve push ao GitHub, envio a lojas de aplicativos ou publicação do ambiente.
