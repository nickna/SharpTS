import { useEffect, useRef, useState } from "./runtime";
import type { PointerEvent, ScrollEvent, KeyEvent } from "./runtime-types";
import { centeredZoomOffset, fitZoom } from "./patterns";

/** Centered scroll/zoom/pan mechanics, independent of an editor's tools or document. @category Hooks and State */
export function useViewportNavigation(options: {
    width: number;
    height: number;
    zoom: number;
    fit: boolean;
    resetKey: string;
    onZoom: (zoom: number, automatic: boolean) => void;
}) {
    const [viewport, setViewport] = useState<ScrollEvent>({
        offsetX: 0,
        offsetY: 0,
        viewportWidth: 0,
        viewportHeight: 0,
        extentWidth: 0,
        extentHeight: 0
    });
    const [offset, setOffset] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
    const [space, setSpace] = useState<boolean>(false);
    const pan = useRef<{ x: number; y: number; offsetX: number; offsetY: number } | null>(null);
    const previous = useRef({ zoom: options.zoom, key: options.resetKey });
    const wheelZoom = useRef<number | null>(null);
    const width = options.width * options.zoom,
        height = options.height * options.zoom;
    const frameWidth = Math.max(viewport.viewportWidth, width + 48);
    const frameHeight = Math.max(viewport.viewportHeight, height + 48);
    const left = (frameWidth - width) / 2,
        top = (frameHeight - height) / 2;
    const anchor = (zoom: number, x: number, y: number, oldZoom: number): void => {
        const oldLeft = Math.max(24, (viewport.viewportWidth - options.width * oldZoom) / 2);
        const oldTop = Math.max(24, (viewport.viewportHeight - options.height * oldZoom) / 2);
        setOffset({
            x: centeredZoomOffset(
                viewport.offsetX,
                x,
                oldZoom,
                zoom,
                oldLeft,
                Math.max(24, (viewport.viewportWidth - options.width * zoom) / 2)
            ),
            y: centeredZoomOffset(
                viewport.offsetY,
                y,
                oldZoom,
                zoom,
                oldTop,
                Math.max(24, (viewport.viewportHeight - options.height * zoom) / 2)
            )
        });
    };
    useEffect(() => {
        const reset = previous.current.key !== options.resetKey;
        if (options.fit && viewport.viewportWidth > 48 && viewport.viewportHeight > 48) {
            const zoom = fitZoom(
                options.width,
                options.height,
                viewport.viewportWidth,
                viewport.viewportHeight
            );
            if (Math.abs(zoom - options.zoom) > 0.000001) options.onZoom(zoom, true);
            setOffset({ x: 0, y: 0 });
        } else if (reset) setOffset({ x: 0, y: 0 });
        else if (previous.current.zoom !== options.zoom && wheelZoom.current !== options.zoom)
            anchor(
                options.zoom,
                viewport.viewportWidth / 2,
                viewport.viewportHeight / 2,
                previous.current.zoom
            );
        previous.current = { zoom: options.zoom, key: options.resetKey };
        wheelZoom.current = null;
    }, [
        options.zoom,
        options.fit,
        options.resetKey,
        options.width,
        options.height,
        viewport.viewportWidth,
        viewport.viewportHeight
    ]);
    return {
        viewport,
        offset,
        width,
        height,
        frameWidth,
        frameHeight,
        left,
        top,
        space,
        setViewport: (event: ScrollEvent): void => {
            setViewport(event);
            setOffset({ x: event.offsetX, y: event.offsetY });
        },
        onKeyDown: (event: KeyEvent): boolean => {
            if (event.key !== "Space" || event.isTextInput) return false;
            setSpace(true);
            return true;
        },
        onKeyUp: (event: KeyEvent): boolean => {
            if (event.key !== "Space") return false;
            setSpace(false);
            pan.current = null;
            return true;
        },
        cancel: (): void => {
            pan.current = null;
            setSpace(false);
        },
        zoomAt: (delta: number, x: number, y: number): void => {
            const zoom = Math.max(0.01, Math.min(8, options.zoom * Math.pow(1.15, delta)));
            anchor(zoom, x, y, options.zoom);
            wheelZoom.current = zoom;
            options.onZoom(zoom, false);
        },
        // Coordinates here are relative to the fixed viewport, not its scrolling content.
        onPointerDown: (event: PointerEvent): boolean => {
            if (!space && event.button !== "middle") return false;
            pan.current = { x: event.x, y: event.y, offsetX: viewport.offsetX, offsetY: viewport.offsetY };
            return true;
        },
        onPointerMove: (event: PointerEvent): boolean => {
            if (!pan.current) return false;
            setOffset({
                x: Math.max(0, pan.current.offsetX + pan.current.x - event.x),
                y: Math.max(0, pan.current.offsetY + pan.current.y - event.y)
            });
            return true;
        },
        onPointerUp: (): boolean => {
            const active = pan.current !== null;
            pan.current = null;
            return active;
        }
    };
}
