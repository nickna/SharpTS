import { PaintCommand, PaintDocument, createLayerId, createTextCommand } from "./document";

/** Entirely editable, portable artwork: no external images, fonts, or network requests. */
export function createDemoDocument(): PaintDocument {
    const rectangle = (x: number, y: number, width: number, height: number, fill: string): PaintCommand => ({
        kind: "rectangle",
        x,
        y,
        width,
        height,
        fill,
        strokeThickness: 1
    });
    const circle = (x: number, y: number, radius: number, fill: string): PaintCommand => ({
        kind: "ellipse",
        centerX: x,
        centerY: y,
        radiusX: radius,
        radiusY: radius,
        fill,
        strokeThickness: 1
    });
    const layer = (name: string, commands: PaintCommand[], opacity: number = 1) => ({
        id: createLayerId(),
        name,
        commands,
        opacity,
        isVisible: true
    });
    return {
        format: "sharpaint",
        version: 1,
        width: 960,
        height: 640,
        layers: [
            layer("Midnight paper", [rectangle(0, 0, 960, 640, "#142d3b")]),
            layer("Sun & horizon", [
                circle(700, 218, 133, "#f0b86b"),
                rectangle(0, 346, 960, 294, "#285c68")
            ]),
            layer(
                "Water reflections",
                [
                    rectangle(596, 383, 209, 5, "#f7ca8c"),
                    rectangle(637, 407, 135, 4, "#f7ca8c"),
                    rectangle(558, 433, 285, 6, "#f7ca8c"),
                    rectangle(601, 463, 198, 4, "#f7ca8c"),
                    rectangle(641, 487, 107, 3, "#f7ca8c"),
                    rectangle(552, 513, 278, 5, "#f7ca8c")
                ],
                0.55
            ),
            layer("Foreground dunes", [
                {
                    kind: "ellipse",
                    centerX: 110,
                    centerY: 660,
                    radiusX: 515,
                    radiusY: 190,
                    fill: "#1b414c",
                    strokeThickness: 1
                },
                {
                    kind: "ellipse",
                    centerX: 890,
                    centerY: 724,
                    radiusX: 490,
                    radiusY: 173,
                    fill: "#102936",
                    strokeThickness: 1
                }
            ]),
            layer("Editable typography", [
                createTextCommand(
                    "THE QUIET\nBETWEEN",
                    { x: 60, y: 82, width: 460, height: 170 },
                    "#fff3d8",
                    "Segoe UI",
                    62,
                    true,
                    false
                ),
                createTextCommand(
                    "A STUDY IN LIGHT & LAYERS",
                    { x: 65, y: 266, width: 460, height: 36 },
                    "#d0dfe0",
                    "Segoe UI",
                    17,
                    false,
                    false
                ),
                createTextCommand(
                    "SHARPTS   /   FIELD NOTES 001",
                    { x: 65, y: 571, width: 500, height: 30 },
                    "#b7cfd2",
                    "Segoe UI",
                    14,
                    false,
                    false
                )
            ])
        ]
    };
}
