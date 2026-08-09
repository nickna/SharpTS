import {
    DesktopApplication,
    Button,
    TextBlock,
    Window,
    createDesktopApplication,
    createSignal,
} from "@sharpts/gui";
import { getProperty, queueMicrotask as queueHostedMicrotask, trace } from "@sharpts/gui/internal-testing";

const [fail, setFail] = createSignal<boolean>(false);
const [stylePhase, setStylePhase] = createSignal<number>(0);
let application: DesktopApplication;

function FailureWindow(): JSX.Element {
    if (fail()) throw new Error("expected isolated window failure");
    return <Window title="Failure probe"><TextBlock>Ready</TextBlock></Window>;
}

function MainWindow(): JSX.Element {
    return <Window title="Main"><Button key="styled" classes={["accent"]}>{"Main " + stylePhase()}</Button></Window>;
}

application = createDesktopApplication({
    shutdownMode: "onMainWindowClose",
    resources: { accent: "#336699", buttonPadding: 8 },
    styles: [{
        selector: { control: "Button", classes: ["accent"] },
        setters: {
            background: { resource: "accent" },
            padding: { resource: "buttonPadding" },
        },
    }],
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
    <MainWindow />,
    { main: true },
);
const secondaryWindow = application.createWindow(
    <Window title="Secondary"><TextBlock>Secondary</TextBlock></Window>,
    { owner: mainWindow },
);
application.createWindow(<FailureWindow />);

if (application.windowCount !== 3) throw new Error("Expected three mounted windows.");
if (mainWindow.findResource("accent") !== "#336699") throw new Error("Resource lookup failed.");
trace("multi-window-mounted");
secondaryWindow.close();
if (!secondaryWindow.isDisposed) throw new Error("Secondary close did not dispose its root.");
trace("multi-window-secondary-closed");
setTimeout((() => {
    if (getProperty("styled", "background").indexOf("336699") < 0) throw new Error("Native style did not apply.");
    trace("multi-window-style-applied");
    setStylePhase(1);
    queueHostedMicrotask(() => {
        if (getProperty("styled", "background").indexOf("336699") < 0) throw new Error("Native style was lost after update.");
        trace("multi-window-style-retained");
        setFail(true);
    });
}) as any, 0);
