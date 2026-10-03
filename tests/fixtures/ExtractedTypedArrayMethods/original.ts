const a:any=new Uint8Array([1,2]);const fill:any=a.fill;fill.call(a,3);console.log(a[0],a[1]);
