for(const s of ["\u0000","\u007F","\u0080","\u07FF","\u0800","\uFFFF","\uD800\uDC00","\uDBFF\uDFFF"]){const encoded=encodeURIComponent(s);console.log(encoded,decodeURIComponent(encoded)===s);}
