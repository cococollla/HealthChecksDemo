FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["src/ServiceB/ServiceB.csproj", "src/ServiceB/"]
RUN dotnet restore "src/ServiceB/ServiceB.csproj"

COPY . .
RUN dotnet publish "src/ServiceB/ServiceB.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl jq \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY ["src/ServiceB/wait-for-service-a.sh", "/app/wait-for-service-a.sh"]
RUN chmod 755 /app/wait-for-service-a.sh
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["/app/wait-for-service-a.sh"]
