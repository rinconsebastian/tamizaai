// The exporter is loaded the first time a member exports an image, as Leaflet is loaded with the first map, so none of it is in the
// dashboard's first download. A load that failed is tried again next time.
let loading: Promise<typeof import('./widget-exporter')> | undefined;

export function loadExporter(): Promise<typeof import('./widget-exporter')> {
  loading ??= import('./widget-exporter');
  loading.catch(() => (loading = undefined));
  return loading;
}
