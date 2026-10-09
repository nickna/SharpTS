"use strict";
// formatter-comment: ordinary comments survive.
namespace Formatting{export type Label<T> = {value:T};export function label<T>(value:T):Label<T>{return {value /* formatter-comment: inline comment survives. */};}}
class Vault<T>{#value:T;constructor(value:T){this.#value=value;}static has(value:any):boolean{return #value in value;}read():T{return this.#value;}}
const box=new Vault(Formatting.label<number>(42));
console.log(box.read().value);console.log(Vault.has(box));console.log(Vault.has({}));
// @ts-expect-error Keep this directive attached to its intentional type error.
const suppressed:number="directive 🧭";
console.log(suppressed);
