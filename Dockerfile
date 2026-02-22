FROM node:20-slim

WORKDIR /app

# Setup server
WORKDIR /app/server
COPY server/package*.json ./
# Install all dependencies (including devDependencies like tsx) to run typescript directly
# Add mirrors for faster installation if needed
RUN npm config set registry https://registry.npmmirror.com
RUN npm install

# Copy server source code
COPY server/ .

# Create data directory
RUN mkdir -p data/danmaku

# Expose port
EXPOSE 3001

# Start server
CMD ["npm", "start"]
