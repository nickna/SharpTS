const M:any=Map;const groups=M.groupBy([1,2,3],(x:number)=>x%2);console.log(groups.get(0).join(","));
