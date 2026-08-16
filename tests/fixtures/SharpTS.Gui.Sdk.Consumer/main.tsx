import {
    Button,
    StackPanel,
    TextBlock,
    Window,
    createDesktopApplication,
    createSignal,
    getLaunchArguments,
} from "@sharpts/gui";
import { inspectDesktopTree } from "@sharpts/gui/devtools";

const [count, setCount] = createSignal<number>(0);

function App(): JSX.Element {
    return <Window title="SharpTS GUI packaged consumer" width={420} height={240}>
        <StackPanel margin={20} spacing={12}>
            <TextBlock key="status">{"Count: " + count()}</TextBlock>
            <Button key="increment" onClick={() => setCount(value => value + 1)}>Increment</Button>
        </StackPanel>
    </Window>;
}

const application = createDesktopApplication();
const window = application.createWindow(<App />, { main: true });
if (inspectDesktopTree().windows.length !== 1)
    throw new Error("GUI devtools inspector did not report the packaged consumer window.");

if (getLaunchArguments().indexOf("--smoke-close") >= 0)
    setTimeout((() => window.close()) as any, 25);
