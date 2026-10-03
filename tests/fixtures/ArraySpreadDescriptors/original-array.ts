const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,"1",{get(){reads++;return 8;}});const b=[...a];console.log(b.join(","),reads);
