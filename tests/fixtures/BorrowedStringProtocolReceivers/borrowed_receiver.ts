const value:any={[Symbol.match](s:any){return typeof s+":"+s;}};console.log(String.prototype.match.call(77 as any,value));
