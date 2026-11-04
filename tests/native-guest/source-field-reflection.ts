class Fields { value: number = 1; flag: boolean = true; }
const value = new Fields(), other = new Fields();
console.log(Reflect.set(value, "value", null), Reflect.set(value, "flag", undefined), value.value, value.flag);
console.log(Reflect.set(value, "value", 7), Reflect.set(value, "flag", false), value.value, value.flag);
const raw: any = {};
const numberSet = Reflect.set(value, "value", raw), booleanSet = Reflect.set(value, "flag", raw);
const descriptor: any = Object.getOwnPropertyDescriptor(value, "value");
const copy: any = Object.assign({}, value);
console.log(numberSet, booleanSet, Object.is(value.value, raw), Object.is(value.flag, raw),
    Object.is(descriptor.value, raw), Object.is(copy.flag, raw));
console.log(other.value, other.flag);
