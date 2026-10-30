# Expense receipts by text message

An employee texts a photo of a receipt, with a note such as the project if they like. The app finds the employee by the phone number, straightens the receipt, reads it, creates the expense in Acumatica or Sage Intacct with the photo attached, and texts back a link to it. It asks a question only when it must: who an unknown number belongs to, an expense type it cannot pick, a project it cannot match, or the amount of a receipt in another currency.

The app is a small Python server. Every ERP read and write goes through the Velixo CLI, through the queries and batches in `velixo/`. Texts come in through Twilio, and a simulator page lets you try it without a Twilio account. [PROMPT.md](PROMPT.md) tells how it was built.

## What it does with a receipt

1. **Who:** the phone number is compared, digits only, with every phone of every active employee. When none matches, the app asks for an employee ID or work email and remembers the answer for that number.
2. **The photo:** OpenCV finds the receipt, the largest light shape on the darker table, and straightens it into a portrait image.
3. **Reading:** a vision model, through OpenRouter, reads the merchant, date, total and currency, and chooses the expense item and the project from the ERP's own lists, using the employee's note.
4. **Creating:** the CLI runs the ERP's batch with the straightened photo attached, and the reply gives the new expense's number and link.

| | Acumatica | Sage Intacct |
|---|---|---|
| What it creates | An expense receipt (Expense Receipts, EP301020) | An employee expense report with one line |
| Expense items | Stock items of type Expense | Employee expense types |
| Projects | Active projects that have not expired, with their default task, or their first task, and the default cost code | The projects the employee is a resource of |
| Where the expense goes | Acumatica's default branch | The employee's own location |

Business Central has no expense module of its own, so the sample does not support it.

## Run it

You need Python 3.10 or later, and the Velixo CLI signed in (`velixo login`). Use a CLI release after 0.3.1: the app runs CLI commands for several phones at once, and only later releases renew the sign-in safely when they do.

```sh
cd samples/expensereceipts
python3 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env    # then fill it in
set -a; . ./.env; set +a
python app.py
```

Open <http://localhost:8090>, attach a photo of a receipt, and send it. The phone number at the top is the employee's: on the Acumatica demo at `demo.velixo.com` (connection `demo`), `+1 777 345 2246` is Maxwell Baker.

| Variable | Meaning |
|---|---|
| `EXPENSES_ERP` | `acumatica` or `sageintacct`. |
| `VELIXO_CONNECTION` | The CLI connection to use. |
| `EXPENSES_BASE_CURRENCY` | The currency expenses are claimed in. A receipt in another currency makes the app ask for the amount. Default `USD`. |
| `OPENROUTER_API_KEY` | The key the receipt reader uses. |
| `OPENROUTER_MODEL` | Another vision model on OpenRouter. Default `anthropic/claude-opus-5.5`. |
| `ACUMATICA_URL`, `ACUMATICA_COMPANY` | The instance address and company, for the link in the reply. |
| `SAGE_EXPENSE_URL_TEMPLATE` | A link to an expense report with `{id}` in place of its key. Without it, the reply gives the report's number only. |
| `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER` | The Twilio number that receives the texts and sends the replies. |
| `TELEGRAM_BOT_TOKEN`, `TELEGRAM_WEBHOOK_SECRET` | The Telegram bot that receives receipts, and the secret Telegram sends with each update. |
| `TWILIO_WEBHOOK_URL` | The public URL Twilio posts to, when the app runs behind a tunnel or proxy, so the signature check passes. |
| `PORT`, `HOST` | Where the app listens. Default `127.0.0.1:8090`. |

## Receive real texts

1. Buy an SMS-capable number in Twilio and fill in the four `TWILIO_` variables.
2. Give the app a public HTTPS address, for example with a Cloudflare Tunnel: `cloudflared tunnel --url http://localhost:8090`.
3. In the Twilio console, set the number's "A message comes in" webhook to `https://<that address>/sms`, method POST.

The app answers Twilio at once and sends its replies through Twilio's API, because reading a receipt and creating the expense take longer than Twilio waits.

## Receive receipts in Telegram

The same app also works as a Telegram bot, beside the text messages or instead of them.

