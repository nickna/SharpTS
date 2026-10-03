const value:any={[Symbol.match](s:any){return typeof s;}};const match:any=String.prototype.match;for(const receiver of [77,false,"abc",1n,Symbol("s")]){console.log(match.call(receiver,value));}
