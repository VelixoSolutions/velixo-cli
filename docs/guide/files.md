<!-- commands: files stage, files download -->
# Files: sending files to the ERP, velixo files stage, velixo files download

A file goes to the ERP by its path: `velixo command run --attach PATH[:COMMENT]` attaches it to the record the batch saves, and `velixo import run --file PATH` imports it. Both upload the file themselves. `velixo files stage` uploads a file ahead of the run instead, or hands out a URL to upload it from elsewhere, for example with `curl`. A file comes back from the ERP as a link: a rendered report, an attachment read off a record, a workbook. `velixo files download` saves it, and so does `curl`.

## Send a file

```sh
velixo command run create-bill --param VendorID=ACME \
  --attach "invoice-4411.pdf:Vendor invoice" --yes
velixo import run --connection gab-dev --scenario "Import Customers" --file customers.csv --validate-only
```

The file must be on a disk you can read. When only the user holds it, ask them to save it where you can read it and give you the path, or stage it as below.

## Stage a file first

`velixo files stage` stages one file and prints its `fileToken`. Pass the token to the run instead of the path:

```sh
velixo files stage invoice-4411.pdf                     # uploads it, prints the token
velixo command run create-bill --param VendorID=ACME --attach-token "TOKEN:Vendor invoice" --yes
velixo import run --scenario "Import Customers" --file-token TOKEN --validate-only
```

For a file that is not on this machine, give its name and exact size in bytes. The command prints an `uploadUrl` and the `curl` command that sends the file to it; run that where the file is, or open the URL in a browser to upload the file there:

```sh
velixo files stage invoice-4411.pdf --size 48213        # add --sha256 HEX to have the server check the bytes
curl -T invoice-4411.pdf "https://..."                  # where the file is
```

`--no-upload` does the same for a file on this machine. A token is for one file on one record: the run that files it spends it. It expires in about an hour, and works only on the profile that staged it, which remembers the file's name for it. The upload URL takes the file once, and is a secret bound to the signed-in user: do not repeat it in a message.

## Download a file

```sh
velixo files download --from order.result.json --dir out
velixo files download "https://..." --out report.pdf
```

- `--from FILE` reads every link in a saved `command run` result and downloads each one once.
- `--dir DIR` saves each file under the name the server gives it. It never overwrites: when `report.pdf` exists, the new file is `report (2).pdf`.
- `--out PATH` saves one link to an exact path, and replaces a file already there.

It prints `fileName`, `path`, `bytes` and `contentType` for each file. Read `path` for the name each file got. `command run --download-dir DIR` downloads a batch's files in the same run.

A download link needs no sign-in, so `curl -o report.pdf "<downloadUrl>"` works as well.

## When it fails

| Exit | Code | What to do |
|---|---|---|
| 2 | `USAGE` | The path does not exist, `--out` was given with several links, or no link was found. Fix the command line. |
| 5 | `ATTACHMENT_TYPE_NOT_ALLOWED` or another `ATTACHMENT_*` code | The server will not take this file's type or size. Tell the user which file and why. |
| 5 | `ATTACHMENT_UPLOAD_FAILED` | The upload endpoint refused the bytes. Run the command again. |
| 1 | `HTTP_ERROR` | The upload or download failed on the server side. Retry once. |
| 5 | `ATTACHMENT_TOKEN_INVALID` from `files download` | The link expired. Run the command that issued it again. |

## Rules

- Attach or import only a file the user actually handed you. Never create a document, re-encode something you produced, or reuse a file from an unrelated request. If no file was handed to you, say plainly that you cannot attach one.
- Attaching or importing writes to the ERP: confirm it first, naming each document and the record it goes to. A file the user handed you is consent to give it to you, not consent to put it on a record.
- One file is one document on one record. Running the attach again files the document again, so after a failure you cannot classify, read the record before you retry.
- A report or a file read off a record arrives as a link, and the link is the deliverable. Give it to the user or download it, and name the format. Never describe, quote or summarise a file you have not downloaded and read.
- A link is bound to the user and lasts about an hour. Do not repeat it in later messages, and run the command that issued it again rather than re-send a stale one.

Next: `velixo guide command run` for attachments and downloads in a batch, or `velixo guide import`.
