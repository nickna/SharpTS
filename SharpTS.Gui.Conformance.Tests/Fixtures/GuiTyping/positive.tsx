import {
    Border,
    Button,
    ButtonHandle,
    Fragment,
    Grid,
    TextBlock,
    useControlRef,
} from "@sharpts/gui";
import { CalculatorButton, CalculatorButtonDefinition } from "../../../Examples/Calculator/CalculatorApp";

const definition: CalculatorButtonDefinition = {
    id: "test", label: "1", automationName: "One", shortcut: "1",
    role: "digit", row: 0, column: 0, action: { type: "digit", digit: "1" },
};
const buttonRef = useControlRef<ButtonHandle>();

export const positive = (
    <Grid>
        <CalculatorButton key="typed-component" definition={definition} active={false} onPress={() => {}} />
        <TextBlock>{["recursive", 1, false, null]}</TextBlock>
        <Button ref={buttonRef}>Text only</Button>
        <Border><TextBlock>One logical child</TextBlock></Border>
        <Fragment><TextBlock>A</TextBlock><TextBlock>B</TextBlock></Fragment>
    </Grid>
);
