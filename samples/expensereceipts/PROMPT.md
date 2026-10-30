# How this sample was built

Claude Code built the expense receipts sample in one session, on 2026-10-30, with the Velixo CLI signed in on a development environment, an Acumatica demo company and a Sage Intacct demo company. Every ERP query and batch came from `velixo query build` or `velixo command build`, and was run against the demo companies before the app used it.

## The request

> I want to build another sample using the CLI: an app that receives expense receipts by text message, and creates the receipt.
>
> It matches the employee by phone number, or asks if it can't find it. It decodes the image, isolates and straightens the receipt, then attaches it to the expense created.
>
> The user will mention the project, or add notes by text. Match what you can yourself (expense item, project), ask as few questions as possible to the user during the receipt creation. Return the URL to the expense receipt.
>
> It should work with both Sage and Acumatica (and Business Central if there's an equivalent module).

The developer also gave the notes a previous agent had learned about creating Sage Intacct expense reports: the employee is required, the report needs a base currency, a multi-entity company needs a location on every line, and the header's entity is read-only. Later in the session:

> Let's use OpenRouter for this project.

> What's the simplest way to deploy this? I will likely deploy two instances, one for Sage Intacct skyline, the other for the Acumatica demo, each with their own phone number. Let's do two Docker files.

The first Docker build failed: the CLI's install script downloads from the repository's GitHub releases, and the repository was not public yet. Once it was, both images installed the CLI from `cli.velixo.com`. The Acumatica image defaults to the `demo` connection and `https://demo.velixo.com` for its links.

## What was built, and what the demo companies taught

- **Language:** Python, so the sample shows the CLI from a language other than the C# it is written in, with OpenCV for the receipt.
- **Employees:** both ERPs store phone numbers in many formats (`+1 (777) 345-2246`, `206-321-0942`), so the app compares digits only.
- **Acumatica:** the first receipt the app created failed three times, and each failure became part of the batch: an expired project (expired projects are now left out), a missing project task (the batch sets the project's default or first task), and a missing cost code (the batch sets the tenant's default cost code).
- **Sage Intacct:** the first report failed because the employee was not a resource of the project, so the project list became the employee's own. The location comes from the employee's record, so the app never asks for it.
- **Business Central** has no expense module of its own, so the sample does not cover it.

## How it was checked

- `pytest` runs 27 tests with a fake CLI, a fake ERP and a fake reader, including a drawn receipt photographed at an angle on a dark table.
- On the Acumatica demo company the app created expense receipt 000834 with the straightened photo attached. On the Sage Intacct demo company it created expense report 3559 with its photo.
- The receipt reader was not run live in this session: the OpenRouter key was not available to it.

## What would have made it simpler

1. **The CLI returns the saved record's key in two shapes.** An Acumatica batch returns `result.feedback.savedKey`; a Sage Intacct batch returns the key inside `result.textualResponse`, a JSON string. One `savedKey` for every ERP would remove the special case.
2. **No link to the saved record.** The app builds an Acumatica link itself. For Sage Intacct, the REST API the CLI uses has no record URL on an expense report (only the older XML API has `RECORD_URL`), so the app needs a link template from the user. The server could return the record's URL.
3. **The builder does not know what a project brings with it.** Acumatica needs a project task and a cost code with a project, and Sage Intacct needs the employee to be a resource of it; each took a failed create to learn.
