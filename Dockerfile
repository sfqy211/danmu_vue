# Stage 1: Build Backend (.NET)
# Use DaoCloud mirror for China network
FROM m.daocloud.io/mcr.microsoft.com/dotnet/sdk:9.0 AS backend
WORKDIR /source

# Copy backend project files
COPY server_net/ .

# Restore and Publish
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
# Use DaoCloud mirror for China network
FROM m.daocloud.io/mcr.microsoft.com/dotnet/aspnet:9.0
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone
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
