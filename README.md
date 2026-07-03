# The Realm Project

Launcher Windows privado para Minecraft NeoForge, criado para `mirai`.

## Estrutura

- `src/TheRealmProject.Launcher`: aplicativo WPF `.exe`.
- `src/TheRealmProject.Core`: serviços de NeoForge, Java, GitHub Releases, modpack, perfil offline e launch.
- `src/TheRealmProject.OfflineCosmeticsMod`: mod cliente NeoForge que lê o perfil de cosméticos offline gerado pelo launcher.

O launcher usa a instância isolada:

```text
%AppData%\The Realm Project\instances\default
```

Arquivos de configuração e estado ficam em:

```text
%AppData%\The Realm Project\config
%AppData%\The Realm Project\assets\cosmetics
```

## Build do launcher

Instale o .NET 8 SDK com workload Desktop para Windows e execute:

```powershell
dotnet restore
dotnet build .\TheRealmProject.sln -c Release
```

Para gerar `.exe` self-contained:

```powershell
dotnet publish .\src\TheRealmProject.Launcher\TheRealmProject.Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Configurar GitHub Releases do modpack

Na primeira execução o launcher cria:

```text
%AppData%\The Realm Project\config\appsettings.json
```

Edite:

```json
{
  "GitHubOwner": "seu-usuario",
  "GitHubRepository": "seu-repo-de-modpack",
  "GitHubAssetNameContains": "realm",
  "MaximumRamMb": 4096,
  "MinimumRamMb": 1024,
  "IncludeBetaNeoForgeVersions": true
}
```

Use um repositório público para o modpack. Assim o `.exe` funciona em outros computadores sem configurar token, variável de ambiente ou credenciais.

## Publicar o modpack

Publique o modpack como asset `.zip` ou `.rar` na release mais recente. O pacote deve conter na raiz, ou dentro de uma única pasta raiz:

```text
mods/
config/
resourcepacks/
shaderpacks/
options.txt
```

Ao atualizar, o launcher substitui apenas esses itens e preserva `saves/`, `screenshots/`, `logs/` e os arquivos internos do launcher.

## NeoForge e Java

O launcher busca installers em:

```text
https://maven.neoforged.net/releases/net/neoforged/neoforge/
```

Ele detecta Java local e baixa JDK oficial do Eclipse Adoptium para `%AppData%\The Realm Project\runtime` quando necessário.

Regra implementada:

- Minecraft `1.20.2` até `1.20.4`: Java 17.
- Minecraft `1.20.5+`: Java 21.

## Modo offline

O launcher cria sessão offline local por username. Esse modo é destinado a desenvolvimento local e servidores com:

```properties
online_mode=false
```

Não há login Microsoft no v1.

## Customização de skin e capa

A aba **Customização** salva:

```text
%AppData%\The Realm Project\assets\cosmetics\profile.json
%AppData%\The Realm Project\assets\cosmetics\skin.png
%AppData%\The Realm Project\assets\cosmetics\cape.png
```

O projeto `TheRealmProject.OfflineCosmeticsMod` lê esse perfil. Inclua o jar compilado no asset do modpack em `mods/`. Em ambiente de desenvolvimento, se o jar existir em `src/TheRealmProject.OfflineCosmeticsMod/build/libs`, o launcher tenta copiá-lo automaticamente para a instância antes de jogar.

Observação: a ponte do mod está criada, mas o hook final de renderização de skin/capa deve ser fixado para a versão exata Minecraft/NeoForge do seu modpack, porque as APIs internas de skin do cliente mudam entre versões.

## Build do mod de cosméticos

Instale Gradle 8.8+ ou adicione um Gradle Wrapper ao projeto do mod. Depois:

```powershell
cd .\src\TheRealmProject.OfflineCosmeticsMod
gradle build
```

O jar final ficará em:

```text
src\TheRealmProject.OfflineCosmeticsMod\build\libs
```

## Personalizar miniatura/ícone do .exe

1. Crie um arquivo `.ico`, por exemplo `src/TheRealmProject.Launcher\Assets\app.ico`.
2. Adicione ao `.csproj` do launcher:

```xml
<ApplicationIcon>Assets\app.ico</ApplicationIcon>
```

3. Publique novamente com `dotnet publish`.

## Verificação

Para validar o launcher e os testes:

```powershell
dotnet restore
dotnet build .\TheRealmProject.sln -c Release
dotnet test .\TheRealmProject.sln -c Release
```
