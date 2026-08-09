import {
    DesktopApplication,
    TextBlock,
    Window,
    createDesktopApplication,
    createSignal,
} from "@sharpts/gui";
import { trace } from "@sharpts/gui/internal-testing";

const [fail, setFail] = createSignal<boolean>(false);
let application: DesktopApplication;

function FailureWindow(): JSX.Element {
    if (fail()) throw new Error("expected isolated window failure");
    return <Window title="Failure probe"><TextBlock>Ready</TextBlock></Window>;
}

application = createDesktopApplication({
    shutdownMode: "onMainWindowClose",
    onUnhandledError: (error, window) => {
        if (String(error).indexOf("expected isolated window failure") < 0)
            throw error;
        trace("multi-window-isolated-error");
        if (!window.isDisposed) throw new Error("Failed window was not disposed.");
        if (application.windowCount !== 1) throw new Error("Unrelated window did not retain identity.");
        trace("multi-window-main-retained");
        setTimeout((() => application.shutdown(0)) as any, 10);
    },
});

const mainWindow = application.createWindow(
    <Window title="Main"><TextBlock>Main</TextBlock></Window>,
    { main: true },
);
const secondaryWindow = application.createWindow(
    <Window title="Secondary"><TextBlock>Secondary</TextBlock></Window>,
    { owner: mainWindow },
);
application.createWindow(<FailureWindow />);

if (application.windowCount !== 3) throw new Error("Expected three mounted windows.");
trace("multi-window-mounted");
secondaryWindow.close();
if (!secondaryWindow.isDisposed) throw new Error("Secondary close did not dispose its root.");
trace("multi-window-secondary-closed");
setFail(true);
