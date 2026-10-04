const g:any=isNaN;for(const value of [""," ","0x10","0b10","0o10","NaN","Infinity","1x"]){console.log(g(value),isNaN(value as any));}
