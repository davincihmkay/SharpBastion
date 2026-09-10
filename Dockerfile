FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SharpBastion.csproj", "./"]
RUN dotnet restore "SharpBastion.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "./SharpBastion.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SharpBastion.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SharpBastion.dll"]

#FROM debian:bookworm-slim
#RUN apt-get update && apt-get install -y \
#    curl \
#    ca-certificates \
#    bash \
#    --no-install-recommends && \
#    rm -rf /var/lib/apt/lists/*
#
## Create a non-root user
#RUN useradd -m -s /bin/bash claudeuser
#
## Install Claude Code as root to a system-wide location
#RUN curl -fsSL https://claude.ai/install.sh | CLAUDE_INSTALL_DIR=/usr/local/bin bash \
#    || curl -fsSL https://claude.ai/install.sh | bash
#
## If the installer put it in root's home, copy it to a known location
#RUN find /root /home -name "claude" -type f 2>/dev/null | head -1 | xargs -I{} cp {} /usr/local/bin/claude || true
#RUN chmod +x /usr/local/bin/claude 2>/dev/null || true
#
#USER claudeuser
#WORKDIR /workspace
#
#ENTRYPOINT ["/usr/local/bin/claude"]