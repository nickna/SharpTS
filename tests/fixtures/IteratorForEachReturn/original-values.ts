const seen:any[]=[];Iterator.from([4,5]).forEach((x:any,i:any)=>seen.push(x+i));console.log(seen.join(","));
