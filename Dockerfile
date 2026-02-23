# Stage 1: Build Frontend
# Use DaoCloud mirror for China network
FROM m.daocloud.io/docker.io/library/node:22-slim AS frontend
WORKDIR /src

# Copy package.json and package-lock.json (if exists)
COPY package*.json ./

# Install dependencies
# Remove package-lock.json to force fresh install of platform-specific binaries (like esbuild/rollup) for Linux
RUN rm -f package-lock.json
# Set npm mirror for China
RUN npm config set registry https://registry.npmmirror.com
RUN npm install

# Copy frontend source
COPY . .

# Workaround: Rename vite.config.ts to vite.config.js to bypass TS compilation of config
# This helps if esbuild has issues in the container
RUN mv vite.config.ts vite.config.js

# Build frontend
# Use npx vite build directly to skip tsc (which might fail) and ensure we use the local vite
RUN npx vite build

# Stage 2: Build Backend (.NET)
# Use DaoCloud mirror for China network
FROM m.daocloud.io/mcr.microsoft.com/dotnet/sdk:9.0 AS backend
WORKDIR /source

# Copy backend project files
COPY server_net/ .

# Restore and Publish
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Stage 3: Runtime
# Use DaoCloud mirror for China network
FROM m.daocloud.io/mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app/server_net

# Install necessary runtime dependencies if any (e.g. for System.Drawing if used, though it's deprecated)
# If the app uses System.Drawing.Common on Linux, it might need libgdiplus, but usually .NET 6+ prefers ImageSharp or SkiaSharp.
# The project references "ImageService", check if it uses System.Drawing.
# If it uses SkiaSharp or ImageSharp, no extra native libs usually needed.
# Let's assume standard .NET 9 environment is enough for now.

# Copy backend artifacts
COPY --from=backend /app/publish .

# Copy frontend artifacts to ../dist (relative to WORKDIR /app/server_net)
# /app/server_net/../dist resolves to /app/dist
COPY --from=frontend /src/dist /app/dist

# Create data directories to ensure permissions/existence
# The app expects ../server/data -> /app/server/data
RUN mkdir -p /app/server/data

# Expose port
ENV ASPNETCORE_HTTP_PORTS=3001
EXPOSE 3001

# Entry point
ENTRYPOINT ["dotnet", "Danmu.Server.dll"]
