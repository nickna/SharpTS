for (const target of [Function.prototype.call, Function.prototype.apply, Function.prototype.bind]) {
    try { Reflect.construct(target, []); console.log(false); }
    catch (error) { console.log(error instanceof TypeError); }
}
