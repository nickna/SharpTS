interface MapConstructor{groupBy(items:any,callback:any):any;}const groupBy=Map.groupBy;const groups=groupBy([1,2,3],(x:number)=>x%2);console.log(groups.get(0).join(","));
