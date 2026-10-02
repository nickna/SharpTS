interface MapConstructor{groupBy(items:any,callback:any):any;}const groups=Map.groupBy([1,2,3],(x:number)=>x%2);console.log(groups.get(0).join(","));
