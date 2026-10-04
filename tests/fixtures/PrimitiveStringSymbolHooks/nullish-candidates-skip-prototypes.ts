let calls=0;(Object.prototype as any)[Symbol.search]=function(){calls++;return 99;};console.log("abc".search(undefined),"null".search(null),calls);
