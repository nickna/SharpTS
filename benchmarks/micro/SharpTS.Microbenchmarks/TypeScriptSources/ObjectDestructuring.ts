type Point = { x: number; y: number };

function direct(n: number): number {
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) sum = sum + point.x + point.y;
    return sum;
}

function hoisted(n: number): number {
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    const { x, y } = point;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) sum = sum + x + y;
    return sum;
}

function fractional(n: number): number {
    const point: Point = { x: 1.25, y: 2.5 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        sum = sum + x + y;
    }
    return sum;
}

function varying(n: number): number {
    const first: Point = { x: 1, y: 2 };
    const second: Point = { x: 3, y: 4 };
    const dynamicFirst: any = first;
    const dynamicSecond: any = second;
    dynamicFirst.extra = true;
    dynamicSecond.extra = true;
    const points: Point[] = [first, second];
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = points[i % 2];
        sum = sum + x + y;
    }
    return sum;
}

function mutated(n: number): number {
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        dynamicPoint.x = i;
        const { x, y } = point;
        sum = sum + x + y;
    }
    return sum;
}

function destructureMaterialized(n: number): number {
    // The dynamic alias makes this literal dictionary-backed from construction.
    // object-destructure-carrier-materialized covers an actual carrier transition.
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        checksum = checksum + x + y;
    }
    return checksum;
}

function destructureCarrierMaterialized(n: number): number {
    // Discarded push retains compact allocation despite the subsequent dynamic write.
    const points: Point[] = [];
    points.push({ x: 1, y: 2 });
    const point: Point = points[0];
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        checksum = checksum + x + y;
    }
    return checksum;
}


