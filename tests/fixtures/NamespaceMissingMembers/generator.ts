namespace Values {export const value=3;}function* run(){const values:any=Values;yield values.missing;yield values["missing"];}for(const value of run())console.log(value===undefined,typeof value);
