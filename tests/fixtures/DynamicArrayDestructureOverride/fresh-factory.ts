const source:any=[1,2];let calls=0;source[Symbol.iterator]=function*(){calls++;yield calls;yield calls+4;};const [...a]=source;const [...b]=source;console.log(a.join(","),b.join(","),calls);
