import { createDesktopApplication } from "@sharpts/gui";
import { PAINT_STYLES } from "./controls";
import { runPresentationChecks } from "./presentation.tests";

// Mount the first window synchronously; this entry has no document workflow or disk fixtures.
const app = createDesktopApplication({ styles: PAINT_STYLES, shutdownMode: "explicit" });
void runPresentationChecks(app).then(
    () => app.shutdown(0),
    (error) => {
        console.error(String(error));
        app.shutdown(1);
    }
);
