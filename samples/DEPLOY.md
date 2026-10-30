# Deploying the samples

Both CLI samples run in one Fly.io app, `velixo-cli-samples`. Fly's GitHub integration deploys it on every push to `main`, from `fly.toml` and `Dockerfile` at the repository root: Fly's launch looks for them only there.

| Path | What answers |
|---|---|
| `https://velixo-cli-samples.fly.dev/` | The inventory checker kiosk |
| `/acumatica/sms`, `/acumatica/telegram` | The expense receipts demo on Acumatica |
| `/sageintacct/sms`, `/sageintacct/telegram` | The expense receipts demo on Sage Intacct |

The app is one machine that never stops, with a 1 GB volume, `data`, at `/data`. The root `Dockerfile` builds Node, Python, the Velixo CLI and Caddy into one image, and the root `.dockerignore` sends only the samples to the builder. `samples/deploy/start.sh` starts the kiosk, the two expense demos and Caddy, which routes each path to its app. The three apps share the CLI's one sign-in, in `/data/velixo`. The CLI replaces its refresh token on every call and saves the new one there, so the apps stay signed in only as long as the volume survives. Deploys keep the volume.

## Set it up once

The app `velixo-cli-samples` already exists. Its volume `data` lives in `yyz`, Toronto: the first deploy creates it there from `initial_size` in `fly.toml`. An earlier, unused `data` volume in `iad` can be deleted with `fly volumes list --app velixo-cli-samples`, then `fly volumes destroy <id>`.

1. **Connect the repository.** In the Fly dashboard, launch from GitHub with `VelixoSolutions/velixo-cli` and branch `main`, region `yyz`, where the volume is. Fly reads the root `fly.toml`, so it needs no other setting. To deploy from a checkout instead: `fly deploy --app velixo-cli-samples --ha=false` at the repository root.

2. **Set the app's secrets.** In the dashboard under Secrets, or with `fly secrets set --app velixo-cli-samples NAME=value`. Each one is optional: a demo without its keys answers 503 on that path, and the rest keeps working.

   | Secret | Meaning |
   |---|---|
   | `KIOSK_WAREHOUSE` | The store's warehouse. It needs a `RESERVED` location for reservations. Without it, the kiosk only reads. |
   | `OPENROUTER_API_KEY` | The key both expense demos use to read receipts. |
   | `ACUMATICA_CONNECTION` | The CLI connection for the kiosk and the Acumatica expense demo. It defaults to `demo`. |
   | `SAGEINTACCT_CONNECTION` | The CLI connection for the Sage Intacct expense demo. It defaults to `skyline`. |
   | `ACUMATICA_URL`, `ACUMATICA_COMPANY` | The Acumatica instance and company, for the link in the reply. The address defaults to `https://demo.velixo.com`. |
   | `SAGE_EXPENSE_URL_TEMPLATE` | A link to a Sage Intacct expense report, with `{id}` in place of its key. |
   | `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` | The Twilio account both demos send from. |
   | `ACUMATICA_TWILIO_FROM_NUMBER`, `SAGEINTACCT_TWILIO_FROM_NUMBER` | Each demo's own number, such as `+15551234567`. |
   | `ACUMATICA_TELEGRAM_BOT_TOKEN`, `SAGEINTACCT_TELEGRAM_BOT_TOKEN` | Each demo's own Telegram bot, from @BotFather. |
   | `TELEGRAM_WEBHOOK_SECRET` | A long random secret both bots send with each update, for example from `openssl rand -hex 32`. |

   Setting a secret restarts the machine with the new value.

3. **Sign in to Velixo once.** The app starts signed out:

   ```sh
   fly ssh console --app velixo-cli-samples -C "velixo login --no-browser"
   # Open the URL it prints and sign in, then pass the address you land on:
   fly ssh console --app velixo-cli-samples -C 'velixo login --complete "<redirected URL>"'
   ```

   The account must reach every connection the samples use. Sign in from nowhere else with the same files: two copies of one sign-in end each other's session.

4. **Point the numbers and bots at the app.**
   - In Twilio, set each number's "A message comes in" webhook to `https://velixo-cli-samples.fly.dev/acumatica/sms` or `.../sageintacct/sms`.
   - Register each bot once:

     ```sh
     curl "https://api.telegram.org/bot<token>/setWebhook" \
       -d url=https://velixo-cli-samples.fly.dev/acumatica/telegram -d secret_token=<TELEGRAM_WEBHOOK_SECRET>
     ```

The expense demos run with `EXPENSES_SIMULATOR=off`, so the deployed app never serves the simulator, which would let anyone create expenses as any employee.

The image installs the latest CLI release. The three apps call the CLI at the same time, which is safe only in CLI releases after 0.3.1.