1. In Telegram, message @BotFather, send `/newbot`, and put the token it gives you in `TELEGRAM_BOT_TOKEN`.
2. Choose a long random secret for `TELEGRAM_WEBHOOK_SECRET`, and give the app a public HTTPS address as for Twilio.
3. Point the bot at the app once:

   ```sh
   curl "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook" \
     -d url=https://<that address>/telegram -d secret_token=$TELEGRAM_WEBHOOK_SECRET
   ```

A Telegram message carries no phone number, so the bot's first reply shows a "Share my phone number" button. Telegram sends the number of the user's own account, and the app then finds the employee by it, as for a text message. It remembers the number for that chat in `telegram-phones.json`.

## Two demos with Docker

`Dockerfile.sageintacct` and `Dockerfile.acumatica` build one demo each, with the Velixo CLI installed from `cli.velixo.com`. The CLI's sign-in and the app's conversations live on volumes, so they survive a restart. Run every command from this folder.

```sh
docker build -f Dockerfile.sageintacct -t expense-receipts-sageintacct .
docker build -f Dockerfile.acumatica -t expense-receipts-acumatica .

# Sign each one in once. The first command prints a URL; open it, then pass the URL you land on.
docker run -it --rm -v velixo-sageintacct:/data/velixo expense-receipts-sageintacct velixo login --env nx-dev --no-browser
docker run -it --rm -v velixo-sageintacct:/data/velixo expense-receipts-sageintacct velixo login --complete "<redirected URL>"

docker run -d --name receipts-sage --env-file .env.sageintacct \
  -v velixo-sageintacct:/data/velixo -v state-sageintacct:/data/state -p 8090:8090 expense-receipts-sageintacct
docker run -d --name receipts-acumatica --env-file .env.acumatica \
  -v velixo-acumatica:/data/velixo -v state-acumatica:/data/state -p 8091:8090 expense-receipts-acumatica
```

Each `.env.<erp>` holds that demo's Twilio number, OpenRouter key and link settings; the image already sets `EXPENSES_ERP` and a default `VELIXO_CONNECTION`. Give each port its own public hostname, for example two Cloudflare Tunnel routes, and point each Twilio number at its own `/sms`.

The image installs the latest CLI release; `--build-arg VELIXO_VERSION=0.3.1` pins one. Each image signs in on its own volume, so the two demos never share a refresh token.

## Tests

```sh
pytest
```

The tests need no ERP, no model and no Twilio: the CLI, the ERP and the reader are fakes. They draw a receipt, tilt it on a dark table, and check that the scanner stands it upright and crops the table away; they walk every turn of the conversation; and they check the batches each ERP adapter runs.

## Making it yours

The queries and batches in `velixo/` were built with `velixo query build` and `velixo command build`, then checked against the demo companies:

- Acumatica refuses a receipt with a project and no project task, and, when cost codes are on, without a cost code. The batch sets the project's task and the tenant's default cost code (`default-cost-code.vql`) whenever the receipt has a project.
- Sage Intacct refuses an expense on a project the employee is not a resource of, so its project list is the employee's own (`projects.vql` takes the employee ID).
- Sage Intacct needs a base currency on the report and a location on each line; the line takes the employee's own location.
- The app trusts the answer to "who are you?": a number the ERP does not know can name any employee, and the app remembers it. Before real use, confirm the answer, for example with a code sent to the employee's email.
- The simulator lets anyone send as any phone number, so the app serves it only while neither Twilio nor Telegram is set up.

## Files

| File | What it does |
|---|---|
| `app.py` | The web app: the Twilio and Telegram webhooks, and the simulator. |
| `expenses/conversation.py` | The conversation: what to ask, and when to create the expense. |
| `expenses/erp.py` | Acumatica and Sage Intacct behind one interface. |
| `expenses/velixo.py` | Runs the CLI. |
| `expenses/scan.py` | Finds and straightens the receipt. |
| `expenses/reader.py` | Reads the receipt through OpenRouter. |
| `expenses/telegram.py` | The Telegram bot: the shared phone number, photos and replies. |
| `expenses/sms.py` | Sends replies through Twilio, or to the simulator. |
| `static/simulator.html` | The simulator page. |
| `velixo/` | The CLI queries and batches, one folder per ERP. |
| `tests/` | The tests, and the synthetic receipt photo they use. |
| `Dockerfile.sageintacct`, `Dockerfile.acumatica` | One demo each, with the CLI from `cli.velixo.com`. |
