FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/services/CashFlow.Entries/CashFlow.Entries.csproj", "src/services/CashFlow.Entries/"]
COPY ["src/shared/CashFlow.SharedKernel/CashFlow.SharedKernel.csproj", "src/shared/CashFlow.SharedKernel/"]
COPY ["src/shared/CashFlow.EventBus/CashFlow.EventBus.csproj", "src/shared/CashFlow.EventBus/"]

RUN dotnet restore "src/services/CashFlow.Entries/CashFlow.Entries.csproj"

COPY . .
RUN dotnet build "src/services/CashFlow.Entries/CashFlow.Entries.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/services/CashFlow.Entries/CashFlow.Entries.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Install wget for healthcheck (must run as root)
RUN apt-get update && apt-get install -y --no-install-recommends wget && rm -rf /var/lib/apt/lists/*

# Non-root user for security
RUN addgroup --system --gid 1001 appgroup && adduser --system --uid 1001 --ingroup appgroup appuser
USER appuser

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CashFlow.Entries.dll"]
