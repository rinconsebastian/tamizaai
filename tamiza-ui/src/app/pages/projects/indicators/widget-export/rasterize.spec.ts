import { PngError, RasterDeps, crc32, maxPngPixels, pixelsPerMetre, pngScale, rasterize, withResolution } from './rasterize';

/** A 1x1 PNG, the smallest valid one, as its bytes. */
const tiny = Uint8Array.from(
  atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='),
  (c) => c.charCodeAt(0),
);

describe('pngScale', () => {
  it('is 2 for a usual widget', () => {
    expect(pngScale(600, 340)).toBe(2);
  });

  it('is less than 2 but within 16,000,000 pixels for a tall one', () => {
    const scale = pngScale(1232, 3600);

    expect(scale).toBeLessThan(2);
    expect(scale).toBeGreaterThanOrEqual(1);
    expect(1232 * scale * 3600 * scale).toBeLessThanOrEqual(maxPngPixels);
  });

  it('is under 1 when even its size on screen does not fit', () => {
    expect(pngScale(1232, 20000)).toBeLessThan(1);
  });
});

describe('crc32', () => {
  it('gives the CRC of "123456789"', () => {
    expect(crc32(new TextEncoder().encode('123456789'))).toBe(0xcbf43926);
  });
});

describe('withResolution', () => {
  it('declares 7,559 pixels per metre, 192 dots per inch, at twice the size', () => {
    expect(pixelsPerMetre(2)).toBe(7559);
  });

  it('adds a pHYs chunk right after the header, with a valid CRC, and keeps the rest', () => {
    const out = withResolution(tiny, 2);
    const view = new DataView(out.buffer, out.byteOffset);

    expect(out.length).toBe(tiny.length + 21);
    expect(String.fromCharCode(...out.subarray(37, 41))).toBe('pHYs');
    expect(view.getUint32(33)).toBe(9);
    expect(view.getUint32(41)).toBe(7559);
    expect(view.getUint32(45)).toBe(7559);
    expect(out[49]).toBe(1);
    expect(view.getUint32(50)).toBe(crc32(out.subarray(37, 50)));
    expect(Array.from(out.subarray(0, 33))).toEqual(Array.from(tiny.subarray(0, 33)));
    expect(Array.from(out.subarray(54))).toEqual(Array.from(tiny.subarray(33)));
  });

  it('leaves a PNG that has one alone', () => {
    const once = withResolution(tiny, 2);

    expect(withResolution(once, 3)).toBe(once);
  });
});

describe('rasterize', () => {
  const deps = (overrides: Partial<RasterDeps> = {}, blob: Blob | null = new Blob([tiny as BlobPart], { type: 'image/png' })) => {
    const drawn: number[][] = [];
    const released: number[] = [];
    const made: number[][] = [];
    const raster: RasterDeps = {
      load: async () => ({}) as CanvasImageSource,
      canvas: (width, height) => {
        made.push([width, height]);
        return {
          context: { drawImage: (_i: unknown, x: number, y: number, w: number, h: number) => drawn.push([x, y, w, h]) } as unknown as CanvasRenderingContext2D,
          toBlob: async () => blob,
          release: () => released.push(1),
        };
      },
      ...overrides,
    };
    return { raster, drawn, released, made };
  };

  it('draws a 600 by 340 picture on a 1,200 by 680 canvas and releases the canvas', async () => {
    const { raster, drawn, made, released } = deps();

    const { blob, scale } = await rasterize('<svg/>', 600, 340, raster);

    expect(scale).toBe(2);
    expect(made).toEqual([[1200, 680]]);
    expect(drawn).toEqual([[0, 0, 1200, 680]]);
    expect(released).toHaveLength(1);
    expect(blob.type).toBe('image/png');
    expect(blob.size).toBe(tiny.length + 21);
  });

  it('refuses a picture that does not fit, before it makes a canvas', async () => {
    const { raster, made } = deps();

    await expect(rasterize('<svg/>', 1232, 20000, raster)).rejects.toMatchObject({ reason: 'tooLarge' });
    await expect(rasterize('x'.repeat(13 * 1024 * 1024), 600, 340, raster)).rejects.toMatchObject({ reason: 'tooLarge' });
    expect(made).toEqual([]);
  });

  it('says it failed when the browser cannot decode the picture, taints the canvas or makes no blob, and still releases the canvas', async () => {
    const decode = deps({ load: async () => { throw new Error('decode'); } });
    const noBlob = deps({}, null);

    await expect(rasterize('<svg/>', 600, 340, decode.raster)).rejects.toBeInstanceOf(PngError);
    await expect(rasterize('<svg/>', 600, 340, noBlob.raster)).rejects.toMatchObject({ reason: 'failed' });
    expect(decode.released).toHaveLength(1);
    expect(noBlob.released).toHaveLength(1);
  });

  it('loads the picture from a data URL, never a blob URL', async () => {
    const urls: string[] = [];
    const { raster } = deps({ load: async (url) => { urls.push(url); return {} as CanvasImageSource; } });

    await rasterize('<svg a="1"/>', 600, 340, raster);

    expect(urls[0]).toBe(`data:image/svg+xml;charset=utf-8,${encodeURIComponent('<svg a="1"/>')}`);
  });
});
