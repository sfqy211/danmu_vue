FROM node:20-slim

WORKDIR /app

# Install build dependencies for native modules (like sqlite3)
RUN apt-get update && apt-get install -y python3 make g++ && rm -rf /var/lib/apt/lists/*

# Setup server
WORKDIR /app/server
COPY server/package*.json ./

# Install all dependencies (including devDependencies like tsx) to run typescript directly
# Add mirrors for faster installation if needed
RUN npm config set registry https://registry.npmmirror.com

# Set sqlite3 binary mirror to avoid build from source fallback if possible
# Note: npm config set will fail for unknown options in newer npm versions, so we use ENV instead
ENV sqlite3_binary_host_mirror=https://npmmirror.com/mirrors/sqlite3/
ENV sqlite3_binary_site=https://npmmirror.com/mirrors/sqlite3/

RUN npm install

# Copy server source code
COPY server/ .

# Create data directory
RUN mkdir -p data/danmaku

# Expose port
EXPOSE 3001

# Start server
CMD ["npm", "start"]
