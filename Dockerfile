FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY *.slnx ./
COPY src/Barbearia.Domain/*.csproj src/Barbearia.Domain/
COPY src/Barbearia.Application/*.csproj src/Barbearia.Application/
COPY src/Barbearia.Infrastructure/*.csproj src/Barbearia.Infrastructure/
COPY src/Barbearia.Api/*.csproj src/Barbearia.Api/
RUN dotnet restore src/Barbearia.Api/Barbearia.Api.csproj

COPY . .
RUN dotnet publish src/Barbearia.Api/Barbearia.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tzdata: sem ele o TimeZoneInfo nao acha America/Sao_Paulo.
# libgssapi-krb5-2: o Npgsql sonda GSS ao conectar e reclama se faltar.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# O Render injeta PORT; o Program.cs le essa variavel.
ENV ASPNETCORE_ENVIRONMENT=Production

# Desliga o recarregamento automatico do appsettings.json. Por padrao o host abre
# um FileSystemWatcher por arquivo de configuracao, e no Linux cada um gasta uma
# instancia de inotify. O limite (fs.inotify.max_user_instances, 128) e por UID no
# kernel do host, compartilhado com os outros containers que rodam como root na
# mesma maquina do Render: quando ele esgota, o WebApplication.CreateBuilder estoura
# IOException e a app nem chega a subir. Vigiar o arquivo aqui nao serve para nada —
# a imagem e imutavel e a configuracao de verdade vem por variavel de ambiente.
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

EXPOSE 10000

ENTRYPOINT ["dotnet", "Barbearia.Api.dll"]
