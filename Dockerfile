# Stage 1: Build Backend (.NET)
# Use DaoCloud mirror for China network
FROM m.daocloud.io/mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /source

# Copy backend project files
COPY server_net/ .

# Restore and Publish
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
# Use DaoCloud mirror for China network
FROM m.daocloud.io/mcr.microsoft.com/dotnet/aspnet:10.0
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# curl 仅用于 docker-compose 的 healthcheck。aspnet:10.0 基础镜像不再自带 curl，
# 缺失时容器会一直显示 unhealthy（服务本身正常）。装完清理 apt 缓存以控制体积。
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app/server_net

# Copy backend artifacts
COPY --from=backend /app/publish .

# Create data directories to ensure permissions/existence
# The app expects ../server/data -> /app/server/data
RUN mkdir -p /app/server/data

# Expose port
ENV ASPNETCORE_HTTP_PORTS=3001
EXPOSE 3001

# Entry point
ENTRYPOINT ["dotnet", "Danmu.Server.dll"]
