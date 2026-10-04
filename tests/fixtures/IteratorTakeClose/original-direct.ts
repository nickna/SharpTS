const source:any={closed:0,return(){this.closed++;return {done:true};}};console.log(source.return().done,source.closed);
