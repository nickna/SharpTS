import {
    Border,
    Button,
    ButtonHandle,
    CheckBox,
    ComboBox,
    Grid,
    ProgressBar,
    ScrollViewer,
    Slider,
    StackPanel,
    TextBlock,
    TextBlockHandle,
    TextBox,
    Window,
    createControlRef,
    createSignal,
    renderDesktop,
} from "@sharpts/gui";
import {
    beginOffThreadTask,
    isRefAttached,
    queueMicrotask,
    setCheckBoxValue,
    setComboBoxIndex,
    setSliderValue,
    setTextBoxValue,
    trace,
    traceControlIdentities,
} from "@sharpts/gui/internal-testing";

const statusRef = createControlRef<TextBlockHandle>();
const transientRef = createControlRef<ButtonHandle>();
const replacementRef = createControlRef<TextBlockHandle>();
const [phase, setPhase] = createSignal<number>(0);
const [useAlternate, setUseAlternate] = createSignal<boolean>(false);
const [primary, setPrimary] = createSignal<number>(0);
const [alternate, setAlternate] = createSignal<number>(0);
let viewCount = 0;
let desktopRoot: any = null;

function ConformanceApp(): JSX.Element {
    viewCount++;
    const currentPhase = phase();
    const selected = useAlternate() ? alternate() : primary();
    trace("view-render-" + viewCount);
    const ordered = currentPhase === 0
        ? [
            <TextBlock key="a">A</TextBlock>,
            <TextBlock key="b">B</TextBlock>,
        ]
        : [
            <TextBlock key="b">B updated</TextBlock>,
            <TextBlock key="a">A updated</TextBlock>,
        ];

    return (
        <Window key="window" title={currentPhase === 0 ? "SharpTS reactive desktop" : "SharpTS reactive desktop updated"} width={currentPhase === 0 ? 720 : 760} height={currentPhase === 0 ? 560 : 600} theme={currentPhase === 0 ? "light" : "dark"}>
            <Border key="shell" padding={16} background={currentPhase === 0 ? "#f4f4f4" : "#202020"} borderBrush="#4f8cc9" borderThickness={1} cornerRadius={8}>
                <ScrollViewer key="scroll" verticalScrollBarVisibility="auto">
                    <StackPanel key="panel" spacing={currentPhase === 0 ? 12 : 18}>
                        <TextBlock key="status" ref={statusRef} fontSize={20} fontWeight="bold" textWrapping="wrap">{"Selected " + selected}</TextBlock>
                        <Grid key="form" rows="auto,auto,auto,auto,auto" columns="140,*">
                            <TextBlock key="name-label" gridRow={0} gridColumn={0} margin={4}>Name</TextBlock>
                            <TextBox key="name" text={currentPhase === 0 ? "Initial" : "Rendered"} placeholder="Enter a name" gridRow={0} gridColumn={1} margin={4} onTextChanged={(value: string) => trace("form-text:" + value)} />
                            <CheckBox key="enabled" isChecked={currentPhase !== 0} gridRow={1} gridColumn={1} margin={4} onCheckedChanged={(value: boolean) => trace("form-check:" + value)}>Enabled</CheckBox>
                            <ComboBox key="choice" items={["First", "Second", "Third"]} selectedIndex={currentPhase === 0 ? 0 : 1} gridRow={2} gridColumn={1} margin={4} onSelectionChanged={(index: number) => trace("form-choice:" + index)} />
                            <Slider key="amount" minimum={0} maximum={10} value={currentPhase === 0 ? 2 : 4} gridRow={3} gridColumn={1} margin={4} onValueChanged={(value: number) => trace("form-slider:" + value)} />
                            <ProgressBar key="progress" minimum={0} maximum={10} value={currentPhase === 0 ? 2 : 4} gridRow={4} gridColumn={1} margin={4} />
                        </Grid>
                        <Button
                            key="action"
                            padding={8}
                            onClick={currentPhase === 0
                                ? () => trace("stale-guest-click")
                                : () => {
                                    trace("guest-click");
                                    setTimeout((() => {
                                        setAlternate(2);
                                        trace("late-reactive-work-ignored");
                                        desktopRoot.dispose();
                                    }) as any, 10);
                                }}
                        >{currentPhase === 0 ? "Old callback" : "Latest callback"}</Button>
                        {currentPhase === 0 ? <Button key="transient" ref={transientRef}>Removed later</Button> : null}
                        {currentPhase === 0
                            ? <TextBlock key="replacement" ref={replacementRef}>Replace me</TextBlock>
                            : <Button key="replacement">Replacement button</Button>}
                        {ordered}
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Window>
    );
}

desktopRoot = renderDesktop(<ConformanceApp />);

traceControlIdentities("identities-initial");

function afterCoalescedUpdate(): void {
    trace("coalesced-update-complete");
    traceControlIdentities("identities-reordered");
    if (!isRefAttached(transientRef)) {
        trace("transient-ref-cleaned");
    }
    setUseAlternate(true);
    queueMicrotask(afterDependencySwitch);
}

function afterDependencySwitch(): void {
    trace("dependency-switch-complete");
    setPrimary(3);
    setAlternate(1);
    queueMicrotask(afterFinalUpdate);
}

function afterFinalUpdate(): void {
    setTextBoxValue("name", "User");
    setCheckBoxValue("enabled", false);
    setComboBoxIndex("choice", 2);
    setSliderValue("amount", 9);
    trace("forms-events-complete");
    trace("reactive-update-complete");
    traceControlIdentities("identities-final");
}

setPrimary(0);
setPrimary(1);
setPrimary(2);
setPhase(1);
queueMicrotask(afterCoalescedUpdate);
beginOffThreadTask(() => {
    trace("guest-async-resume");
    setTimeout((() => trace("guest-timer")) as any, 50);
});

async function resumePromiseMicrotask(): Promise<void> {
    await Promise.resolve(1);
    trace("guest-promise-resume");
}

resumePromiseMicrotask();
