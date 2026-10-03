for(const limit of [1n,Symbol("limit")] as any[]){try{Iterator.from([1]).take(limit);}catch(e:any){console.log(e.name);}try{Iterator.from([1]).drop(limit);}catch(e:any){console.log(e.name);}}
