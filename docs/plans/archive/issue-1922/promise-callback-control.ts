const e = new RangeError('reject');
Promise.reject(e).catch((x: any) => console.log(x === e, x instanceof RangeError, x.message));