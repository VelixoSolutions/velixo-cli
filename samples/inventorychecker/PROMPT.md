# How this sample was built

Claude Code built the inventory checker in one session, on 2026-10-29, from the requests below. It had the Velixo CLI signed in on a development environment and an Acumatica demo company to work against. It wrote no ERP code by hand: every query and screen batch came from `velixo query build` and `velixo command build`, and Claude Code then ran each one against the company to check its answer before the app used it.

## The requests

These are the developer's requests, in order, each with what Claude Code did with it.

1. > Using the Velixo CLI repo, build a sample kiosk app to verify inventory on an Acumatica system. Think of an app you would put in a store. Full screen, futuristic looking. I scan an item, it searches by inventory ID, then shows me the description, the image and the quantity on hand, as well as the expected receive date if it's on order. I will publish this as a sample in the CLI project.

   Claude Code built the item query and the open purchase order query with `velixo query build`, and the image download with `velixo command build`. It ran each one against items with an image, with and without an open order, and with an ID that does not exist. That found two things to change: the builder's purchase order query failed for an unknown ID, so its parameter moved to the item's code, and the image batch downloaded every image, so it now takes the file's name.

2. > The learnings you make could be used to improve the CLI or its documentation.

   Claude Code kept a list of everything about the CLI that made the app harder to build. The list is at the end of this file.

3. > Use whatever language you want. I would use something different than C#, just to highlight capabilities. You could also create a full screen web app in Node.

   The app became a Node.js server with no dependencies, which calls the CLI, and a web page.

4. > I would like the project to be contained in a folder named samples/inventorychecker, with a file named PROMPT.md which explains how this was created. Did you write some tests to verify functionality?

   It had been checked by hand only. Claude Code added `npm test`, which runs the server against a fake CLI that answers with what the real CLI printed, and `npm run test:live` against a real connection.

5. > Also add a feature to exercise commands: build a simple inventory transfer to put away some units into a RESERVED location of the warehouse. Create the location so that we have it.

   Claude Code built the batch that adds a location on the Warehouses screen, and ran it to add `RESERVED` to the demo company's retail warehouse. It then built the transfer batch, ran it for one unit, and read the locations back to see the unit move. The builder first asked for the transfer's branch and project; saying "leave every other field at its default" answered it.

6. > Add a nice animation when something gets reserved.

   A crate flies from the source location into the reserved one, a check mark draws itself, and the available quantities count down.

7. > The one call at a time issue should be fixed. I would make sure the CLI supports parallel invocations.

   The server had run one CLI command at a time, because two commands that renewed the access token at the same moment could end the session. Claude Code fixed the CLI instead, in a separate change, and the server now runs its queries in parallel.

8. > Make sure this shows inventory by warehouse.

   A hand-written query adds the quantity on hand and available in each warehouse, with the store's own marked.

9. > No need to have a duplicate guard in the app: it's a proof of concept.

   The server passes `--force` on the transfer, so reserving the same quantity twice makes two transfers.

10. > Add a powered by Velixo image with our logo somewhere on the sample app.

    The badge in the bottom right corner uses the Velixo logo, and the top left uses the Velixo symbol.

11. > I'd like to turn the inventory sample app into a progressive web app, that has a different interface when used on a mobile device. From the search page, it should have an always on camera that searches for barcodes. It would be useful to have inventory by location available too if we want, maybe not fetched realtime but when you click a button next to a warehouse.

    Claude Code added a manifest, a service worker and icons, and a phone layout that the page picks on a narrow touch screen. Its search screen reads barcodes from the rear camera, with the browser's barcode detector or, where there is none, ZXing in WebAssembly. Each warehouse on the item screen has a button that runs the existing `locations.vql` for that warehouse when tapped, so it needed no new query. The server can now serve HTTPS, which a phone needs for the camera.

12. > I would like you to show stock by location on desktops too.

    The kiosk's warehouses got the same button. Opening a warehouse's locations restarts the kiosk's 30-second return to the scan screen, and a barcode scanner still works after a click.

## How it was checked

- `npm test` runs 20 tests against the fake CLI.
- For the phone layout, `npm test` grew to 26 tests, and Chromium, driven by Playwright at 390×844 with a fake camera that showed a Code 128 barcode, read the item from the camera, listed a warehouse's locations, and went back to the camera without reading the same barcode twice.
- Chrome, driven by Playwright, scanned items at 1920×1080 and in portrait: an item on order, an item with nothing on order, an unknown code, the on-screen keypad, and a reservation of one unit from the start to the animation.
- Two transfers of one unit each ran on the demo company: one from the CLI, one from the app.

## What would have made it simpler

These went back to the CLI as fixes or as proposals:

1. **Parallel commands could end the session.** Fixed: the CLI now renews the token under a lock that other velixo processes wait on, and every change to its own files is made under a lock too.
2. **A batch that only reads counted as a write.** The image download sets the item's ID and downloads a file, with no save, yet a dry run reported `"wouldWrite": true`, so it needed `--yes`, and the duplicate guard refused the same read within 10 minutes. Fixed (RD-14742): such a batch now runs as a read.
3. **Two shapes for one query result.** `query run NAME` wraps the result in `{note, results: [...]}`, while `query run --query FILE` does not.
4. **The builder's choice of parameter field changes what an unknown value does.** On a selector field, Acumatica fails the whole query for a value it does not know; on the code field, the query returns no rows. Nothing in the guide says which to choose.
5. **The builder downloads attachments one file type at a time.** "Read the image files attached to" became 11 `DownloadAttachments` commands, one per extension, where one `*` does the job. A `{{Placeholder}}` also works as the pattern, which the guide does not say.
6. **Acumatica pads inventory IDs with spaces.** Query results return `"CONBABY1  "`, so an app must trim them before it compares or shows them.
