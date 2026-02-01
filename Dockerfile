FROM node:20-slim

WORKDIR /app

# Copy root package files and install dependencies
COPY package*.json ./
# Install build dependencies if needed (usually not for glibc/node:20)
# RUN apt-get update && apt-get install -y python3 make g++
RUN npm install

# Copy source code
COPY . .

# Build frontend
RUN npm run build

# Setup server
WORKDIR /app/server
COPY server/package*.json ./
# Install all dependencies (including devDependencies like tsx) to run typescript directly
RUN npm install

# Create data directory
RUN mkdir -p data/danmaku

# Expose port
EXPOSE 3001

# Start server
CMD ["npm", "start"]
