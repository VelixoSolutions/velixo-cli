// Reads barcodes from the phone's rear camera. Chrome on Android has a built-in barcode detector; Safari has none,
// so there the page loads a WebAssembly build of ZXing that offers the same API.

const polyfillUrl = 'https://cdn.jsdelivr.net/npm/barcode-detector@3.2.2/dist/es/ponyfill.js';
const formats = ['code_128', 'code_39', 'code_93', 'codabar', 'ean_13', 'ean_8', 'upc_a', 'upc_e', 'itf', 'qr_code', 'data_matrix'];
const scanEveryMs = 120;

/** The last code read counts as gone once this many frames in a row show no barcode, and it can then be read again. */
const framesUntilGone = 6;

let detectorPromise;

async function createDetector() {
    let Detector = globalThis.BarcodeDetector;
    if (!Detector) {
        ({ BarcodeDetector: Detector } = await import(polyfillUrl));
    }

    const supported = await Detector.getSupportedFormats();
    const wanted = formats.filter(format => supported.includes(format));
    return new Detector(wanted.length ? { formats: wanted } : undefined);
}

/**
 * Shows the rear camera in `video` and calls `onCode` with the text of each barcode it reads.
 * After a read the scanner pauses until the next `start()`, so one barcode is one search.
 */
export function createScanner(video, { onCode, onStatus }) {
    let stream;
    let timer;
    let paused = false;
    let wanted = false;
    let lastCode;
    let framesWithoutCode = 0;
    let starting;

    async function start() {
        wanted = true;
        if (stream) {
            paused = false;
            loop();
            return;
        }
        if (starting) {
            return starting;
        }

        starting = (async () => {
            if (!navigator.mediaDevices?.getUserMedia) {
                onStatus('unsupported');
                return;
            }

            onStatus('starting');
            let opened;
            try {
                opened = await navigator.mediaDevices.getUserMedia({
                    audio: false,
                    video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
                });
            } catch (error) {
                onStatus(error?.name === 'NotAllowedError' ? 'denied' : 'unavailable');
                return;
            }

            /** The page may have left the search screen while the browser asked for the camera. */
            if (!wanted) {
                opened.getTracks().forEach(track => track.stop());
                return;
            }

            stream = opened;
            video.srcObject = stream;
            await video.play().catch(() => {});

            try {
                detectorPromise ??= createDetector();
                await detectorPromise;
            } catch {
                detectorPromise = undefined;
                onStatus('no-detector');
                return;
            }

            if (stream) {
                onStatus('scanning');
                paused = false;
                loop();
            }
        })();

        try {
            await starting;
        } finally {
            starting = undefined;
        }
    }

    async function loop() {
        clearTimeout(timer);
        if (!stream || paused) {
            return;
        }

        if (video.readyState >= video.HAVE_ENOUGH_DATA) {
            try {
                const detector = await detectorPromise;
                const [barcode] = await detector.detect(video);
                const code = barcode?.rawValue?.trim();
                framesWithoutCode = code ? 0 : framesWithoutCode + 1;
                if (framesWithoutCode >= framesUntilGone) {
                    lastCode = undefined;
                }

                /** A code still in front of the lens when the search screen comes back is the item just shown. */
                if (code && code !== lastCode && stream && !paused) {
                    lastCode = code;
                    paused = true;
                    navigator.vibrate?.(60);
                    onCode(code);
                    return;
                }
            } catch {
                /** A frame the detector cannot read is skipped; the next one is tried. */
            }
        }

        timer = setTimeout(loop, scanEveryMs);
    }

    function stop() {
        wanted = false;
        clearTimeout(timer);
        stream?.getTracks().forEach(track => track.stop());
        stream = undefined;
        video.srcObject = null;
    }

    /** Turns the phone's flashlight on or off, where the browser lets a page do it. Returns whether it is on. */
    async function toggleTorch() {
        const track = stream?.getVideoTracks()[0];
        if (!track?.getCapabilities?.().torch) {
            return false;
        }

        const on = !track.getSettings().torch;
        await track.applyConstraints({ advanced: [{ torch: on }] });
        return on;
    }

    function hasTorch() {
        return Boolean(stream?.getVideoTracks()[0]?.getCapabilities?.().torch);
    }

    return { start, stop, toggleTorch, hasTorch, isRunning: () => Boolean(stream) };
}
