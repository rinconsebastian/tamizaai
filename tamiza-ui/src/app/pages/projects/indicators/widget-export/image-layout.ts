import { ExportKeyGlyph, ExportLegend, ExportLegendEntry, ExportParts } from '../export-parts';
import { clampFigure } from '../stat-figure';
import { TableLabels, TableTheme, drawTable, fitText } from './svg-table';

// Lays out the picture of a widget (add-widget-export, design D8): the title, the subtitle, the notes above the chart, the caption, the body
// (the chart's own plot, a stat tile's figure or a table), the whole legend, key or scale, the notes below, the footnote, and the footer. It is the one
// place the picture is composed, so the SVG file and the PNG made from it are the same drawing. Pure but for the DOM it builds nodes in.

const SVG_NS = 'http://www.w3.org/2000/svg';

/** The room around the picture, and between its parts. */
const margin = 16;
const gap = 8;
const titleSize = 14;
const noteSize = 12;
const footerSize = 11;
const maxTitleLines = 3;

export interface ImageTheme extends TableTheme {
  /** The white the picture is drawn on. */
  surface: string;
  /** The hairline round an outlined swatch. */
  strong: string;
  /** A warning note, such as rows left out. */
  warning: string;
}

export interface ImageInput {
  /** The width of the widget's body on screen plus the picture's margins; a wide table makes the picture wider. */
  width: number;
  title: string;
  /** The widget's whole subtitle, drawn under the title; null when it has none (add-widget-context, design D8). */
  subtitle: string | null;
  /** The widget's whole footnote, drawn after the notes and before the footer; null when it has none. */
  footnote: string | null;
  /** The tile's notes above the chart ("Not filtered by …", the row limit). */
  notesAbove: string[];
  /**
   * What the tile says under its chart (add-small-sample-warnings, design D11): the small-base note, the base n line and the no-data line.
   * They follow the chart's own notes and come before the footnote.
   */
  notesBelow: string[];
  parts: ExportParts;
  /** The sanitized chart (or the map's picture) as a nested `<svg>` with its own size, or null. */
  plot: SVGSVGElement | null;
  /** A line about what the picture leaves out, such as "The basemap is not included in the image.". */
  notesExtra: string[];
  footer: string[];
  fontFamily: string;
  theme: ImageTheme;
  tableLabels: TableLabels;
  /** A CSS color as the picture should write it: the light theme's, in a form every SVG program reads. */
  resolve(color: string): string;
  /** The width in pixels of a text at a font size and weight. */
  measure(text: string, size: number, weight: number): number;
}

export interface ImageLayout {
  svg: SVGSVGElement;
  width: number;
  height: number;
}

/** Breaks a text into lines of at most `width` pixels at word boundaries, and a word wider than a line at a letter. */
export function wrap(text: string, width: number, measure: (text: string) => number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(/\s+/).filter(Boolean)) {
    const candidate = line ? `${line} ${word}` : word;
    if (measure(candidate) <= width) {
      line = candidate;
      continue;
    }
    if (line) {
      lines.push(line);
      line = '';
    }
    let rest = word;
    while (measure(rest) > width && rest.length > 1) {
      let cut = rest.length - 1;
      while (cut > 1 && measure(rest.slice(0, cut)) > width) {
        cut--;
      }
      lines.push(rest.slice(0, cut));
      rest = rest.slice(cut);
    }
    line = rest;
  }
  return line || lines.length === 0 ? [...lines, line] : lines;
}

/**
 * Breaks a text into lines of at most `width` pixels between words, and never inside one: a word wider than a line stays whole on a line
 * of its own, for the caller to widen the picture to (improve-stat-cards, design D1).
 */
export function wrapWords(text: string, width: number, measure: (text: string) => number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(/\s+/).filter(Boolean)) {
    const candidate = line ? `${line} ${word}` : word;
    if (!line || measure(candidate) <= width) {
      line = candidate;
      continue;
    }
    lines.push(line);
    line = word;
  }
  return line || lines.length === 0 ? [...lines, line] : lines;
}

/** Cuts a list of lines to `max`, ending the last one with an ellipsis when something was cut. */
function limit(lines: string[], max: number, measure: (text: string) => number, width: number): string[] {
  if (lines.length <= max) {
    return lines;
  }
  const kept = lines.slice(0, max);
  let last = kept[max - 1];
  while (last.length > 0 && measure(`${last}…`) > width) {
    last = last.slice(0, -1);
  }
  kept[max - 1] = `${last.trimEnd()}…`;
  return kept;
}

