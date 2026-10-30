# Deploying the samples

`.github/workflows/deploy-samples.yml` deploys both CLI samples to one Fly.io app, `velixo-cli-samples`, whenever a sample changes on `main`. You can also run it by hand from the Actions tab.

| Path | What answers |
|---|---|
| `https://velixo-cli-samples.fly.dev/` | The inventory checker kiosk, behind its password |
| `/acumatica/sms`, `/acumatica/telegram` | The expense receipts demo on Acumatica |
| `/sageintacct/sms`, `/sageintacct/telegram` | The expense receipts demo on Sage Intacct |

The app is one machine that never stops, with a 1 GB volume, `data`, at `/data`. `Dockerfile` builds Node, Python, the Velixo CLI and Caddy into one image. `deploy/start.sh` starts the kiosk, the two expense demos and Caddy, which routes each path to its app. The three apps share the CLI's one sign-in, in `/data/velixo`. The CLI replaces its refresh token on every call and saves the new one there, so the apps stay signed in only as long as the volume survives. Deploys keep the volume.

## Set it up once

The app `velixo-cli-samples` and its volume `data` in `iad` already exist, and the `samples` GitHub environment holds the Fly token.

1. **Fill in the settings.** The `samples` environment has every setting the workflow reads, empty or with a known value. Set them in GitHub under Settings › Environments › samples. A setting can be a secret or a variable; the workflow reads either, and sends the ones that are not empty to Fly as the app's secrets. Emptying a setting later does not remove it from Fly: run `fly secrets unset` for that.

   | Setting | Meaning |
   |---|---|
   | `KIOSK_PASSWORD` | **Required.** The password the browser asks for before it shows the kiosk. The kiosk can reserve stock, so the workflow does not deploy without one. |
   | `KIOSK_CONNECTION`, `KIOSK_WAREHOUSE` | The kiosk's CLI connection and store warehouse. Without a warehouse the kiosk only reads. |
   | `OPENROUTER_API_KEY` | The key both expense demos use to read receipts. |
   | `ACUMATICA_CONNECTION`, `SAGEINTACCT_CONNECTION` | Each expense demo's CLI connection: `demo` and `skyline`. |
   | `ACUMATICA_URL`, `ACUMATICA_COMPANY` | The Acumatica instance and company, for the link in the reply. |
   | `SAGE_EXPENSE_URL_TEMPLATE` | A link to a Sage Intacct expense report, with `{id}` in place of its key. |
   | `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` | The Twilio account both demos send from. |
   | `ACUMATICA_TWILIO_FROM_NUMBER`, `SAGEINTACCT_TWILIO_FROM_NUMBER` | Each demo's own number. |
   | `ACUMATICA_TELEGRAM_BOT_TOKEN`, `SAGEINTACCT_TELEGRAM_BOT_TOKEN` | Each demo's own Telegram bot. |
   | `TELEGRAM_WEBHOOK_SECRET` | A long random secret both bots send with each update. |

2. **Deploy.** Run **Deploy samples** from the Actions tab. It skips, with a notice, while `FLY_API_TOKEN` or `KIOSK_PASSWORD` is empty, or while either sample folder is missing from `main`.

3. **Sign in to Velixo once.** The app starts signed out:

   ```sh
   fly ssh console --app velixo-cli-samples -C "velixo login --no-browser"
   # Open the URL it prints and sign in, then pass the address you land on:
   fly ssh console --app velixo-cli-samples -C 'velixo login --complete "<redirected URL>"'
   ```

   The account must reach all three connections. Sign in from nowhere else with the same files: two copies of one sign-in end each other's session.

4. **Point the numbers and bots at the app.**
   - In Twilio, set each number's "A message comes in" webhook to `https://velixo-cli-samples.fly.dev/acumatica/sms` or `.../sageintacct/sms`.
   - Register each bot once:

     ```sh
     curl "https://api.telegram.org/bot<token>/setWebhook" \
       -d url=https://velixo-cli-samples.fly.dev/acumatica/telegram -d secret_token=<TELEGRAM_WEBHOOK_SECRET>
     ```

The expense demos run with `EXPENSES_SIMULATOR=off`, so the deployed app never serves the simulator, which would let anyone create expenses as any employee.

The image installs the latest CLI release. The three apps call the CLI at the same time, which is safe only in CLI releases after 0.3.1.
