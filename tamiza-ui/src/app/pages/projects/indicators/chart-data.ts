import { ElementRef, Signal, effect, signal } from '@angular/core';
import { QueryRow } from '../../../core/projects/indicators.service';
import { naturalCompare } from './chart-scales';

export interface Series {
  name: string;
  /** One value per category; null where the indicator has no row for that group. */
  values: (number | null)[];
}

export interface Pivot {
  categories: string[];
  series: Series[];
}

/**
 * Rows to a category-by-series table. Categories are the group values, in the order the rows list them or in natural order
 * (2 before 10); there is one series per indicator, in the order the widget names them. A missing group shows as `emptyLabel`.
 */
export function pivot(rows: readonly QueryRow[], indicators: readonly string[], emptyLabel: string, order: 'listed' | 'natural'): Pivot {
  const none = Symbol('none');
  const keys: (string | typeof none)[] = [];
  const seen = new Set<string | typeof none>();
  for (const row of rows) {
    const key = row.group ?? none;
    if (!seen.has(key)) {
      seen.add(key);
      keys.push(key);
    }
  }
  if (order === 'natural') {
    keys.sort((a, b) => (a === none ? 1 : b === none ? -1 : naturalCompare(a, b)));
  }
  const index = new Map(keys.map((key, i) => [key, i]));
  const names = [...indicators];
  for (const row of rows) {
    if (!names.includes(row.indicator)) {
      names.push(row.indicator);
    }
  }
  const series: Series[] = names.map((name) => ({ name, values: keys.map(() => null) }));
  for (const row of rows) {
    const target = series[names.indexOf(row.indicator)];
    const at = index.get(row.group ?? none)!;
    target.values[at] = (target.values[at] ?? 0) + row.value;
  }
  return { categories: keys.map((key) => (key === none ? emptyLabel : key)), series };
}

export interface Bucket {
  from: number;
  to: number;
  /** Rows per series that fall in `[from, to)`; the last bucket also takes `to`. */
  counts: number[];
}

/**
 * Buckets the plotted value of the returned rows into `count` equal-width ranges between the smallest and the largest value and
 * counts the rows in each, one count per indicator. The ranges are shared by every indicator so they can be compared.
 */
export function histogram(rows: readonly QueryRow[], indicators: readonly string[], count: number): { buckets: Bucket[]; names: string[] } {
  const names = [...indicators];
  for (const row of rows) {
    if (!names.includes(row.indicator)) {
      names.push(row.indicator);
    }
  }
  const values = rows.map((row) => row.value).filter((value) => Number.isFinite(value));
  if (!values.length) {
    return { buckets: [], names };
  }
  const size = Math.max(1, Math.floor(count));
  const min = Math.min(...values);
  let max = Math.max(...values);
  if (max === min) {
    max = min + 1; // every row falls in the first bucket
  }
  const width = (max - min) / size;
  const buckets: Bucket[] = Array.from({ length: size }, (_, i) => ({ from: min + i * width, to: i === size - 1 ? max : min + (i + 1) * width, counts: names.map(() => 0) }));
  for (const row of rows) {
    if (!Number.isFinite(row.value)) {
      continue;
    }
    const at = Math.min(size - 1, Math.floor((row.value - min) / width));
    buckets[at].counts[names.indexOf(row.indicator)]++;
  }
  return { buckets, names };
}

export interface Size {
  width: number;
  height: number;
}

/**
 * The pixel size of an element, kept up to date as the widget is resized, so a chart draws at exactly its box and its text keeps
 * one size. Browsers only: where there is no ResizeObserver (jsdom) the fallback stays.
 */
export function observeSize(target: () => ElementRef<HTMLElement> | undefined, fallback: Size = { width: 480, height: 280 }): Signal<Size> {
  const size = signal(fallback);
  effect((onCleanup) => {
    const element = target()?.nativeElement;
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect;
      if (box && box.width > 0 && box.height > 0) {
        size.set({ width: Math.round(box.width), height: Math.round(box.height) });
      }
    });
    observer.observe(element);
    onCleanup(() => observer.disconnect());
  });
  return size.asReadonly();
}