export function composeImage(input: ImageInput): ImageLayout {
  const document = globalThis.document;
  const { theme, measure } = input;
  const body = document.createElementNS(SVG_NS, 'g');
  let y = margin;
  let width = input.width;

  const add = (parent: Element, tag: string, attributes: Record<string, string | number>, text?: string) => {
    const node = document.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(attributes)) {
      node.setAttribute(name, String(value));
    }
    if (text !== undefined) {
      node.textContent = text;
    }
    parent.appendChild(node);
    return node;
  };

  /** Writes wrapped text from `y` down and moves `y` below it. */
  const paragraph = (text: string, size: number, weight: number, color: string, maxLines = Infinity) => {
    const inner = width - margin * 2;
    const measureAt = (value: string) => measure(value, size, weight);
    const lines = limit(wrap(text, inner, measureAt), maxLines, measureAt, inner);
    const lineHeight = Math.round(size * 1.4);
    lines.forEach((line, index) => {
      add(body, 'text', { x: margin, y: y + index * lineHeight + size, 'font-size': size, ...(weight === 400 ? {} : { 'font-weight': weight }), fill: color }, line);
    });
    y += lines.length * lineHeight + gap;
  };

  // The width first: a table can be wider than the widget was, and everything else is wrapped to it.
  const table = input.parts.table ? drawTable(input.parts.table, theme, input.tableLabels, measure) : null;
  if (table) {
    width = Math.max(width, table.width + margin * 2);
  }

  // A stat tile's figure, in the same way: it is never cut, so the picture is as wide as its widest word.
  const stat = input.parts.stat;
  const figureSize = stat ? clampFigure(stat.size) : 0;
  if (stat && !table) {
    const widest = Math.max(0, ...stat.value.split(/\s+/).map((word) => measure(word, figureSize, 600)));
    width = Math.max(width, Math.ceil(widest) + margin * 2);
  }

  paragraph(input.title, titleSize, 500, theme.heading, maxTitleLines);
  if (input.subtitle) {
    paragraph(input.subtitle, noteSize, 400, theme.body);
  }
  for (const note of input.notesAbove) {
    paragraph(note, noteSize, 400, theme.body);
  }
  if (input.parts.caption) {
    paragraph(input.parts.caption, noteSize, 400, theme.body);
  }

  if (table) {
    const holder = add(body, 'svg', { x: margin, y, width: table.width, height: table.height, viewBox: `0 0 ${table.width} ${table.height}` });
    holder.appendChild(table.node);
    y += table.height + gap;
  } else if (stat) {
    // The figure is drawn at full contrast, whatever its base, at the size its tile gave it on screen (improve-stat-cards, design D1),
    // broken only between words.
    const lines = wrapWords(stat.value, width - margin * 2, (text) => measure(text, figureSize, 600));
    const lineHeight = Math.round(figureSize * 1.2);
    lines.forEach((line, index) => {
      add(body, 'text', { x: margin, y: y + index * lineHeight + figureSize, 'font-size': figureSize, 'font-weight': 600, fill: theme.heading }, line);
    });
    y += lines.length * lineHeight + gap;
    if (stat.description) {
      paragraph(stat.description, 14, 400, theme.body);
    }
    for (const line of stat.secondary) {
      paragraph(line, noteSize, 400, theme.body);
    }
  } else if (input.plot) {
    const height = Number(input.plot.getAttribute('height')) || 0;
    const plotWidth = Number(input.plot.getAttribute('width')) || 0;
    input.plot.setAttribute('x', String(margin));
    input.plot.setAttribute('y', String(y));
    body.appendChild(input.plot);
    width = Math.max(width, plotWidth + margin * 2);
    y += height + gap;
  }

  if (input.parts.legend) {
    y = drawLegend(input.parts.legend, body, y);
  }
  for (const note of [...(table?.notes ?? []), ...input.parts.notes, ...input.notesExtra, ...input.notesBelow]) {
    paragraph(note, noteSize, 400, theme.body);
  }
  if (input.footnote) {
    paragraph(input.footnote, noteSize, 400, theme.body);
  }

  if (input.footer.length) {
    y += 4;
    add(body, 'line', { x1: margin, y1: y, x2: width - margin, y2: y, stroke: theme.border, 'stroke-width': 1 });
    y += gap;
    for (const line of input.footer) {
      paragraph(line, footerSize, 400, theme.body);
    }
  }
  const height = y - gap + margin;

  // ---- Legend, key and scale -------------------------------------------------------------------------------------------------

  function drawLegend(legend: ExportLegend, parent: Element, top: number): number {
    const inner = width - margin * 2;
    const line = 18;
    let x = margin;
    let row = 0;
    /** Places an item of `itemWidth` on the current row, or on the next when it does not fit. */
    const place = (itemWidth: number): { x: number; y: number } => {
      if (x > margin && x + itemWidth > margin + inner) {
        x = margin;
        row++;
      }
      const at = { x, y: top + row * line };
      x += itemWidth + 16;
      return at;
    };
    const text = (at: { x: number; y: number }, offset: number, value: string, color = theme.body) =>
      add(parent, 'text', { x: at.x + offset, y: at.y + 11, 'font-size': noteSize, fill: color }, value);
    const measureText = (value: string) => measure(value, noteSize, 400);

    if (legend.kind === 'entries') {
      for (const entry of legend.entries) {
        const label = fitText(entry.detail ? `${entry.label}  ${entry.detail}` : entry.label, inner - 18, measureText);
        const itemWidth = 18 + measureText(label);
        const at = place(Math.min(itemWidth, inner));
        swatch(parent, entry, at, input.resolve(entry.color));
        text(at, 18, label);
      }
    } else if (legend.kind === 'scale') {
      const steps = legend.steps.length * 20;
      const lowWidth = measureText(legend.low);
      const at = place(lowWidth + 8 + steps + 8 + measureText(legend.high));
      text(at, 0, legend.low);
      legend.steps.forEach((color, index) => {
        add(parent, 'rect', { x: at.x + lowWidth + 8 + index * 20, y: at.y + 2, width: 20, height: 10, fill: input.resolve(color), ...(legend.stepOpacity === undefined ? {} : { 'fill-opacity': legend.stepOpacity }) });
      });
      text(at, lowWidth + 8 + steps + 8, legend.high);
      if (legend.empty) {
        const empty = place(18 + measureText(legend.empty));
        add(parent, 'rect', { x: empty.x, y: empty.y + 2, width: 14, height: 10, rx: 2, fill: theme.surface, stroke: theme.strong, 'stroke-width': 1 });
        add(parent, 'line', { x1: empty.x + 2, y1: empty.y + 11, x2: empty.x + 12, y2: empty.y + 3, stroke: theme.strong, 'stroke-width': 1 });
        text(empty, 18, legend.empty);
      }
    } else {
      const accent = input.resolve(legend.accent);
      for (const item of legend.items) {
        const at = place(22 + measureText(item.label));
        glyph(parent, item.glyph, at, accent);
        text(at, 22, item.label);
      }
    }
    return top + (row + 1) * line + gap;
  }

  /** A legend swatch: a square, a short stroke, or a square with a hairline. */
  function swatch(parent: Element, entry: ExportLegendEntry, at: { x: number; y: number }, color: string): void {
    if (entry.swatch === 'line') {
      add(parent, 'line', { x1: at.x, y1: at.y + 7, x2: at.x + 14, y2: at.y + 7, stroke: color, 'stroke-width': 2, 'stroke-linecap': 'round' });
    } else {
      add(parent, 'rect', { x: at.x, y: at.y + 2, width: 10, height: 10, rx: 2, fill: color, ...(entry.swatch === 'outlined' ? { stroke: theme.strong, 'stroke-width': 1 } : {}) });
    }
  }

  /** The marks of a box plot's key, as the page draws them. */
  function glyph(parent: Element, kind: ExportKeyGlyph, at: { x: number; y: number }, accent: string): void {
    const x = at.x;
    const y = at.y + 1;
    switch (kind) {
      case 'box':
        add(parent, 'rect', { x: x + 1, y: y + 1, width: 14, height: 10, rx: 2, fill: accent, 'fill-opacity': 0.2, stroke: accent, 'stroke-width': 1.5 });
        break;
      case 'median':
        add(parent, 'line', { x1: x + 1, y1: y + 6, x2: x + 15, y2: y + 6, stroke: theme.heading, 'stroke-width': 2 });
        break;
      case 'mean':
        add(parent, 'line', { x1: x + 4.5, y1: y + 2.5, x2: x + 11.5, y2: y + 9.5, stroke: theme.heading, 'stroke-width': 1.5 });
        add(parent, 'line', { x1: x + 4.5, y1: y + 9.5, x2: x + 11.5, y2: y + 2.5, stroke: theme.heading, 'stroke-width': 1.5 });
        break;
      case 'whiskers':
        add(parent, 'line', { x1: x + 8, y1: y + 1, x2: x + 8, y2: y + 11, stroke: accent, 'stroke-width': 1.5 });
        add(parent, 'line', { x1: x + 4, y1: y + 1, x2: x + 12, y2: y + 1, stroke: accent, 'stroke-width': 1.5 });
        add(parent, 'line', { x1: x + 4, y1: y + 11, x2: x + 12, y2: y + 11, stroke: accent, 'stroke-width': 1.5 });
        break;
      case 'outliers':
        add(parent, 'circle', { cx: x + 8, cy: y + 6, r: 3, fill: 'none', stroke: accent, 'stroke-width': 1.5 });
        break;
    }
  }

  const root = document.createElementNS(SVG_NS, 'svg');
  root.setAttribute('xmlns', SVG_NS);
  root.setAttribute('width', String(Math.ceil(width)));
  root.setAttribute('height', String(Math.ceil(height)));
  root.setAttribute('viewBox', `0 0 ${Math.ceil(width)} ${Math.ceil(height)}`);
  root.setAttribute('font-family', input.fontFamily);
  add(root, 'rect', { x: 0, y: 0, width: Math.ceil(width), height: Math.ceil(height), fill: theme.surface });
  root.appendChild(body);
  return { svg: root, width: Math.ceil(width), height: Math.ceil(height) };
}
