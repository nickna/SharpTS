(Object.prototype as any)[Symbol.search]=function(s:any){"use strict";console.log(typeof this);return s.length+5;};console.log("abc".search(1 as any),"abc".search(false as any),"abc".search("x"));
