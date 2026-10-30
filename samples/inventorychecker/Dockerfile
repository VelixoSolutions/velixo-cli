FROM node:22-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ARG VELIXO_VERSION=
RUN curl -fsSL https://cli.velixo.com/install.sh | VELIXO_INSTALL_DIR=/usr/local/bin sh -s -- $VELIXO_VERSION

WORKDIR /app
COPY package.json server.mjs ./
COPY public ./public
COPY velixo ./velixo

ENV HOST=0.0.0.0 \
    PORT=8080 \
    VELIXO_CONFIG_DIR=/data/velixo \
    KIOSK_IMAGE_CACHE=/data/images
EXPOSE 8080

CMD ["node", "server.mjs"]
