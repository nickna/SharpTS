namespace State {export let value=2;export function read(){return value;}}const state:any=State;state["value"]=9;console.log(state.value,State.value,State.read());
