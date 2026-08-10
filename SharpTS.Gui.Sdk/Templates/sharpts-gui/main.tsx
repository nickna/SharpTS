import {
    Button,
    StackPanel,
    TextBlock,
    Window,
    createDesktopApplication,
    useState,
} from "@sharpts/gui";

function App() {
    const [count, setCount] = useState(0);
    return (
        <Window title="SharpTS GUI" width={420} height={240}>
            <StackPanel spacing={12} margin={24}>
                <TextBlock fontSize={24}>SharpTS GUI</TextBlock>
                <TextBlock key="count">{`Count: ${count}`}</TextBlock>
                <Button key="increment" onClick={() => setCount(value => value + 1)}>
                    Increment
                </Button>
            </StackPanel>
        </Window>
    );
}

const application = createDesktopApplication();
application.createWindow(<App />, { main: true });
