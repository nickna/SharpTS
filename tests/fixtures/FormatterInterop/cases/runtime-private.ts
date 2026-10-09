"use strict";
// formatter-comment: the runtime control keeps generics and namespaces in supported call positions.
namespace RuntimeFormatting{export function value():number{return 42;}}
function identity<T>(value:T):T{return value;}
class RuntimeVault<T>{#value:T;constructor(value:T){this.#value=value;}static has(value:any):boolean{return #value in value;}read():T{return this.#value;}}
const box=new RuntimeVault(identity<number>(RuntimeFormatting.value()));
console.log(box.read());console.log(RuntimeVault.has(box));console.log(RuntimeVault.has({}));
// @ts-expect-error Keep this directive attached to its intentional missing-property error.
const intentionallyIncomplete:{required:number}={};
console.log("directive 🧭");
