# The prompt

Claude Code built this sample with the Velixo CLI signed in on an Acumatica demo company. This prompt builds it again.

> Using the Velixo CLI, build a sample app in `samples/inventorychecker` that checks inventory on an Acumatica system. Use Node.js, not C#, to show that any language can use the CLI as its ERP back end.
>
> **The kiosk.** Think of an app you would put in a store: full screen and futuristic looking. I scan an item, it searches by inventory ID, then shows me the description, the image and the quantity on hand and available, in total and for each warehouse, with the store's own warehouse marked. If the item is on order, show the expected receive date of the next purchase order. Tapping a warehouse shows the item's stock in each of its locations, read when tapped rather than with the scan. Add a New scan button on the item screen, and a "powered by Velixo" badge with our logo.
>
> **Reserving.** Add a feature that exercises commands: an inventory transfer that puts some units away into a `RESERVED` location of the store's warehouse. Create that location so that we have it. Show a nice animation when something gets reserved. It is a proof of concept, so no duplicate guard.
>
> **On a phone.** Make it a progressive web app with its own layout on a mobile device. Its search page is an always-on camera that reads barcodes, and stock by location is available there too.
>
> **How to build it.** Build every query and batch with `velixo query build` or `velixo command build`, save them as files in `velixo/`, and run each one against the company before the app uses it. Run the queries in parallel. Write tests that run the server against a fake CLI answering with what the real CLI printed, and a live test against a real connection. Add a Dockerfile that installs the CLI from `cli.velixo.com`, and a README.
>
> Keep a list of what about the CLI made the app harder to build, so we can improve the CLI or its documentation.
