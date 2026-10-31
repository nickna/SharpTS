import { createHash, randomBytes } from "crypto";

console.log(createHash("sha256").update("abc").digest("hex"));
console.log(randomBytes(8).length);
