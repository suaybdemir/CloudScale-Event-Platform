FROM node:20-alpine

WORKDIR /app

# Install dependencies first (caching)
COPY package.json package-lock.json ./
RUN npm ci

# Copy source
COPY . .

# Expose Vite port
EXPOSE 5173

# Start in dev mode allowing external access
CMD ["npm", "run", "dev", "--", "--host"]
