# Both CLI samples in one image: Node runs the inventory checker, Python the two expense receipts demos, and Caddy
# routes each path to its app. samples/deploy/start.sh starts them all. It builds from the repository root.
FROM node:22-bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates python3 python3-venv libglib2.0-0 caddy \
    && rm -rf /var/lib/apt/lists/*

ARG VELIXO_VERSION=
RUN curl -fsSL https://cli.velixo.com/install.sh | VELIXO_INSTALL_DIR=/usr/local/bin sh -s -- $VELIXO_VERSION

WORKDIR /app
COPY samples/expensereceipts/requirements.txt expensereceipts/
RUN python3 -m venv /opt/venv && /opt/venv/bin/pip install --no-cache-dir -r expensereceipts/requirements.txt

COPY samples/inventorychecker/package.json samples/inventorychecker/server.mjs inventorychecker/
COPY samples/inventorychecker/public inventorychecker/public
COPY samples/inventorychecker/velixo inventorychecker/velixo
COPY samples/expensereceipts/app.py expensereceipts/
COPY samples/expensereceipts/expenses expensereceipts/expenses
COPY samples/expensereceipts/static expensereceipts/static
COPY samples/expensereceipts/velixo expensereceipts/velixo
COPY samples/deploy deploy

ENV VELIXO_CONFIG_DIR=/data/velixo \
    PORT=8080
EXPOSE 8080

CMD ["bash", "deploy/start.sh"]
