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
EXPOSE 10000

ENTRYPOINT ["dotnet", "Barbearia.Api.dll"]
