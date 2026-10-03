let calls=0;const r:any=Iterator.from([]).forEach(()=>{calls++;});console.log(r===undefined,r===null,calls);
