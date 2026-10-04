"use strict";namespace State {export let value=2;export function read(){return value;}}const state:any=State;state.extra=8;state["value"]=9;console.log(state.extra,state.value,State.read());
