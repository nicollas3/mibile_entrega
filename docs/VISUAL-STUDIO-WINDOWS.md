# Abrir no Visual Studio e gerar o APK

O Git contém o código-fonte, projetos, migrations, lockfiles e configurações de exemplo. Segredos, caches e binários são gerados no computador; não precisam ser versionados.

## 1. Preparar o Windows

No Visual Studio Installer, instale uma versão do Visual Studio compatível com o SDK .NET 10.0.403 e com soluções `.slnx`. Selecione **ASP.NET e desenvolvimento Web** e **Desenvolvimento de interface do usuário de aplicativo multiplataforma .NET (MAUI)**, incluindo Android SDK e JDK. Instale também Docker Desktop com WSL2 e containers Linux, ou um PostgreSQL local.

Clone pelo Visual Studio (Git → Clonar repositório):

```text
https://github.com/nicollas3/mibile_entrega.git
```

Ou no terminal:

```powershell
git clone https://github.com/nicollas3/mibile_entrega.git
cd mibile_entrega
```

## 2. Configurar o banco e os segredos locais

Abra o PowerShell na pasta do projeto. Com Docker Desktop iniciado:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1
```

O parâmetro de execução vale apenas para esse processo, sem mudar a política permanente do Windows. O script preserva `.env` existente, gera segredos locais quando necessário, configura ASP.NET User Secrets, restaura dependências e inicia somente PostgreSQL. Não imprime valores de segredos. As migrations e o seed fictício são executados quando a API inicia.

Se já usa PostgreSQL próprio, configure `ConnectionStrings__Database` no `.env` antes e acrescente `-SkipDatabase`. O Visual Studio não carrega `.env` automaticamente: é o script que transfere a configuração para User Secrets. Execute-o novamente após alterar `.env`.

## 3. Executar API e painéis

Abra **Brasa.slnx**. Nas propriedades da solução, configure vários projetos de inicialização:

- `Brasa.Api`: Iniciar, perfil `Brasa.Api`.
- `Brasa.Web`: Iniciar, perfil `Brasa.Web`.
- Bibliotecas e testes: nenhum comando de inicialização.

Compile em **Debug** e pressione F5. Os painéis abrem na porta **5080**. A API atende na porta **5070**, e a documentação está no caminho `/docs`. A configuração é de desenvolvimento e o pagamento é simulado.

Conta local: `cliente@brasa.local`; restaurante: `loja@brasa.local`; administração: `admin@brasa.local`. Senha padrão local: `BrasaLocal!2026`, salvo se você alterou `Demo__Password` antes da criação das contas.

## 4. Executar o app no emulador

Mantenha API e banco funcionando. Abra **Brasa.Mobile.slnx** em outra instância do Visual Studio. Selecione **Brasa.Mobile** como projeto de inicialização, configuração **Debug** e um emulador Android. Pressione F5.

O padrão `http://10.0.2.2:5070` acessa o notebook pelo emulador Android. O endereço `localhost` no celular/emulador não aponta para o notebook.

## 5. Usar um Android físico no mesmo Wi-Fi

Execute `ipconfig` no Windows e localize o IPv4 da conexão Wi-Fi. Exemplo fictício: `192.168.1.10`.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1 -NotebookIp 192.168.1.10
```

Isso inclui o IP nos hosts permitidos da API e cria `Directory.Build.local.props`, ignorado pelo Git, com a URL do app para sua rede. Um arquivo local já existente é preservado: confira o endereço nele. Reinicie a API e recompile o aplicativo se o IP mudar.

O perfil da API escuta na rede. No Firewall do Windows, permita entrada TCP na porta **5070**, somente no perfil **Privado**, preferencialmente limitada à sub-rede local. Não libere para rede pública nem encaminhe a porta no roteador. Confira no navegador do celular `http://IP-DO-NOTEBOOK:5070/health`: deve aparecer `status: ok`.

Ative as opções de desenvolvedor e a depuração USB no Android. Conecte por USB, autorize o notebook e escolha o aparelho no Visual Studio. O cabo permite instalar/depurar; a comunicação com a API neste roteiro usa Wi-Fi. Redes de convidados podem isolar os dispositivos.

## 6. Gerar um APK para instalar manualmente

No PowerShell da pasta do projeto, com o workload MAUI instalado:

```powershell
# Emulador
powershell -ExecutionPolicy Bypass -File .\scripts\build-apk.ps1

# Celular no mesmo Wi-Fi: substitua pelo IPv4 real do notebook
powershell -ExecutionPolicy Bypass -File .\scripts\build-apk.ps1 -ApiUrl http://192.168.1.10:5070
```

O APK fica em **artifacts\Brasa-dev.apk**, assinado para depuração. Transfira por USB ao celular, abra o arquivo e autorize a instalação por essa origem quando o Android solicitar. Para reinstalar, use a mesma chave de depuração; se houver conflito de assinatura de uma instalação anterior, a desinstalação remove também os dados locais daquele aplicativo.

O APK contém ARM64 e x64. O notebook e a API devem permanecer ligados. HTTP é permitido apenas na configuração Android Debug para esse teste local. Para distribuição real, configure HTTPS, assinatura própria, provedores reais e homologação; esta versão bloqueia o servidor em produção enquanto usa apenas integrações demonstrativas.

## Verificação e limitações

Os projetos e APKs foram compilados no Linux, e o backend possui testes em PostgreSQL real. Os scripts PowerShell e o fluxo visual do Visual Studio estão fornecidos para Windows, mas não foram executados nessa plataforma durante esta tarefa. Se o Visual Studio não abrir `.slnx`, atualize-o para uma versão que suporte o SDK fixado em `global.json`.

iOS precisa de Mac/Xcode e provisionamento Apple. Este roteiro não publica código, ambiente ou aplicativos automaticamente.
