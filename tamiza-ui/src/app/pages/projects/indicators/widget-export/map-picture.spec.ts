import { MapPicture } from '../map-engine';
import { mapPictureToSvg } from './map-picture';

const resolve = (css: string) => (css.startsWith('var(--tmz-seq-') ? `rgb(${css.match(/\d+/)![0]}, 0, 0)` : '#fff');

const picture: MapPicture = {
  width: 600,
  height: 400,
  shapes: [
    { step: 7, point: null, polygons: [[[{ x: 10, y: 10 }, { x: 110.26, y: 10 }, { x: 110, y: 90 }], [{ x: 30, y: 30 }, { x: 50, y: 30 }, { x: 50, y: 50 }]]] },
    { step: 2, point: { x: 300.04, y: 200 }, polygons: [] },
  ],
};

describe('mapPictureToSvg', () => {
  const svg = mapPictureToSvg(picture, resolve, 'wx-clip');

  it('is the size of the view, clipped to it', () => {
    expect(svg.getAttribute('viewBox')).toBe('0 0 600 400');
    expect(svg.querySelector('clipPath')!.getAttribute('id')).toBe('wx-clip');
    expect(svg.querySelector('clipPath rect')!.getAttribute('width')).toBe('600');
    expect(svg.querySelector('g')!.getAttribute('clip-path')).toBe('url(#wx-clip)');
  });

  it('draws a region as one even-odd path with its hole, in the color of its step at full opacity', () => {
    const path = svg.querySelector('path')!;

    expect(path.getAttribute('d')).toBe('M10 10L110.3 10L110 90Z' + 'M30 30L50 30L50 50Z');
    expect(path.getAttribute('fill-rule')).toBe('evenodd');
    expect(path.getAttribute('fill')).toBe('rgb(7, 0, 0)');
    expect(path.getAttribute('stroke-width')).toBe('1');
    expect(path.hasAttribute('fill-opacity')).toBe(false);
  });

  it('draws a point as a circle of radius 6 with a 2 pixel edge', () => {
    const circle = svg.querySelector('circle')!;

    expect([circle.getAttribute('cx'), circle.getAttribute('cy'), circle.getAttribute('r'), circle.getAttribute('stroke-width')]).toEqual(['300', '200', '6', '2']);
    expect(circle.getAttribute('fill')).toBe('rgb(2, 0, 0)');
  });

  it('holds no image, tile, link or credit', () => {
    const markup = new XMLSerializer().serializeToString(svg);

    expect(markup).not.toMatch(/<image|<img|href|<text|<foreignObject/);
  });
});
