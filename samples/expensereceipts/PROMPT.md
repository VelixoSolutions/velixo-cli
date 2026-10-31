# The prompt

Claude Code built this sample with the Velixo CLI signed in on an Acumatica demo company and a Sage Intacct demo company. This prompt builds it again.

> Using the Velixo CLI, build a sample app in `samples/expensereceipts` that receives expense receipts by text message and in a Telegram bot, and creates the expense. Use Python, not C#, to show that any language can use the CLI as its ERP back end. It works with both Acumatica (an expense receipt) and Sage Intacct (an employee expense report), and Business Central if it has an equivalent module.
>
> **Who.** Match the employee by phone number, or ask if you can't find it, and remember the answer. In Telegram, ask the user to share their phone number with a button.
>
> **The receipt.** Decode the image, isolate and straighten the receipt, turn it into a black and white scan, and attach it to the expense created. Read the merchant, date, total and currency with a vision model through OpenRouter. Take the currency only from a code printed on the receipt; a receipt in another currency asks for the amount.
>
> **Ask as few questions as possible.** The user may mention the project or add notes in the message. Match what you can yourself: the expense item, the project and, on Acumatica, its task. When you are not sure of the expense item, offer your three likeliest, and remember the item chosen for each merchant. For a restaurant, bar or taxi bill that shows no tip, offer to add one. Before creating, look for an expense the same employee already has for the same amount within three days, and ask before creating a duplicate. In Telegram, use buttons: a summary with Create, a project picker with its tasks, and an expense item picker.
>
> **The reply** gives the URL of the new expense.
>
> **How to build it.** Build every query and batch with `velixo query build` or `velixo command build`, save them as files in `velixo/`, one folder per ERP, and run each one against the demo companies before the app uses it. Add a simulator page for trying it without Twilio, off in a public deployment. Write tests with a fake CLI, a fake ERP and a fake reader. Add two Dockerfiles, one demo for each ERP, each with its own phone number, and a README.
>
> Keep a list of what about the CLI made the app harder to build, so we can improve the CLI or its documentation.
