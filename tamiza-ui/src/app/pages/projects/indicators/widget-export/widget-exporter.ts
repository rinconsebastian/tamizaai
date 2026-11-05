import { ExportRegistration } from '../export-parts';
import { MapPicture } from '../map-engine';
import { FooterFacts, footerLines } from './footer';
import { ImageTheme, composeImage } from './image-layout';
import { mapPictureToSvg } from './map-picture';
import { RasterDeps, browserRaster, rasterize } from './rasterize';
import { TableLabels } from './svg-table';
import { CleanOptions, computedStyle, inProbe, inheritedStyle, sanitizeSvg, srgbColor } from './svg-clean';

// Makes the picture of one widget (add-widget-export, design D5 to D8): settle the widget, clone its chart into a probe, write what the
// page computed for it as attributes, compose the picture around it, and, for a PNG, draw that SVG on a canvas. This module and its
// neighbours are loaded on first use (`loadExporter`), so none of it is in the dashboard's first download.

export type ImageFormat = 'png' | 'svg';

export interface ImageJob {
  format: ImageFormat;
  title: string;
  /** The widget's subtitle and footnote, whole; null when it has none (add-widget-context, design D8). */
  subtitle: string | null;
  footnote: string | null;
  /** The widget hides its caption: the picture leaves out the line that names what is plotted, and a stat tile's description. */
  captionHidden: boolean;
  /** The width of the widget's body on screen, to which the picture adds its margins. */
  bodyWidth: number;
  registration: ExportRegistration;
  /** The tile's own notes above its chart ("Not filtered by …", the row limit). */
  notesAbove: string[];
  /** The map's shapes in the member's view when the widget is a map drawn as a chart; null otherwise. */
  mapPicture: MapPicture | null;
  /** "The basemap is not included in the image.", when the member shows a basemap under the map; else null. */
  basemapNote: string | null;
  facts: FooterFacts;
  tableLabels: TableLabels;
  /** Closes every tooltip of the page. */
  closeTooltips(): void;
}

/** What the picture needs to ask the page: the theme's colors, the font and the width of a text. Injected so a test needs no browser. */
export interface ImageEnvironment {
  theme: ImageTheme;
  fontFamily: string;
  resolve(css: string): string;
  measure(text: string, size: number, weight: number): number;
  clean: CleanOptions;
  /** Puts a clone where the page's styles reach it, runs `work` and takes it away. */
  probe<T>(source: Element, work: (clone: Element) => T): T;
  dispose(): void;
}

export interface ImageResult {
  /** The SVG file's text. */
  svg: string;
  width: number;
  height: number;
  /** The file to save: the SVG, or the PNG drawn from it. */
  blob: Blob;
}

const SVG_TYPE = 'image/svg+xml;charset=utf-8';
/** The narrowest a picture is, whatever the widget was on screen (design D7). */
const minWidth = 352;

/** A new prefix for the ids of one picture. */
const newPrefix = () => `wx${Math.floor(Math.random() * 36 ** 5).toString(36)}-`;

/** The environment of the open page: the light theme's colors, the system font stack and a canvas to measure text with. */
export function browserEnvironment(document: Document, anchor: Element): ImageEnvironment {
  const hostStyle = inheritedStyle(anchor);
  const probe = document.createElement('div');
  probe.setAttribute('aria-hidden', 'true');
  probe.setAttribute('inert', '');
  probe.setAttribute('style', `${hostStyle};position:absolute;left:-100000px;top:0;visibility:hidden;pointer-events:none`);
  const swatch = document.createElement('span');
  probe.appendChild(swatch);
  document.body.appendChild(probe);

  const toRgb = srgbColor(document);
  const view = document.defaultView!;
  const resolve = (css: string) => {
    swatch.style.color = '';
    swatch.style.color = css;
    return toRgb(view.getComputedStyle(swatch).color);
  };
  const fontFamily = view.getComputedStyle(document.documentElement).getPropertyValue('--font-sans').trim() || 'system-ui, sans-serif';
  const context = document.createElement('canvas').getContext('2d');
  const measure = (text: string, size: number, weight: number) => {
    if (!context) {
      return text.length * size * 0.55;
    }
    context.font = `${weight} ${size}px ${fontFamily}`;
    return context.measureText(text).width;
  };

  return {
    theme: {
      heading: resolve('var(--color-heading, #111827)'),
      body: resolve('var(--color-body, #4b5563)'),
      border: resolve('var(--color-default, #e5e7eb)'),
      shade: resolve('var(--color-neutral-secondary-soft, #f9fafb)'),
      strong: resolve('var(--color-default-strong, #d1d5db)'),
      warning: resolve('var(--color-fg-warning, #b45309)'),
      surface: 'rgb(255, 255, 255)',
    },
    fontFamily,
    resolve,
    measure,
    clean: { styleOf: computedStyle, prefix: '', color: toRgb },
    probe: (source, work) => inProbe(source, hostStyle, null, work),
    dispose: () => probe.remove(),
  };
}

/** Makes the picture of a widget as an SVG file, or as the PNG drawn from it. */
export async function makeImage(job: ImageJob, environment: ImageEnvironment, raster: RasterDeps = browserRaster): Promise<ImageResult> {
  try {
    // The chart at rest: no tooltip, no mark pointed at, nothing of the member's pointer in it.
    job.closeTooltips();
    job.registration.settle();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    const shown = job.registration.parts();
    // An image holds no text meant only for screen readers, so a hidden caption is left out of it (design D3, D8).
    const parts = job.captionHidden ? { ...shown, caption: null, stat: shown.stat ? { ...shown.stat, description: null } : null } : shown;
    const prefix = newPrefix();
    const source = job.registration.plot();
    let plot: SVGSVGElement | null = null;
    if (source) {
      plot = environment.probe(source, (clone) => sanitizeSvg(clone, { ...environment.clean, prefix }));
    } else if (job.mapPicture) {
      plot = mapPictureToSvg(job.mapPicture, environment.resolve, `${prefix}clip`);
    }

    const layout = composeImage({
      width: Math.max(minWidth, job.bodyWidth + 32),
      title: job.title,
      subtitle: job.subtitle,
      footnote: job.footnote,
      notesAbove: job.notesAbove,
      parts,
      plot,
      notesExtra: job.basemapNote ? [job.basemapNote] : [],
      footer: footerLines(job.facts),
      fontFamily: environment.fontFamily,
      theme: environment.theme,
      tableLabels: job.tableLabels,
      resolve: environment.resolve,
      measure: environment.measure,
    });
    const markup = new XMLSerializer().serializeToString(layout.svg);
    const file = `<?xml version="1.0" encoding="UTF-8"?>\n${markup}`;
    const blob = job.format === 'svg' ? new Blob([file], { type: SVG_TYPE }) : (await rasterize(markup, layout.width, layout.height, raster)).blob;
    return { svg: file, width: layout.width, height: layout.height, blob };
  } finally {
    environment.dispose();
  }
}
