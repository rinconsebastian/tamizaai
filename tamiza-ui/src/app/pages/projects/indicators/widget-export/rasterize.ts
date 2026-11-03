// Draws the SVG of a widget's picture as a PNG (add-widget-export, design D7, D11): at twice its size within a pixel budget, marked
// 192 dots per inch so a word processor places it at its size on screen. The SVG goes to an <img> as a `data:` URL, which the
// Content-Security-Policy already allows; a `blob:` image is not, and a canvas that has drawn only that stays untainted.

/** Most pixels a PNG may hold, and most on a side (Safari's canvas limit is 16,777,216 pixels). */
export const maxPngPixels = 16_000_000;
export const maxPngSide = 8192;
/** The largest SVG text that is turned into an image. */
export const maxSvgBytes = 12 * 1024 * 1024;
const targetScale = 2;

/** The scale of a PNG of a picture `width` by `height` CSS pixels: 2, or less to stay within the budget. Under 1, it does not fit. */
export function pngScale(width: number, height: number): number {
  return Math.min(targetScale, Math.sqrt(maxPngPixels / (width * height)), maxPngSide / Math.max(width, height));
}

/** The error code of a PNG that could not be made, for the message that suggests the SVG image. */
export class PngError extends Error {
  override readonly name = 'PngError';

  constructor(readonly reason: 'tooLarge' | 'failed') {
    super(reason);
  }
}

const crcTable = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }
  return table;
})();

/** The CRC-32 of PNG chunks. */
export function crc32(bytes: Uint8Array): number {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

/** Pixels per metre for a picture drawn at `scale` times 96 dots per inch. */
export const pixelsPerMetre = (scale: number) => Math.round((96 * scale) / 0.0254);

/** The PNG with a `pHYs` chunk after its `IHDR`, so it declares its resolution. A PNG that already has one is returned as it is. */
export function withResolution(png: Uint8Array, scale: number): Uint8Array {
  const view = new DataView(png.buffer, png.byteOffset, png.byteLength);
  const headerEnd = 8 + 8 + view.getUint32(8) + 4; // signature, chunk length and type, IHDR data, its CRC
  const type = (at: number) => String.fromCharCode(...png.subarray(at + 4, at + 8));
  for (let at = 8; at < png.length; at += 12 + view.getUint32(at)) {
    if (type(at) === 'pHYs') {
      return png;
    }
  }
  const chunk = new Uint8Array(21);
  const data = new DataView(chunk.buffer);
  data.setUint32(0, 9);
  chunk.set([0x70, 0x48, 0x59, 0x73], 4); // pHYs
  data.setUint32(8, pixelsPerMetre(scale));
  data.setUint32(12, pixelsPerMetre(scale));
  chunk[16] = 1; // metres
  data.setUint32(17, crc32(chunk.subarray(4, 17)));
  const out = new Uint8Array(png.length + chunk.length);
  out.set(png.subarray(0, headerEnd));
  out.set(chunk, headerEnd);
  out.set(png.subarray(headerEnd), headerEnd + chunk.length);
  return out;
}

export interface RasterDeps {
  /** Decodes an image from a URL; in a browser, an `Image` that is decoded. */
  load(url: string): Promise<CanvasImageSource>;
  /** A canvas of this size whose `toBlob` makes a PNG. */
  canvas(width: number, height: number): { context: CanvasRenderingContext2D | null; toBlob(): Promise<Blob | null>; release(): void };
}

/** The browser's way of loading an image and making a canvas. */
export const browserRaster: RasterDeps = {
  load: async (url) => {
    const image = new Image();
    image.src = url;
    await image.decode();
    return image;
  },
  canvas: (width, height) => {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return {
      context: canvas.getContext('2d'),
      toBlob: () => new Promise((resolve) => canvas.toBlob(resolve, 'image/png')),
      release: () => {
        canvas.width = 0;
        canvas.height = 0;
      },
    };
  },
};

/**
 * The PNG of an SVG picture `width` by `height`. Throws `PngError('tooLarge')` when it does not fit the budget even at its size on
 * screen, and `PngError('failed')` when the browser cannot decode or draw it.
 */
export async function rasterize(svg: string, width: number, height: number, deps: RasterDeps = browserRaster): Promise<{ blob: Blob; scale: number }> {
  const scale = pngScale(width, height);
  if (scale < 1 || svg.length > maxSvgBytes) {
    throw new PngError('tooLarge');
  }
  const canvas = deps.canvas(Math.round(width * scale), Math.round(height * scale));
  try {
    if (!canvas.context) {
      throw new PngError('failed');
    }
    const image = await deps.load(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`);
    canvas.context.drawImage(image, 0, 0, Math.round(width * scale), Math.round(height * scale));
    const blob = await canvas.toBlob();
    if (!blob) {
      throw new PngError('failed');
    }
    const bytes = withResolution(new Uint8Array(await blob.arrayBuffer()), scale);
    return { blob: new Blob([bytes as BlobPart], { type: 'image/png' }), scale };
  } catch (error) {
    throw error instanceof PngError ? error : new PngError('failed');
  } finally {
    canvas.release();
  }
}
