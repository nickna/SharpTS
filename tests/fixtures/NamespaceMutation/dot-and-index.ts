namespace State {export let value=2;}const state:any=State;state.value=7;console.log(state["value"],State.value);state["added"]=8;console.log(state.added);state.added=9;console.log(state["added"]);
