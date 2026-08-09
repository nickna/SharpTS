import {
    Border,
    Button,
    TextBlock,
    TextBlockHandle,
    useControlRef,
} from "@sharpts/gui";
import { CalculatorButton, CalculatorButtonDefinition } from "../../../Examples/Calculator/CalculatorApp";

declare const definition: CalculatorButtonDefinition;
const wrongRef = useControlRef<TextBlockHandle>();

export const missingProp = <CalculatorButton key="missing" definition={definition} active={false} />;
export const elementInText = <Button><TextBlock>Invalid</TextBlock></Button>;
export const multipleContent = <Border><TextBlock>A</TextBlock><TextBlock>B</TextBlock></Border>;
export const invalidRef = <Button ref={wrongRef}>Invalid ref</Button>;
