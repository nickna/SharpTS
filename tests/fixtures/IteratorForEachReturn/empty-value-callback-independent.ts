let calls=0;const r:any=Iterator.from([]).forEach(()=>{calls++;return 9;});console.log(r===undefined,r===null,calls);
