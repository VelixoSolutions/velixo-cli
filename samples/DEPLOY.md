# Deploying the samples

Both CLI samples run in one Fly.io app, `velixo-cli-samples`. The workflow `.github/workflows/deploy-samples.yml` deploys it, each time with an explicit CLI version:

- after a CLI release is published: the samples from that release's tag, with that release;
- after a push to `main` that changes `samples/`: the samples from `main`, with the latest published CLI.

A sample that needs a CLI change not yet released runs on the latest release until the next release redeploys it.

| Path | What answers |
|---|---|
| `https://velixo-cli-samples.fly.dev/` | The inventory checker kiosk |
| `/acumatica/sms`, `/acumatica/telegram` | The expense receipts demo on Acumatica |
| `/sageintacct/sms`, `/sageintacct/telegram` | The expense receipts demo on Sage Intacct |

The app is one machine that never stops, with a 1 GB volume, `data`, at `/data`. `Dockerfile` builds Node, Python, the Velixo CLI and Caddy into one image. `deploy/start.sh` starts the kiosk, the two expense demos and Caddy, which routes each path to its app. The three apps share the CLI's one sign-in, in `/data/velixo`. The CLI replaces its refresh token on every call and saves the new one there, so the apps stay signed in only as long as the volume survives. Deploys keep the volume.

## Set it up once

The app `velixo-cli-samples` already exists, in Toronto (`yyz`). Create its volume there before the first deploy, because a deploy only attaches a volume that already exists: `fly volumes create data --app velixo-cli-samples --region yyz --size 1 --yes`. A `data` volume in another region is not used, and `fly volumes destroy <id>` deletes it.

1. **Let the workflow deploy.** Turn off Fly's own GitHub deploy for the app, in the Fly dashboard, so each merge is not deployed twice. Then store a deploy token for the app in the repository: `fly tokens create deploy --app velixo-cli-samples | gh secret set FLY_API_TOKEN -R VelixoSolutions/velixo-cli`. Without the token, the workflow skips with a notice. To deploy by hand, run `fly deploy --app velixo-cli-samples --ha=false --build-arg VELIXO_VERSION=<version>` inside `samples/`.

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
   fly ssh console --app velixo-cli-samples
   velixo login --env nx-dev --no-browser
   ```

   Open the URL it prints and sign in. The browser then lands on a page that does not load: copy its address and paste it at the prompt. Open a shell first, as above: `fly ssh console -C "..."` may run the command without a terminal, and it then prints the URL and exits instead of waiting. In that case, finish with `velixo login --complete "<the address>"` in the same shell.

   The account must reach every connection the samples use. Sign in from nowhere else with the same files: two copies of one sign-in end each other's session.

4. **Point the numbers and bots at the app.**
   - In Twilio, set each number's "A message comes in" webhook to `https://velixo-cli-samples.fly.dev/acumatica/sms` or `.../sageintacct/sms`.
   - Register each bot once:

     ```sh
     curl "https://api.telegram.org/bot<token>/setWebhook" \
       -d url=https://velixo-cli-samples.fly.dev/acumatica/telegram -d secret_token=<TELEGRAM_WEBHOOK_SECRET>
     ```

The expense demos run with `EXPENSES_SIMULATOR=off`, so the deployed app never serves the simulator, which would let anyone create expenses as any employee.

The image installs the CLI version the deploy passes as `VELIXO_VERSION`, or the latest release when none is passed. The three apps call the CLI at the same time, which the CLI handles safely.
