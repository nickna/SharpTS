class Values extends Array {}
const values: any = new Values(3);
values[1] = 7;
console.log(values.length, 0 in values, values[1]);
console.log(values instanceof Values, values instanceof Array, Array.isArray(values));
