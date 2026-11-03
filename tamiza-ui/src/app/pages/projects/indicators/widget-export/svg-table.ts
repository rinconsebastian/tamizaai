import { TableModel } from '../widget-table';

// A table as part of an image (add-widget-export, design D9): the header and the rows the image holds, the columns that fit in a
// fixed width, and what it left out. A cross table can have 5,000 cells; the workbook is the complete copy.

const SVG_NS = 'http://www.w3.org/2000/svg';

/** The most body rows an image of a table holds. */
export const maxImageRows = 100;
/** The widest the columns of an image of a table may be together. */
export const maxImageWidth = 2400;
const maxColumnWidth = 240;
const minColumnWidth = 48;
const rowHeight = 22;
const padding = 8;
const fontSize = 12;

export interface TableTheme {
  heading: string;
  body: string;
  border: string;
  /** The shade of the header row. */
  shade: string;
}

export interface TableLabels {
  rowsNotShown(count: number): string;
  columnsNotShown(count: number): string;
  inWorkbook: string;
}

export interface TableDrawing {
  node: SVGGElement;
  width: number;
  height: number;
  /** "28 rows not shown in the image." and the like, to write under the table. */
  notes: string[];
}

/** Text cut to `width` pixels with an ellipsis, whole when it fits. */
export function fitText(text: string, width: number, measure: (text: string) => number): string {
  if (measure(text) <= width) {
    return text;
  }
  let low = 0;
  let high = text.length;
  while (low < high) {
    const middle = Math.ceil((low + high) / 2);
    if (measure(text.slice(0, middle) + '…') <= width) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }
  return text.slice(0, low).trimEnd() + '…';
}

/**
 * Draws a table at `x`, `y`. The first column is always kept; the others are kept, in order, while they fit `maxImageWidth`. Each
 * column is as wide as its widest text, at most 240 pixels, and a cell cut to its column ends in an ellipsis.
 */
export function drawTable(model: TableModel, theme: TableTheme, labels: TableLabels, measure: (text: string, size: number, weight: number) => number): TableDrawing {
  const document = globalThis.document;
  const rows = model.rows.slice(0, maxImageRows);
  const widths = model.headers.map((header, column) => {
    const widest = Math.max(measure(header, fontSize, 600), ...rows.map((row) => measure(row[column] ?? '—', fontSize, 400)));
    return Math.min(maxColumnWidth, Math.max(minColumnWidth, Math.ceil(widest) + padding * 2));
  });
  let kept = 1;
  let total = widths[0] ?? 0;
  while (kept < widths.length && total + widths[kept] <= maxImageWidth) {
    total += widths[kept];
    kept++;
  }

  const group = document.createElementNS(SVG_NS, 'g');
  const add = (tag: string, attributes: Record<string, string | number>, text?: string) => {
    const node = document.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(attributes)) {
      node.setAttribute(name, String(value));
    }
    if (text !== undefined) {
      node.textContent = text;
    }
    group.appendChild(node);
  };

  const align = (column: number) => model.align?.[column] ?? (column === 0 ? 'start' : 'end');
  const lastColumn = model.headers.length - 1;
  const line = (index: number, header: boolean, bold: boolean, cells: (string | null)[]) => {
    const y = index * rowHeight;
    if (header) {
      add('rect', { x: 0, y, width: total, height: rowHeight, fill: theme.shade });
    }
    add('line', { x1: 0, y1: y + rowHeight, x2: total, y2: y + rowHeight, stroke: theme.border, 'stroke-width': 1 });
    let x = 0;
    for (let column = 0; column < kept; column++) {
      const weight = header || bold || (model.totals?.column && column === lastColumn) ? 600 : 400;
      const text = fitText(cells[column] ?? '—', widths[column] - padding * 2, (value) => measure(value, fontSize, weight));
      const end = align(column) === 'end';
      add(
        'text',
        {
          x: end ? x + widths[column] - padding : x + padding,
          y: y + rowHeight - 7,
          'font-size': fontSize,
          'font-weight': weight === 600 ? 600 : 400,
          fill: header || weight === 600 || column === 0 ? theme.heading : theme.body,
          ...(end ? { 'text-anchor': 'end' } : {}),
        },
        text,
      );
      x += widths[column];
    }
  };

  line(0, true, false, model.headers);
  rows.forEach((row, index) => line(index + 1, false, !!model.totals?.row && index === model.rows.length - 1, row));

  const notes: string[] = [];
  if (model.rows.length > rows.length) {
    notes.push(labels.rowsNotShown(model.rows.length - rows.length));
  }
  if (kept < widths.length) {
    notes.push(labels.columnsNotShown(widths.length - kept));
  }
  if (notes.length) {
    notes.push(labels.inWorkbook);
  }
  return { node: group, width: total, height: (rows.length + 1) * rowHeight, notes };
}
