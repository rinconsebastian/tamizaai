import { MapPicture, MapPoint } from '../map-engine';

// A map's picture as SVG (add-widget-export, design D10): the shapes in the member's view, in the colors of the widget's scale at full
// opacity, clipped to the view. Never a tile, the Google logo or a credit: the image says the basemap is not in it.

const SVG_NS = 'http://www.w3.org/2000/svg';
const polygonWeight = 1;
const markerWeight = 2;
const markerRadius = 6;

/** A coordinate with one decimal: more adds nothing to a picture. */
const round = (value: number) => Math.round(value * 10) / 10;
const at = (point: MapPoint) => `${round(point.x)} ${round(point.y)}`;

/**
 * The picture as a nested `<svg>` of the view's size. `resolve` writes a CSS color (`var(--tmz-seq-3)`) as the picture should have it;
 * `id` names the clip path and must be unique in the image.
 */
export function mapPictureToSvg(picture: MapPicture, resolve: (css: string) => string, id: string): SVGSVGElement {
  const document = globalThis.document;
  const make = (tag: string, attributes: Record<string, string | number>) => {
    const node = document.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(attributes)) {
      node.setAttribute(name, String(value));
    }
    return node;
  };

  const root = make('svg', { width: picture.width, height: picture.height, viewBox: `0 0 ${picture.width} ${picture.height}` }) as SVGSVGElement;
  const defs = make('defs', {});
  const clip = make('clipPath', { id });
  clip.appendChild(make('rect', { x: 0, y: 0, width: picture.width, height: picture.height }));
  defs.appendChild(clip);
  root.appendChild(defs);

  const surface = resolve('var(--color-neutral-primary-soft, #fff)');
  const group = make('g', { 'clip-path': `url(#${id})` });
  for (const shape of picture.shapes) {
    const fill = resolve(`var(--tmz-seq-${shape.step})`);
    if (shape.point) {
      group.appendChild(make('circle', { cx: round(shape.point.x), cy: round(shape.point.y), r: markerRadius, fill, stroke: surface, 'stroke-width': markerWeight }));
      continue;
    }
    // All the rings of a region in one path, so a hole is a hole whichever way it winds.
    const d = shape.polygons.flatMap((rings) => rings.map((ring) => `M${ring.map(at).join('L')}Z`)).join('');
    group.appendChild(make('path', { d, 'fill-rule': 'evenodd', fill, stroke: surface, 'stroke-width': polygonWeight, 'stroke-linejoin': 'round' }));
  }
  root.appendChild(group);
  return root;
}
