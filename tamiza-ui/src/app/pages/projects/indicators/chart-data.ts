import { DestroyRef, ElementRef, Signal, effect, inject, signal } from '@angular/core';
import { QueryRow } from '../../../core/projects/indicators.service';
import { naturalCompare } from './chart-scales';
import { DashboardPrint } from './dashboard-print';

export interface Pivot {
  categories: string[];
  values: number[];
}

/**
 * Rows to categories and values. Categories are the group values, in the order the rows list them or in natural order (2 before
 * 10); a missing group shows as `emptyLabel`. The server sends one row per group value.
 */
export function pivot(rows: readonly QueryRow[], emptyLabel: string, order: 'listed' | 'natural'): Pivot {
  const listed = rows.map((row) => ({ category: row.group ?? emptyLabel, missing: row.group === null, value: row.value }));
  if (order === 'natural') {
    listed.sort((a, b) => (a.missing ? 1 : b.missing ? -1 : naturalCompare(a.category, b.category)));
  }
  return { categories: listed.map((entry) => entry.category), values: listed.map((entry) => entry.value) };
}

export interface Bucket {
  from: number;
  to: number;
  /** Rows that fall in `[from, to)`; the last bucket also takes `to`. */
  count: number;
}

/**
 * Buckets the plotted value of the returned rows into `count` equal-width ranges between the smallest and the largest value and
 * counts the rows in each.
 */
export function histogram(rows: readonly QueryRow[], count: number): Bucket[] {
  const values = rows.map((row) => row.value).filter((value) => Number.isFinite(value));
  if (!values.length) {
    return [];
  }
  const size = Math.max(1, Math.floor(count));
  const min = Math.min(...values);
  let max = Math.max(...values);
  if (max === min) {
    max = min + 1; // every row falls in the first bucket
  }
  const width = (max - min) / size;
  const buckets: Bucket[] = Array.from({ length: size }, (_, i) => ({ from: min + i * width, to: i === size - 1 ? max : min + (i + 1) * width, count: 0 }));
  for (const value of values) {
    buckets[Math.min(size - 1, Math.floor((value - min) / width))].count++;
  }
  return buckets;
}

export interface Size {
  width: number;
  height: number;
}

/**
 * The pixel size of an element, kept up to date as the widget is resized, so a chart draws at exactly its box and its text keeps
 * one size. Browsers only: where there is no ResizeObserver (jsdom) the fallback stays. On the dashboard page it is also read
 * when the page prepares the print, where no ResizeObserver fires in time (dashboard printing, design D5).
 */
export function observeSize(target: () => ElementRef<HTMLElement> | undefined, fallback: Size = { width: 480, height: 280 }): Signal<Size> {
  const size = signal(fallback);
  const print = inject(DashboardPrint, { optional: true });
  if (print) {
    const unregister = print.registerMeasure(() => {
      const box = target()?.nativeElement.getBoundingClientRect();
      if (box && box.width > 0 && box.height > 0) {
        size.set({ width: Math.round(box.width), height: Math.round(box.height) });
      }
    });
    inject(DestroyRef).onDestroy(unregister);
  }
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
