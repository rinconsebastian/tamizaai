import { LOCALE_ID, Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { QueryRow, WidgetConfig } from '../../../core/projects/indicators.service';
import { observeSize } from './chart-data';
import { Point, plainNumber, ringsPath, sequentialColor, sequentialStep, sequentialSteps } from './chart-scales';
import { Geometry, Position, parseWkt, positionsOf } from './wkt';
import { TableModel, WidgetTable } from './widget-table';

type Translate = (key: string, params?: Record<string, unknown>) => string;

interface Shape {
  group: string;
  value: number;
  /** A filled region, or a marker. */
  kind: 'polygon' | 'point';
  d: string | null;
  cx: number;
  cy: number;
  color: string;
}

const padding = 10;
const markerRadius = 6;

/**
 * A map widget (design D6): the rows' own geometry (WKT text) in relative position, projected with one linear transform fitted
 * to the extent of the rows drawn. Polygons are filled regions and points are markers, both colored by the plotted field on a
 * sequential scale of seven steps. There is no basemap. A row whose geometry is missing or cannot be read is left out and counted.
 */
@Component({
  selector: 'app-map-widget',
  imports: [TranslocoDirective, WidgetTable],
  template: `
    <ng-container *transloco="let t">
      <p class="shrink-0 text-xs text-body" data-testid="chart-caption">{{ t('indicators.map.caption', { indicator: config().indicators[0], field: t('indicators.valueFields.' + config().valueField), group: config().groupBy }) }}</p>
      @if (asTable()) {
        <app-widget-table class="flex-1 overflow-auto" [model]="table(t)" />
      } @else {
        <div #box class="relative min-h-0 flex-1" data-testid="chart-box">
          <svg role="img" class="absolute inset-0 block" [attr.width]="frame().width" [attr.height]="frame().height" [attr.viewBox]="'0 0 ' + frame().width + ' ' + frame().height" [attr.aria-label]="summary(t)" data-testid="chart-svg">
            @for (shape of shapes(); track $index) {
              @if (shape.kind === 'polygon') {
                <path class="stroke-neutral-primary-soft hover:opacity-80" stroke-width="1" fill-rule="evenodd" stroke-linejoin="round" [attr.d]="shape.d" [style.fill]="shape.color" data-testid="map-region">
                  <title>{{ shape.group }}: {{ format(shape.value) }}</title>
                </path>
              } @else {
                <circle class="stroke-neutral-primary-soft hover:opacity-80" stroke-width="2" [attr.cx]="shape.cx" [attr.cy]="shape.cy" [attr.r]="markerRadius" [style.fill]="shape.color" data-testid="map-point">
                  <title>{{ shape.group }}: {{ format(shape.value) }}</title>
                </circle>
              }
            }
          </svg>
        </div>
        @if (shapes().length) {
          <div class="flex shrink-0 flex-wrap items-center gap-2 text-xs text-body" data-testid="map-legend">
            <span class="tabular-nums" data-testid="map-legend-min">{{ format(range().min) }}</span>
            <span class="flex" aria-hidden="true">
              @for (step of steps; track step) {
                <span class="h-2.5 w-5 first:rounded-s-sm last:rounded-e-sm" [style.background]="color(step)"></span>
              }
            </span>
            <span class="tabular-nums" data-testid="map-legend-max">{{ format(range().max) }}</span>
          </div>
        }
        @if (leftOut() > 0) {
          <p class="shrink-0 text-xs text-fg-warning" data-testid="map-left-out">{{ t('indicators.map.leftOut', { count: leftOut() }) }}</p>
        }
        @if (!shapes().length) {
          <p class="shrink-0 text-sm text-body" data-testid="map-nothing">{{ t('indicators.map.nothing') }}</p>
        }
      }
    </ng-container>
  `,
  host: { class: 'flex h-full min-h-0 flex-col gap-2' },
})
export class MapWidget {
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LOCALE_ID);

  readonly rows = input.required<QueryRow[]>();
  readonly config = input.required<WidgetConfig>();
  readonly asTable = input(false);

  protected readonly markerRadius = markerRadius;
  protected readonly steps = Array.from({ length: sequentialSteps }, (_, i) => i + 1);

  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  private readonly size = observeSize(() => this.box());
  private readonly plain = plainNumber(this.locale);

  private readonly parsed = computed(() => this.rows().map((row) => ({ row, geometry: parseWkt(row.geometry) })));
  protected readonly leftOut = computed(() => this.parsed().filter((entry) => entry.geometry === null).length);

  protected readonly frame = computed(() => ({ width: Math.max(120, this.size().width), height: Math.max(100, this.size().height) }));

  protected readonly range = computed(() => {
    const values = this.parsed()
      .filter((entry) => entry.geometry !== null)
      .map((entry) => entry.row.value);
    return values.length ? { min: Math.min(...values), max: Math.max(...values) } : { min: 0, max: 0 };
  });

  protected readonly shapes = computed<Shape[]>(() => {
    const drawn = this.parsed().filter((entry): entry is { row: QueryRow; geometry: Geometry } => entry.geometry !== null);
    if (!drawn.length) {
      return [];
    }
    const project = this.projection(drawn.flatMap((entry) => positionsOf(entry.geometry)));
    const { min, max } = this.range();
    return drawn.map(({ row, geometry }) => {
      const color = sequentialColor(sequentialStep(row.value, min, max));
      const group: string = row.group ?? this.transloco.translate('indicators.chart.noGroup');
      if (geometry.type === 'point') {
        const at = project(geometry.position);
        return { group, value: row.value, kind: 'point', d: null, cx: at.x, cy: at.y, color };
      }
      const polygons = geometry.type === 'polygon' ? [geometry.rings] : geometry.polygons;
      const d = polygons.map((rings) => ringsPath(rings.map((ring) => ring.map(project)))).join(' ');
      return { group, value: row.value, kind: 'polygon', d, cx: 0, cy: 0, color };
    });
  });

  /**
   * One linear (equirectangular) transform for every position: fitted to their bounding box, north up, the same scale on both
   * axes. Longitude is narrowed by the cosine of the middle latitude when the numbers are degrees.
   */
  private projection(positions: readonly Position[]): (position: Position) => Point {
    const xs = positions.map((p) => p[0]);
    const ys = positions.map((p) => p[1]);
    const minX = Math.min(...xs);
    const maxX = Math.max(...xs);
    const minY = Math.min(...ys);
    const maxY = Math.max(...ys);
    const degrees = minX >= -180 && maxX <= 180 && minY >= -90 && maxY <= 90;
    const narrow = degrees ? Math.max(0.05, Math.cos((((minY + maxY) / 2) * Math.PI) / 180)) : 1;
    const spanX = (maxX - minX) * narrow;
    const spanY = maxY - minY;
    const { width, height } = this.frame();
    const room = { width: width - 2 * padding, height: height - 2 * padding };
    const scale = Math.min(spanX > 0 ? room.width / spanX : Infinity, spanY > 0 ? room.height / spanY : Infinity);
    const factor = Number.isFinite(scale) ? scale : 1;
    const left = padding + (room.width - spanX * factor) / 2;
    const top = padding + (room.height - spanY * factor) / 2;
    return ([x, y]) => ({ x: left + (x - minX) * narrow * factor, y: top + (maxY - y) * factor });
  }

  protected color(step: number): string {
    return sequentialColor(step);
  }

  protected format(value: number): string {
    return this.plain(value);
  }

  protected summary(t: Translate): string {
    const shapes = this.shapes();
    const { min, max } = this.range();
    return t('indicators.map.summary', {
      indicator: this.config().indicators[0],
      count: shapes.length,
      min: this.plain(min),
      max: this.plain(max),
      left: this.leftOut(),
    });
  }

  protected table(t: Translate): TableModel {
    return {
      headers: [this.config().groupBy, t(`indicators.valueFields.${this.config().valueField}`), t('indicators.map.geometry')],
      rows: this.parsed().map(({ row, geometry }) => [
        row.group ?? t('indicators.chart.noGroup'),
        row.value,
        geometry === null ? t('indicators.map.kind.none') : t(`indicators.map.kind.${geometry.type === 'point' ? 'point' : 'polygon'}`),
      ]),
    };
  }
}
