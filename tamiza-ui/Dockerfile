# syntax=docker/dockerfile:1

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist/tamiza-ui/browser /usr/share/nginx/html
# The build's third-party licence texts (Leaflet's BSD-2-Clause asks for its notice to travel with the files).
COPY --from=build /app/dist/tamiza-ui/3rdpartylicenses.txt /usr/share/nginx/html/3rdpartylicenses.txt
EXPOSE 8080
