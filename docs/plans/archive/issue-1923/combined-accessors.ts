const mk = Symbol("mk");
const C = class {
    _v: number = 5;
    get [Symbol.toStringTag]() { return "tag" + this._v; }
    get [mk]() { return this._v; }
    set [mk](x: number) { this._v = x; }
};
const c = new C() as any;
console.log(c[Symbol.toStringTag], c[mk]);
c[mk] = 99;
console.log(c[Symbol.toStringTag], c[mk]);