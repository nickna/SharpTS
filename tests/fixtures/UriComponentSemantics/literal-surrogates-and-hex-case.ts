const s=decodeURIComponent("\uD800%41\uDC00");console.log(s.length,s.charCodeAt(0),s.charCodeAt(1),s.charCodeAt(2));console.log(decodeURIComponent("%c3%a9")===decodeURIComponent("%C3%A9"));
