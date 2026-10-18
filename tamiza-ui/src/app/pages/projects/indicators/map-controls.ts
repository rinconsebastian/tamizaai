import { CdkMenu, CdkMenuGroup, CdkMenuItem, CdkMenuItemRadio, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/** One basemap the member can choose for this map: `none`, or a kind the deployment offers. */
export interface BasemapChoice {
  id: string;
  label: string;
}

const button =
  'inline-flex size-9 items-center justify-center rounded-base border border-default bg-neutral-primary-soft text-body shadow-xs hover:bg-neutral-tertiary-medium hover:text-heading focus:outline-none focus:ring-4 focus:ring-neutral-tertiary aria-disabled:cursor-not-allowed aria-disabled:opacity-50';
const item = 'block w-full min-h-10 px-4 py-2 text-left text-sm text-body hover:bg-neutral-tertiary-medium hover:text-heading focus:bg-neutral-tertiary-medium focus:outline-none';
const menu = 'z-10 min-w-44 rounded-base border border-default-medium bg-neutral-primary-medium py-2 shadow-lg';

/**
 * The controls of a map (design D5): zoom in, zoom out, "Show all" and, when the deployment offers a basemap, a menu to choose it
 * for oneself. One row at the top end; a map too small for the row gets one "Map options" button that holds the same actions.
 * The menus are the CDK's, with radio items, so arrows move between items, Escape closes and focus returns to the button.
 */
@Component({
  selector: 'app-map-controls',
  imports: [TranslocoDirective, CdkMenu, CdkMenuGroup, CdkMenuItem, CdkMenuItemRadio, CdkMenuTrigger],
  template: `
    <ng-container *transloco="let t">
      <div class="flex items-center gap-1" role="group" [attr.aria-label]="t('indicators.map.controls.label')" data-testid="map-controls">
        @if (compact()) {
          <button type="button" [class]="button" [cdkMenuTriggerFor]="options" [attr.aria-label]="t('indicators.map.controls.options')" data-testid="map-options">
            <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="2" /><circle cx="12" cy="12" r="2" /><circle cx="12" cy="19" r="2" /></svg>
          </button>
        } @else {
          <button type="button" [class]="button" [attr.aria-label]="t('indicators.map.controls.zoomIn')" [attr.aria-disabled]="canZoomIn() ? null : 'true'" (click)="canZoomIn() && zoomIn.emit()" data-testid="map-zoom-in">
            <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 5v14M5 12h14" /></svg>
          </button>
          <button type="button" [class]="button" [attr.aria-label]="t('indicators.map.controls.zoomOut')" [attr.aria-disabled]="canZoomOut() ? null : 'true'" (click)="canZoomOut() && zoomOut.emit()" data-testid="map-zoom-out">
            <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M5 12h14" /></svg>
          </button>
          <button type="button" [class]="button" [attr.aria-label]="t('indicators.map.controls.showAll')" (click)="showAll.emit()" data-testid="map-show-all">
            <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" /></svg>
          </button>
          @if (choices().length) {
            <button type="button" [class]="button" [cdkMenuTriggerFor]="basemaps" [attr.aria-label]="t('indicators.map.controls.basemap')" data-testid="map-basemap">
              <svg aria-hidden="true" class="size-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M12 3 3 8l9 5 9-5-9-5ZM3 13l9 5 9-5" /></svg>
            </button>
          }
        }
      </div>

      <ng-template #options>
        <div cdkMenu [class]="menu" data-testid="map-options-menu">
          <button cdkMenuItem type="button" [class]="item" (cdkMenuItemTriggered)="canZoomIn() && zoomIn.emit()" data-testid="map-menu-zoom-in">{{ t('indicators.map.controls.zoomIn') }}</button>
          <button cdkMenuItem type="button" [class]="item" (cdkMenuItemTriggered)="canZoomOut() && zoomOut.emit()" data-testid="map-menu-zoom-out">{{ t('indicators.map.controls.zoomOut') }}</button>
          <button cdkMenuItem type="button" [class]="item" (cdkMenuItemTriggered)="showAll.emit()" data-testid="map-menu-show-all">{{ t('indicators.map.controls.showAll') }}</button>
          @if (choices().length) {
            <div cdkMenuGroup class="mt-1 border-t border-default-medium pt-1" role="group" [attr.aria-label]="t('indicators.map.controls.basemap')">
              @for (choice of choices(); track choice.id) {
                <button
                  cdkMenuItemRadio
                  type="button"
                  [class]="item + ' aria-checked:font-medium aria-checked:text-heading'"
                  [cdkMenuItemChecked]="choice.id === chosen()"
                  (cdkMenuItemTriggered)="basemap.emit(choice.id)"
                  [attr.data-testid]="'map-basemap-' + choice.id"
                >
                  {{ choice.label }}
                </button>
              }
            </div>
          }
        </div>
      </ng-template>

      <ng-template #basemaps>
        <div cdkMenu [class]="menu" data-testid="map-basemap-menu">
          <div cdkMenuGroup role="group" [attr.aria-label]="t('indicators.map.controls.basemap')">
            @for (choice of choices(); track choice.id) {
              <button
                cdkMenuItemRadio
                type="button"
                [class]="item + ' aria-checked:font-medium aria-checked:text-heading'"
                [cdkMenuItemChecked]="choice.id === chosen()"
                (cdkMenuItemTriggered)="basemap.emit(choice.id)"
                [attr.data-testid]="'map-basemap-' + choice.id"
              >
                {{ choice.label }}
              </button>
            }
          </div>
        </div>
      </ng-template>

    </ng-container>
  `,
  host: { class: 'block' },
})
export class MapControls {
  /** One "Map options" button in place of the row. */
  readonly compact = input(false);
  /** None and the kinds the deployment offers; empty when it offers none, which hides the basemap menu. */
  readonly choices = input<BasemapChoice[]>([]);
  readonly chosen = input('none');
  readonly canZoomIn = input(true);
  readonly canZoomOut = input(true);

  readonly zoomIn = output<void>();
  readonly zoomOut = output<void>();
  readonly showAll = output<void>();
  readonly basemap = output<string>();

  protected readonly button = button;
  protected readonly item = item;
  protected readonly menu = menu;
}
