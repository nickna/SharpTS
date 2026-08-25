import { initialProgrammerState, programmerReducer } from "./programmer";

let state = programmerReducer(initialProgrammerState, { type: "digit", digit: "1" });
state = programmerReducer(state, { type: "operator", operator: "lsh" });
state = programmerReducer(state, { type: "digit", digit: "3" });
state = programmerReducer(state, { type: "equals" });
if (state.error !== "" || state.value !== 8n)
    throw new Error("Programmer compiled shift failed: " + state.error + " value " + state.value.toString());
console.log("Programmer model tests passed.");
